using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class FileCacheTests
{
    private static readonly PackageIdentity Package = PackageIdentity.Create("Foo.Bar", "1.2.3");

    private static CacheEntry Entry(DateTimeOffset? published = null) => new(
        SchemaVersion: 1,
        Package.Id,
        Package.Version,
        published ?? new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        Listed: true,
        SourceUrl: "https://feed.test/index.json",
        FromCatalog: false,
        CachedAtUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Roundtrips_an_entry()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);

        cache.Set(Package, Entry());
        var loaded = cache.TryGet(Package);

        Assert.Equal(Entry(), loaded);
    }

    [Fact]
    public void Lookup_is_case_insensitive_via_identity_normalization()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);

        cache.Set(Package, Entry());

        Assert.NotNull(cache.TryGet(PackageIdentity.Create("FOO.BAR", "1.2.3.0")));
    }

    [Fact]
    public void Missing_entry_is_null()
    {
        using var dir = new TempDir();

        Assert.Null(new FileCache(dir.Path).TryGet(Package));
    }

    [Fact]
    public void Corrupt_entry_is_treated_as_missing()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);
        cache.Set(Package, Entry());

        var file = Directory.GetFiles(dir.Path, "*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(file, "{ corrupt");

        Assert.Null(cache.TryGet(Package));
    }

    [Fact]
    public void Clear_removes_everything_and_reports_whether_anything_existed()
    {
        using var dir = new TempDir();
        var cacheRoot = dir.Combine("cache");
        var cache = new FileCache(cacheRoot);
        cache.Set(Package, Entry());

        Assert.True(cache.Clear());
        Assert.False(Directory.Exists(cacheRoot));
        Assert.False(cache.Clear());
    }

    [Fact]
    public void Hostile_characters_in_ids_are_sanitized_for_the_filesystem()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);
        var hostile = PackageIdentity.Create("a/b:c", "not-a-version");

        cache.Set(hostile, Entry() with { Id = hostile.Id, Version = hostile.Version });

        Assert.NotNull(cache.TryGet(hostile));
        // Everything must stay under the cache root.
        Assert.All(
            Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories),
            f => Assert.StartsWith(dir.Path, f));
    }
}
