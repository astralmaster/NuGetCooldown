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

    [Theory]
    [InlineData("a/b:c", "1.0.0")]
    [InlineData("..", "1.0.0")]           // a traversal attempt from a hostile assets file
    [InlineData("../../etc", "1.0.0")]
    [InlineData("a\\b", "..")]
    public void Hostile_ids_and_versions_stay_inside_the_cache_root(string id, string version)
    {
        using var dir = new TempDir();
        var cacheRoot = dir.Combine("cache");
        var cache = new FileCache(cacheRoot);
        var hostile = PackageIdentity.Create(id, version);

        cache.Set(hostile, Entry() with { Id = hostile.Id, Version = hostile.Version });

        Assert.NotNull(cache.TryGet(hostile));
        // Nothing may be written outside the cache root, no matter what the identity contains.
        Assert.All(
            Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories),
            f => Assert.StartsWith(cacheRoot, f));
    }

    [Fact]
    public void A_very_long_id_does_not_crash_and_roundtrips()
    {
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);
        var huge = PackageIdentity.Create(new string('x', 200_000), "1.0.0");

        cache.Set(huge, Entry() with { Id = huge.Id, Version = huge.Version });

        Assert.NotNull(cache.TryGet(huge));
    }

    [Fact]
    public void An_entry_for_a_different_package_is_rejected()
    {
        // Defense in depth: even if a file sits at the computed path, it is trusted only when its
        // stored id/version match the request — so a collision or tampered file can't be served.
        using var dir = new TempDir();
        var cache = new FileCache(dir.Path);
        cache.Set(Package, Entry());

        var file = Directory.GetFiles(dir.Path, "*.json", SearchOption.AllDirectories).Single();
        var tampered = File.ReadAllText(file).Replace("2026-08-01", "2000-01-01");
        // Rewrite the file's Id so it no longer describes the requested package.
        File.WriteAllText(file, tampered.Replace(Package.Id, "Some.Other.Package"));

        Assert.Null(cache.TryGet(Package));
    }
}
