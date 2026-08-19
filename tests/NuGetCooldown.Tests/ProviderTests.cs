using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class CachingPublishInfoProviderTests
{
    private static readonly PackageIdentity Package = PackageIdentity.Create("Foo", "1.0.0");
    private static readonly DateTimeOffset Published = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly FakeTime Time = new(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Second_lookup_is_served_from_cache()
    {
        using var dir = new TempDir();
        var inner = new FakeProvider().AddPublished(Package, Published);
        var provider = new CachingPublishInfoProvider(inner, new FileCache(dir.Path), Time);

        var first = await provider.GetPublishInfoAsync(Package, CancellationToken.None);
        var second = await provider.GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.False(first.Info!.FromCache);
        Assert.True(second.Info!.FromCache);
        Assert.Equal(Published, second.Info.PublishedUtc);
        Assert.Equal(1, inner.Lookups[Package]);
    }

    [Fact]
    public async Task Unlisted_flag_survives_the_cache()
    {
        using var dir = new TempDir();
        var inner = new FakeProvider().AddPublished(Package, Published, listed: false);
        var provider = new CachingPublishInfoProvider(inner, new FileCache(dir.Path), Time);

        await provider.GetPublishInfoAsync(Package, CancellationToken.None);
        var cached = await provider.GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.False(cached.Info!.Listed);
    }

    [Fact]
    public async Task Results_without_a_date_are_not_cached()
    {
        using var dir = new TempDir();
        var inner = new FakeProvider().Add(Package, PublishLookupResult.Found(
            new PackagePublishInfo(null, Listed: true, "https://fake.test", false, false)));
        var provider = new CachingPublishInfoProvider(inner, new FileCache(dir.Path), Time);

        await provider.GetPublishInfoAsync(Package, CancellationToken.None);
        await provider.GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(2, inner.Lookups[Package]);
    }

    [Fact]
    public async Task Not_found_is_not_cached()
    {
        using var dir = new TempDir();
        var inner = new FakeProvider(); // knows nothing -> NotFound
        var provider = new CachingPublishInfoProvider(inner, new FileCache(dir.Path), Time);

        await provider.GetPublishInfoAsync(Package, CancellationToken.None);
        await provider.GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(2, inner.Lookups[Package]);
    }
}

public class OfflineCacheProviderTests
{
    private static readonly PackageIdentity Package = PackageIdentity.Create("Foo", "1.0.0");

    [Fact]
    public async Task Cached_entry_is_served_without_network()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);
        cache.Set(Package, new CacheEntry(
            1, Package.Id, Package.Version,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            Listed: true, "https://feed.test", FromCatalog: false,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero)));

        var result = await new OfflineCacheProvider(cache).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.True(result.Info!.FromCache);
    }

    [Fact]
    public async Task Uncached_package_is_reported_as_offline_miss()
    {
        using var dir = new TempDir();

        var result = await new OfflineCacheProvider(new FileCache(dir.Path))
            .GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.NotFound, result.Outcome);
        Assert.Contains("offline", result.Message);
    }
}
