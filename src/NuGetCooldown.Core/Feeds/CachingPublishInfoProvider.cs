using NuGetCooldown.Model;

namespace NuGetCooldown.Feeds;

/// <summary>
/// Serves lookups from the <see cref="FileCache"/> and stores fresh feed answers into it.
/// Only answers with a resolved date are cached; misses and errors are always retried.
/// </summary>
public sealed class CachingPublishInfoProvider(
    IPackagePublishInfoProvider inner,
    FileCache cache,
    TimeProvider timeProvider) : IPackagePublishInfoProvider
{
    /// <inheritdoc />
    public async Task<PublishLookupResult> GetPublishInfoAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        if (cache.TryGet(package) is { PublishedUtc: not null } entry)
        {
            return PublishLookupResult.Found(new PackagePublishInfo(
                entry.PublishedUtc,
                entry.Listed,
                entry.SourceUrl,
                entry.FromCatalog,
                FromCache: true));
        }

        var result = await inner.GetPublishInfoAsync(package, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: LookupOutcome.Found, Info.PublishedUtc: not null })
        {
            var info = result.Info;
            cache.Set(package, new CacheEntry(
                SchemaVersion: 1,
                package.Id,
                package.Version,
                info.PublishedUtc,
                info.Listed,
                info.SourceUrl,
                info.FromCatalog,
                CachedAtUtc: timeProvider.GetUtcNow()));
        }

        return result;
    }
}

/// <summary>Offline lookups: the cache is the only source; anything not cached is unknown.</summary>
public sealed class OfflineCacheProvider(FileCache cache) : IPackagePublishInfoProvider
{
    /// <inheritdoc />
    public Task<PublishLookupResult> GetPublishInfoAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        var result = cache.TryGet(package) is { PublishedUtc: not null } entry
            ? PublishLookupResult.Found(new PackagePublishInfo(
                entry.PublishedUtc, entry.Listed, entry.SourceUrl, entry.FromCatalog, FromCache: true))
            : PublishLookupResult.NotFound("not in the local cache (offline mode)");

        return Task.FromResult(result);
    }
}
