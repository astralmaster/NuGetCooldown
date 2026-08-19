using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class DurationFormatTests
{
    [Theory]
    [InlineData(24, "1 day")]
    [InlineData(168, "7 days")]
    [InlineData(45.6, "1.9 days")]
    [InlineData(23, "23 hours")]
    [InlineData(1, "1 hour")]
    [InlineData(0.5, "0.5 hours")]
    [InlineData(36, "1.5 days")]
    [InlineData(12, "12 hours")]
    public void Humanize_picks_days_or_hours_and_is_grammatical(double hours, string expected)
    {
        Assert.Equal(expected, DurationFormat.Humanize(TimeSpan.FromHours(hours)));
    }
}
