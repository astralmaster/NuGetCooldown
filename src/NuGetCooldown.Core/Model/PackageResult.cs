namespace NuGetCooldown.Model;

/// <summary>The evaluated result for one package version.</summary>
public sealed record PackageResult
{
    /// <summary>The package version that was checked.</summary>
    public required PackageIdentity Package { get; init; }

    /// <summary>The outcome of the check.</summary>
    public required PackageStatus Status { get; init; }

    /// <summary>The severity after applying policy (<c>--warn-only</c>, <c>onUnknown</c>, …).</summary>
    public required Severity Severity { get; init; }

    /// <summary>When the version was published, if known.</summary>
    public DateTimeOffset? PublishedUtc { get; init; }

    /// <summary>Age at check time, in days, if the publish date is known.</summary>
    public double? AgeDays { get; init; }

    /// <summary>Whether the version is listed on its source; <see langword="null"/> when unknown.</summary>
    public bool? Listed { get; init; }

    /// <summary>True when at least one project references the package directly.</summary>
    public bool IsDirect { get; init; }

    /// <summary>Names of the projects whose dependency graph contains the package.</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>The service index URL that answered the lookup, if any.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>True when the publish date was served from the local disk cache.</summary>
    public bool FromCache { get; init; }

    /// <summary>Human-readable explanation for findings.</summary>
    public string? Message { get; init; }

    /// <summary>Diagnostic code (<c>NCD001</c>…) for findings; <see langword="null"/> for OK/allowed packages.</summary>
    public string? DiagnosticCode { get; init; }
}
