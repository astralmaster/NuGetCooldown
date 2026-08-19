namespace NuGetCooldown.Model;

/// <summary>Publish metadata for one package version, as resolved from a feed or the local cache.</summary>
/// <param name="PublishedUtc">
/// When the version was published. For unlisted versions this is the original upload time taken
/// from the NuGet catalog, not the <c>1900-01-01</c> sentinel the registration endpoint reports.
/// <see langword="null"/> when no usable date was available.
/// </param>
/// <param name="Listed">Whether the version is currently listed on the source.</param>
/// <param name="SourceUrl">The service index URL of the source that provided the answer.</param>
/// <param name="FromCatalog">True when the date came from the catalog fallback used for unlisted versions.</param>
/// <param name="FromCache">True when the answer was served from the local disk cache.</param>
public sealed record PackagePublishInfo(
    DateTimeOffset? PublishedUtc,
    bool Listed,
    string SourceUrl,
    bool FromCatalog,
    bool FromCache);
