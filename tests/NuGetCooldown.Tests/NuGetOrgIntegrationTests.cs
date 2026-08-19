using NuGetCooldown.Configuration;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

/// <summary>
/// Live tests against nuget.org. They pin the tool to reality: the registration leaf shape,
/// the unlisted sentinel, and the catalog fallback are all observed behavior, not spec fiction.
/// </summary>
[Trait("Category", "Integration")]
public class NuGetOrgIntegrationTests
{
    private static NuGetV3Client CreateClient() => new(
        NuGetHttpClientFactory.Create("0.0.0-tests"),
        [CooldownSettings.NuGetOrgServiceIndex]);

    [Fact]
    public async Task Listed_package_has_its_known_publish_date()
    {
        var result = await CreateClient().GetPublishInfoAsync(
            PackageIdentity.Create("Newtonsoft.Json", "13.0.3"), CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.True(result.Info!.Listed);
        // Known constant: 13.0.3 was published 2023-03-08.
        Assert.Equal(new DateOnly(2023, 3, 8), DateOnly.FromDateTime(result.Info.PublishedUtc!.Value.UtcDateTime));
    }

    [Fact]
    public async Task Unlisted_package_gets_its_true_date_from_the_catalog()
    {
        // Moq 4.20.0 (the SponsorLink release) is permanently unlisted; uploaded 2023-08-08.
        var result = await CreateClient().GetPublishInfoAsync(
            PackageIdentity.Create("Moq", "4.20.0"), CancellationToken.None);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.False(result.Info!.Listed);
        Assert.True(result.Info.FromCatalog);
        Assert.Equal(new DateOnly(2023, 8, 8), DateOnly.FromDateTime(result.Info.PublishedUtc!.Value.UtcDateTime));
    }

    [Fact]
    public async Task Nonexistent_package_is_not_found()
    {
        var result = await CreateClient().GetPublishInfoAsync(
            PackageIdentity.Create("NuGetCooldown.Tests.DoesNotExist", "1.0.0"), CancellationToken.None);

        Assert.Equal(LookupOutcome.NotFound, result.Outcome);
    }
}
