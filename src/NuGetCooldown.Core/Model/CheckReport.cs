namespace NuGetCooldown.Model;

/// <summary>The complete result of a cooldown check across one or more projects.</summary>
public sealed record CheckReport
{
    /// <summary>Version of the tool that produced the report.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>UTC time the check ran.</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>The cooldown window, in days.</summary>
    public required int CooldownDays { get; init; }

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

    /// <summary>How many lookups were answered by the local disk cache.</summary>
    public int CacheHits => Results.Count(r => r.FromCache);

    /// <summary>Projects that were requested but have no <c>project.assets.json</c> (not restored).</summary>
    public IReadOnlyList<string> NotRestoredProjects { get; init; } = [];

    /// <summary>Number of results with the given status.</summary>
    public int Count(PackageStatus status) => Results.Count(r => r.Status == status);

    /// <summary>True when any result is an error (fails the check).</summary>
    public bool HasErrors => Results.Any(r => r.Severity == Severity.Error);

    /// <summary>True when no result is a warning or an error and every project was restored.</summary>
    public bool IsClean =>
        NotRestoredProjects.Count == 0 && Results.All(r => r.Severity == Severity.None);

    /// <summary>Process exit code: 1 when the check fails, otherwise 0.</summary>
    public int ExitCode => HasErrors ? 1 : 0;
}
