using NuGetCooldown.Model;

namespace NuGetCooldown.Feeds;

/// <summary>Turns cache entries into lookup results, so the cache contract lives in one place.</summary>
internal static class CacheEntryExtensions
{
    /// <summary>A cache entry is usable only when it carries a resolved publish date.</summary>
    public static bool IsUsable(this CacheEntry entry) => entry.PublishedUtc is not null;

    /// <summary>Projects the entry onto the publish-info shape, marked as cache-served.</summary>
    public static PackagePublishInfo ToPublishInfo(this CacheEntry entry) => new(
        entry.PublishedUtc,
        entry.Listed,
        entry.SourceUrl,
        entry.FromCatalog,
        FromCache: true);
}

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
        if (cache.TryGet(package) is { } cached && cached.IsUsable())
        {
            return PublishLookupResult.Found(cached.ToPublishInfo());
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
        var result = cache.TryGet(package) is { } cached && cached.IsUsable()
            ? PublishLookupResult.Found(cached.ToPublishInfo())
            : PublishLookupResult.NotFound("not in the local cache (offline mode)");

        return Task.FromResult(result);
    }
}
