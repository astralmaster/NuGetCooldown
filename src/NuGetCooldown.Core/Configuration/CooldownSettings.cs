using NuGetCooldown.Model;

namespace NuGetCooldown.Configuration;

/// <summary>Effective settings for a cooldown check, after merging defaults, config file, and command line.</summary>
public sealed record CooldownSettings
{
    /// <summary>The default cooldown window: seven days blocks the large majority of observed supply-chain attacks.</summary>
    public const int DefaultCooldownDays = 7;

    /// <summary>The nuget.org V3 service index.</summary>
    public const string NuGetOrgServiceIndex = "https://api.nuget.org/v3/index.json";

    /// <summary>Upper bound for <see cref="CooldownDays"/> (ten years) — anything larger is a configuration mistake.</summary>
    public const int MaxCooldownDays = 3650;

    /// <summary>Minimum age, in days, a package version must have.</summary>
    public int CooldownDays { get; init; } = DefaultCooldownDays;

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

    /// <summary>When true, every finding is downgraded to a warning and the exit code is always 0.</summary>
    public bool WarnOnly { get; init; }

    /// <summary>Throws <see cref="CooldownConfigException"/> when a value is out of range.</summary>
    public void Validate()
    {
        if (CooldownDays is < 0 or > MaxCooldownDays)
        {
            throw new CooldownConfigException(
                $"cooldownDays must be between 0 and {MaxCooldownDays}; got {CooldownDays}.");
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
