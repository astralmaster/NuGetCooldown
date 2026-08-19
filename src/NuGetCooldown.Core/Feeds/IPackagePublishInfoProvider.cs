using NuGetCooldown.Model;

namespace NuGetCooldown.Feeds;

/// <summary>Outcome of a publish-info lookup.</summary>
public enum LookupOutcome
{
    /// <summary>The package version exists on a source (its date may still be unknown).</summary>
    Found,

    /// <summary>No configured source knows the package version.</summary>
    NotFound,

    /// <summary>At least one source could not be queried and no other source had an answer.</summary>
    Error,
}

/// <summary>Result of asking the configured sources when a package version was published.</summary>
public sealed record PublishLookupResult(
    LookupOutcome Outcome,
    PackagePublishInfo? Info,
    string? Message)
{
    /// <summary>The package version exists; <paramref name="info"/> carries the details.</summary>
    public static PublishLookupResult Found(PackagePublishInfo info) => new(LookupOutcome.Found, info, null);

    /// <summary>No source knows the package version.</summary>
    public static PublishLookupResult NotFound(string? message = null) => new(LookupOutcome.NotFound, null, message);

    /// <summary>The lookup failed.</summary>
    public static PublishLookupResult Error(string message) => new(LookupOutcome.Error, null, message);
}

/// <summary>Resolves when a package version was published.</summary>
public interface IPackagePublishInfoProvider
{
    /// <summary>Looks up publish information for <paramref name="package"/>.</summary>
    Task<PublishLookupResult> GetPublishInfoAsync(PackageIdentity package, CancellationToken cancellationToken);
}
