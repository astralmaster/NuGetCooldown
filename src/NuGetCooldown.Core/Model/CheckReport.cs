namespace NuGetCooldown.Model;

/// <summary>The complete result of a cooldown check across one or more projects.</summary>
public sealed record CheckReport
{
    /// <summary>Version of the tool that produced the report.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>UTC time the check ran.</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>The cooldown window.</summary>
    public required TimeSpan Cooldown { get; init; }

    /// <summary>The dependency scope that was checked.</summary>
    public required DependencyScope Scope { get; init; }

    /// <summary>Service index URLs that were consulted.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Names of the checked projects.</summary>
    public required IReadOnlyList<string> ProjectNames { get; init; }

    /// <summary>Per-package results, ordered by severity (errors first), then status, then id.</summary>
    public required IReadOnlyList<PackageResult> Results { get; init; }

    /// <summary>Wall-clock duration of the check, in seconds.</summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>Projects that were requested but have no dependency graph (not restored).</summary>
    public IReadOnlyList<string> NotRestoredProjects { get; init; } = [];

    /// <summary>Severity assigned to unrestored projects, per the <c>onNotRestored</c> policy.</summary>
    public Severity NotRestoredSeverity { get; init; } = Severity.Warning;

    /// <summary>Projects whose file is newer than their dependency graph (a restore is probably pending).</summary>
    public IReadOnlyList<string> StaleProjects { get; init; } = [];

    /// <summary>The cooldown window, formatted for display.</summary>
    public string CooldownText => DurationFormat.Humanize(Cooldown);

    /// <summary>How many lookups were answered by the local disk cache.</summary>
    public int CacheHits => Results.Count(r => r.FromCache);

    /// <summary>Number of package versions actually checked (everything except allow-listed skips).</summary>
    public int CheckedCount => Results.Count(r => r.Status != PackageStatus.Allowed);

    /// <summary>Number of results with the given status.</summary>
    public int Count(PackageStatus status) => Results.Count(r => r.Status == status);

    /// <summary>True when the check fails: any error result, or unrestored projects under an error policy.</summary>
    public bool HasErrors =>
        Results.Any(r => r.Severity == Severity.Error)
        || (NotRestoredSeverity == Severity.Error && NotRestoredProjects.Count > 0);

    /// <summary>True when no result is a warning or an error and every project was restored and current.</summary>
    public bool IsClean =>
        NotRestoredProjects.Count == 0
        && StaleProjects.Count == 0
        && Results.All(r => r.Severity == Severity.None);

    /// <summary>
    /// True only when every package's age was genuinely determined — no feed errors and no unknown
    /// dates, even if the policy ignored them. This is what makes the incremental stamp safe: an
    /// ignored feed outage must not be recorded as a verified-clean build.
    /// </summary>
    public bool FullyVerified =>
        NotRestoredProjects.Count == 0
        && Results.All(r => r.Status is not (PackageStatus.FeedError or PackageStatus.Unknown));

    /// <summary>True when a clean, fully verified result may be recorded as an incremental-build stamp.</summary>
    public bool StampEligible => IsClean && FullyVerified;

    /// <summary>Process exit code: 1 when the check fails, otherwise 0.</summary>
    public int ExitCode => HasErrors ? 1 : 0;
}
