using NuGetCooldown.Model;

namespace NuGetCooldown.Configuration;

/// <summary>Effective settings for a cooldown check, after merging defaults, config file, and command line.</summary>
public sealed record CooldownSettings
{
    /// <summary>The default cooldown window: seven days blocks the large majority of observed supply-chain attacks.</summary>
    public const int DefaultCooldownDays = 7;

    /// <summary>The nuget.org V3 service index.</summary>
    public const string NuGetOrgServiceIndex = "https://api.nuget.org/v3/index.json";

    /// <summary>Upper bound for the cooldown window (ten years) — anything larger is a configuration mistake.</summary>
    public const int MaxCooldownDays = 3650;

    /// <summary>
    /// Minimum age a package version must have. Stored as a duration so sub-day windows (e.g. 24 or
    /// 72 hours, matching pnpm and the NuGet cooldown spec) are expressible, not just whole days.
    /// </summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromDays(DefaultCooldownDays);

    /// <summary>Which packages are checked: all (default) or direct-only.</summary>
    public DependencyScope Scope { get; init; } = DependencyScope.All;

    /// <summary>NuGet V3 service index URLs, queried in order.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [NuGetOrgServiceIndex];

    /// <summary>Packages exempt from the check.</summary>
    public AllowList Allow { get; init; } = AllowList.Empty;

    /// <summary>How a package with an undeterminable publish date is reported.</summary>
    public PolicyAction OnUnknown { get; init; } = PolicyAction.Warn;

    /// <summary>How an unlisted package version is reported.</summary>
    public PolicyAction OnUnlisted { get; init; } = PolicyAction.Warn;

    /// <summary>How a source query failure is reported.</summary>
    public PolicyAction OnFeedError { get; init; } = PolicyAction.Warn;

    /// <summary>How a project that has not been restored (no dependency graph to check) is reported.</summary>
    public PolicyAction OnNotRestored { get; init; } = PolicyAction.Warn;

    /// <summary>When true, every finding is downgraded to a warning and the exit code is always 0.</summary>
    public bool WarnOnly { get; init; }

    /// <summary>Per-request HTTP timeout, in seconds, for feed lookups.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Maximum number of feed lookups performed concurrently.</summary>
    public int MaxConcurrency { get; init; } = 8;

    /// <summary>The cooldown window in whole-ish days, for display.</summary>
    public string CooldownText => DurationFormat.Humanize(Cooldown);

    /// <summary>Throws <see cref="CooldownConfigException"/> when a value is out of range.</summary>
    public void Validate()
    {
        if (Cooldown < TimeSpan.Zero || Cooldown > TimeSpan.FromDays(MaxCooldownDays))
        {
            throw new CooldownConfigException(
                $"The cooldown window must be between 0 and {MaxCooldownDays} days; got {CooldownText}.");
        }

        if (TimeoutSeconds is < 1 or > 600)
        {
            throw new CooldownConfigException(
                $"The request timeout must be between 1 and 600 seconds; got {TimeoutSeconds}.");
        }

        if (MaxConcurrency is < 1 or > 32)
        {
            throw new CooldownConfigException(
                $"The maximum parallelism must be between 1 and 32; got {MaxConcurrency}.");
        }

        if (Sources.Count == 0)
        {
            throw new CooldownConfigException("At least one package source is required.");
        }

        foreach (var source in Sources)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new CooldownConfigException(
                    $"Source '{source}' is not an absolute http(s) URL of a NuGet V3 service index.");
            }
        }
    }
}
