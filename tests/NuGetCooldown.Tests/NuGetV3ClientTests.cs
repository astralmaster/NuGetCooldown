using System.Net;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class NuGetV3ClientTests
{
    private const string Index = "https://feed.test/v3/index.json";
    private const string RegBase = "https://feed.test/reg/";

    private const string IndexJson = $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "https://feed.test/search", "@type": "SearchQueryService" },
            { "@id": "{{RegBase}}", "@type": "RegistrationsBaseUrl/3.6.0" }
          ]
        }
        """;

    private static readonly PackageIdentity Package = PackageIdentity.Create("Foo.Bar", "1.2.3");

    private static FakeHttpHandler WithIndex() => new FakeHttpHandler()
        .Map(Index, () => FakeHttpHandler.Json(IndexJson));

    private static NuGetV3Client Client(FakeHttpHandler handler, params string[] sources) =>
        new(handler.CreateClient(), sources.Length == 0 ? [Index] : sources);

    [Fact]
    public async Task Listed_package_uses_the_registration_published_date()
    {
        var handler = WithIndex().Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
            {
              "listed": true,
              "published": "2026-08-10T12:00:00+00:00",
              "catalogEntry": "https://feed.test/catalog/foo.bar.1.2.3.json"
            }
            """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero), result.Info!.PublishedUtc);
        Assert.True(result.Info.Listed);
        Assert.False(result.Info.FromCatalog);
        Assert.Equal(Index, result.Info.SourceUrl);
        // The catalog must not have been touched.
        Assert.DoesNotContain("https://feed.test/catalog/foo.bar.1.2.3.json", handler.RequestCounts.Keys);
    }

    [Fact]
    public async Task Unlisted_package_recovers_the_true_date_from_the_catalog()
    {
        var handler = WithIndex()
            .Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
                {
                  "listed": false,
                  "published": "1900-01-01T00:00:00+00:00",
                  "catalogEntry": "https://feed.test/catalog/entry.json"
                }
                """))
            .Map("https://feed.test/catalog/entry.json", () => FakeHttpHandler.Json("""
                { "created": "2026-08-01T08:30:00+00:00", "listed": false }
                """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 8, 30, 0, TimeSpan.Zero), result.Info!.PublishedUtc);
        Assert.False(result.Info.Listed);
        Assert.True(result.Info.FromCatalog);
    }

    [Fact]
    public async Task Unlisted_package_with_unreachable_catalog_still_reports_unlisted()
    {
        var handler = WithIndex().Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
            {
              "listed": false,
              "published": "1900-01-01T00:00:00+00:00",
              "catalogEntry": "https://feed.test/catalog/missing.json"
            }
            """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Null(result.Info!.PublishedUtc);
        Assert.False(result.Info.Listed);
    }

    [Fact]
    public async Task Listed_package_without_a_date_falls_back_to_the_catalog()
    {
        var handler = WithIndex()
            .Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
                { "listed": true, "catalogEntry": "https://feed.test/catalog/entry.json" }
                """))
            .Map("https://feed.test/catalog/entry.json", () => FakeHttpHandler.Json("""
                { "created": "2026-07-15T00:00:00+00:00" }
                """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), result.Info!.PublishedUtc);
        Assert.True(result.Info.Listed);
        Assert.True(result.Info.FromCatalog);
    }

    [Fact]
    public async Task Package_missing_on_first_source_is_found_on_the_second()
    {
        const string Index2 = "https://second.test/v3/index.json";
        const string RegBase2 = "https://second.test/reg/";

        var handler = WithIndex()
            .Map(Index2, () => FakeHttpHandler.Json($$"""
                { "resources": [ { "@id": "{{RegBase2}}", "@type": "RegistrationsBaseUrl/3.6.0" } ] }
                """))
            .Map($"{RegBase2}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
                { "listed": true, "published": "2026-08-05T00:00:00+00:00" }
                """));

        var result = await Client(handler, Index, Index2).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Equal(Index2, result.Info!.SourceUrl);
    }

    [Fact]
    public async Task Missing_everywhere_is_not_found()
    {
        var result = await Client(WithIndex()).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Non_transient_failure_is_an_error_with_the_source_in_the_message()
    {
        var handler = WithIndex()
            .Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Status(HttpStatusCode.Forbidden));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Error, result.Outcome);
        Assert.Contains(Index, result.Message);
    }

    [Fact]
    public async Task Transient_failures_are_retried()
    {
        var leafUrl = $"{RegBase}foo.bar/1.2.3.json";
        var handler = WithIndex().Map(leafUrl, attempt => attempt == 1
            ? FakeHttpHandler.Status(HttpStatusCode.InternalServerError)
            : FakeHttpHandler.Json("""{ "listed": true, "published": "2026-08-05T00:00:00+00:00" }"""));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Equal(2, handler.RequestCounts[leafUrl]);
    }

    [Fact]
    public async Task Service_index_without_registration_resource_is_an_error()
    {
        var handler = new FakeHttpHandler().Map(Index, () => FakeHttpHandler.Json("""
            { "resources": [ { "@id": "https://feed.test/search", "@type": "SearchQueryService" } ] }
            """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Error, result.Outcome);
        Assert.Contains("RegistrationsBaseUrl", result.Message);
    }

    [Fact]
    public async Task Resource_type_arrays_are_supported()
    {
        var handler = new FakeHttpHandler()
            .Map(Index, () => FakeHttpHandler.Json($$"""
                { "resources": [ { "@id": "{{RegBase}}", "@type": ["RegistrationsBaseUrl/3.6.0", "Other"] } ] }
                """))
            .Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
                { "listed": true, "published": "2026-08-05T00:00:00+00:00" }
                """));

        var result = await Client(handler).GetPublishInfoAsync(Package, CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
    }

    [Fact]
    public async Task Service_index_is_fetched_once_across_lookups()
    {
        var handler = WithIndex()
            .Map($"{RegBase}foo.bar/1.2.3.json", () => FakeHttpHandler.Json("""
                { "listed": true, "published": "2026-08-05T00:00:00+00:00" }
                """))
            .Map($"{RegBase}other/2.0.0.json", () => FakeHttpHandler.Json("""
                { "listed": true, "published": "2026-08-05T00:00:00+00:00" }
                """));

        var client = Client(handler);
        await client.GetPublishInfoAsync(Package, CancellationToken.None);
        await client.GetPublishInfoAsync(PackageIdentity.Create("Other", "2.0.0"), CancellationToken.None);

        Assert.Equal(1, handler.RequestCounts[Index]);
    }
}
