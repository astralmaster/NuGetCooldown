using System.Diagnostics;
using NuGetCooldown.Configuration;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using NuGetCooldown.Projects;
using NuGetCooldown.Reporting;

namespace NuGetCooldown.Checking;

/// <summary>
/// Evaluates the cooldown policy: every unique resolved package version is looked up once
/// (concurrently), then judged against the cooldown window and the configured policies.
/// </summary>
public sealed class CooldownChecker(
    IPackagePublishInfoProvider provider,
    TimeProvider timeProvider,
    int maxConcurrency = 8)
{
    /// <summary>Runs the check across <paramref name="projects"/>.</summary>
    public async Task<CheckReport> CheckAsync(
        IReadOnlyList<ProjectPackages> projects,
        CooldownSettings settings,
        string toolVersion,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        var stopwatch = Stopwatch.StartNew();

        var occurrences = CollectOccurrences(projects, settings.Scope);
        var (allowed, toCheck) = Partition(occurrences, settings.Allow);
        var lookups = await LookUpAsync(toCheck, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var results = new List<PackageResult>(occurrences.Count);

        results.AddRange(allowed.Select(o => new PackageResult
        {
            Package = o.Package,
            Status = PackageStatus.Allowed,
            Severity = Severity.None,
            IsDirect = o.IsDirect,
            Projects = o.Projects,
            Message = "matched an allow-list entry",
        }));

        results.AddRange(lookups.Select(l => Evaluate(l.Occurrence, l.Result, settings, now)));

        results.Sort(CompareResults);

        return new CheckReport
        {
            ToolVersion = toolVersion,
            CheckedAtUtc = now,
            CooldownDays = settings.CooldownDays,
            Scope = settings.Scope,
            Sources = settings.Sources,
            ProjectNames = projects.Select(p => p.ProjectName).Distinct().Order().ToArray(),
            Results = results,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
        };
    }

    private sealed record Occurrence(PackageIdentity Package, bool IsDirect, IReadOnlyList<string> Projects);

    private static List<Occurrence> CollectOccurrences(
        IReadOnlyList<ProjectPackages> projects,
        DependencyScope scope)
    {
        var map = new Dictionary<PackageIdentity, (bool Direct, SortedSet<string> Projects)>();

        foreach (var project in projects)
        {
            foreach (var package in project.Packages)
            {
                if (scope == DependencyScope.Direct && !package.IsDirect)
                {
                    continue;
                }

                if (map.TryGetValue(package.Identity, out var existing))
                {
                    existing.Projects.Add(project.ProjectName);
                    if (package.IsDirect && !existing.Direct)
                    {
                        map[package.Identity] = (true, existing.Projects);
                    }
                }
                else
                {
                    map[package.Identity] = (
                        package.IsDirect,
                        new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { project.ProjectName });
                }
            }
        }

        return map
            .Select(kv => new Occurrence(kv.Key, kv.Value.Direct, [.. kv.Value.Projects]))
            .ToList();
    }

    private static (List<Occurrence> Allowed, List<Occurrence> ToCheck) Partition(
        List<Occurrence> occurrences,
        AllowList allowList)
    {
        var allowed = new List<Occurrence>();
        var toCheck = new List<Occurrence>();
        foreach (var occurrence in occurrences)
        {
            (allowList.IsAllowed(occurrence.Package) ? allowed : toCheck).Add(occurrence);
        }

        return (allowed, toCheck);
    }

    private async Task<IReadOnlyList<(Occurrence Occurrence, PublishLookupResult Result)>> LookUpAsync(
        List<Occurrence> occurrences,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(maxConcurrency);

        var tasks = occurrences.Select(async occurrence =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await provider
                    .GetPublishInfoAsync(occurrence.Package, cancellationToken)
                    .ConfigureAwait(false);
                return (occurrence, result);
            }
            finally
            {
                throttler.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static PackageResult Evaluate(
        Occurrence occurrence,
        PublishLookupResult lookup,
        CooldownSettings settings,
        DateTimeOffset now)
    {
        var result = EvaluateCore(occurrence, lookup, settings, now);

        if (settings.WarnOnly && result.Severity == Severity.Error)
        {
            result = result with { Severity = Severity.Warning };
        }

        return result;
    }

    private static PackageResult EvaluateCore(
        Occurrence occurrence,
        PublishLookupResult lookup,
        CooldownSettings settings,
        DateTimeOffset now)
    {
        var baseResult = new PackageResult
        {
            Package = occurrence.Package,
            Status = PackageStatus.Ok,
            Severity = Severity.None,
            IsDirect = occurrence.IsDirect,
            Projects = occurrence.Projects,
        };

        switch (lookup.Outcome)
        {
            case LookupOutcome.Error:
                return baseResult with
                {
                    Status = PackageStatus.FeedError,
                    Severity = ToSeverity(settings.OnFeedError),
                    DiagnosticCode = DiagnosticCodes.FeedError,
                    Message = $"could not query the configured sources: {lookup.Message}",
                };

            case LookupOutcome.NotFound:
                return baseResult with
                {
                    Status = PackageStatus.Unknown,
                    Severity = ToSeverity(settings.OnUnknown),
                    DiagnosticCode = DiagnosticCodes.UnknownPublishDate,
                    Message = lookup.Message ?? "not found on any configured source",
                };
        }

        var info = lookup.Info!;
        baseResult = baseResult with
        {
            Listed = info.Listed,
            SourceUrl = info.SourceUrl,
            FromCache = info.FromCache,
        };

        if (info.PublishedUtc is not { } published)
        {
            return baseResult with
            {
                Status = PackageStatus.Unknown,
                Severity = ToSeverity(settings.OnUnknown),
                DiagnosticCode = DiagnosticCodes.UnknownPublishDate,
                Message = info.Listed
                    ? "the source reported no publish date"
                    : "the version is unlisted and no publish date could be recovered",
            };
        }

        var ageDays = (now - published).TotalDays;
        baseResult = baseResult with { PublishedUtc = published, AgeDays = ageDays };

        if (ageDays < settings.CooldownDays)
        {
            var remaining = settings.CooldownDays - ageDays;
            var unlistedNote = info.Listed ? "" : " — and the version is unlisted";
            return baseResult with
            {
                Status = PackageStatus.Violation,
                Severity = Severity.Error,
                DiagnosticCode = DiagnosticCodes.Violation,
                Message = $"published {FormatDays(ageDays)} ago; cooldown is {settings.CooldownDays} days"
                          + $" ({FormatDays(remaining)} remaining){unlistedNote}",
            };
        }

        if (!info.Listed)
        {
            return baseResult with
            {
                Status = PackageStatus.Unlisted,
                Severity = ToSeverity(settings.OnUnlisted),
                DiagnosticCode = DiagnosticCodes.Unlisted,
                Message = "the version is unlisted on its source (possibly withdrawn — check why)",
            };
        }

        return baseResult;
    }

    private static Severity ToSeverity(PolicyAction action) => action switch
    {
        PolicyAction.Error => Severity.Error,
        PolicyAction.Warn => Severity.Warning,
        _ => Severity.None,
    };

    /// <summary>"0.8 days" / "1 day" / "12.4 days" — one decimal, trimmed for whole numbers.</summary>
    public static string FormatDays(double days)
    {
        var rounded = Math.Round(days, 1);
        var text = rounded.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return text == "1" ? "1 day" : $"{text} days";
    }

    private static int CompareResults(PackageResult a, PackageResult b)
    {
        var bySeverity = b.Severity.CompareTo(a.Severity);
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        var byStatus = a.Status.CompareTo(b.Status);
        if (byStatus != 0)
        {
            return byStatus;
        }

        var byId = string.Compare(a.Package.Id, b.Package.Id, StringComparison.OrdinalIgnoreCase);
        return byId != 0
            ? byId
            : string.Compare(a.Package.Version, b.Package.Version, StringComparison.OrdinalIgnoreCase);
    }
}
