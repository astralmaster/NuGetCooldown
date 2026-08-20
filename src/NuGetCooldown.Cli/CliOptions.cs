namespace NuGetCooldown.Cli;

/// <summary>Parsed command line. See <see cref="HelpText"/> for the user-facing contract.</summary>
internal sealed class CliOptions
{
    public string Command { get; set; } = "help";

    // check
    public string? Path { get; set; }
    public int? Days { get; set; }
    public int? Hours { get; set; }
    public string? ConfigPath { get; set; }
    public bool NoConfig { get; set; }
    public List<string> Sources { get; } = [];
    public List<string> Allow { get; } = [];
    public string? Scope { get; set; }
    public string? OnUnknown { get; set; }
    public string? OnUnlisted { get; set; }
    public string? OnFeedError { get; set; }
    public string? OnNotRestored { get; set; }
    public bool WarnOnly { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? MaxParallel { get; set; }
    public string Format { get; set; } = "text";
    public bool Verbose { get; set; }
    public bool Quiet { get; set; }
    public string? MSBuildOrigin { get; set; }
    public string? StampFilePath { get; set; }

    // info
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }

    // cache behavior (check, info, clear-cache)
    public bool NoCache { get; set; }
    public bool Offline { get; set; }
    public string? CacheDir { get; set; }
}
