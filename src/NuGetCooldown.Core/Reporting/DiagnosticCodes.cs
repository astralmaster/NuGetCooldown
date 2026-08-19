namespace NuGetCooldown.Reporting;

/// <summary>Diagnostic codes emitted in MSBuild and JSON output.</summary>
public static class DiagnosticCodes
{
    /// <summary>A package version is younger than the cooldown window.</summary>
    public const string Violation = "NCD001";

    /// <summary>A package version's publish date could not be determined.</summary>
    public const string UnknownPublishDate = "NCD002";

    /// <summary>A package version is unlisted on its source.</summary>
    public const string Unlisted = "NCD003";

    /// <summary>A configured source could not be queried.</summary>
    public const string FeedError = "NCD004";

    /// <summary>A project was requested but has not been restored, so it could not be checked.</summary>
    public const string NotRestored = "NCD005";

    /// <summary>Invalid usage or configuration.</summary>
    public const string Usage = "NCD006";

    /// <summary>Unexpected internal failure.</summary>
    public const string Internal = "NCD999";
}
