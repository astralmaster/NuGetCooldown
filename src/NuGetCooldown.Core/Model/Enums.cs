namespace NuGetCooldown.Model;

/// <summary>How a policy condition (unknown date, unlisted version, feed error) is reported.</summary>
public enum PolicyAction
{
    /// <summary>Report as a warning; does not affect the exit code.</summary>
    Warn,

    /// <summary>Report as an error; the check fails.</summary>
    Error,

    /// <summary>Do not report; visible only in verbose/JSON output.</summary>
    Ignore,
}

/// <summary>Which part of the dependency graph is checked.</summary>
public enum DependencyScope
{
    /// <summary>Every resolved package: direct and transitive. This is where supply-chain attacks hide.</summary>
    All,

    /// <summary>Only packages referenced directly by a project.</summary>
    Direct,
}

/// <summary>The outcome of checking a single package version.</summary>
public enum PackageStatus
{
    /// <summary>Older than the cooldown window and listed.</summary>
    Ok,

    /// <summary>Skipped because it matched an allow-list entry.</summary>
    Allowed,

    /// <summary>Younger than the cooldown window.</summary>
    Violation,

    /// <summary>Old enough, but the version is unlisted on its source (often a takedown or an author pull).</summary>
    Unlisted,

    /// <summary>The publish date could not be determined (not found on any source, or no date available).</summary>
    Unknown,

    /// <summary>A configured source could not be queried (network failure, server error).</summary>
    FeedError,
}

/// <summary>Severity assigned to a package result after applying the configured policy.</summary>
public enum Severity
{
    /// <summary>Not a finding.</summary>
    None,

    /// <summary>Reported, but does not affect the exit code.</summary>
    Warning,

    /// <summary>Fails the check (exit code 1).</summary>
    Error,
}
