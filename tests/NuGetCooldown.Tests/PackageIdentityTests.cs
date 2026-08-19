using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class PackageIdentityTests
{
    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("1.0.3.4", "1.0.3.4")]
    [InlineData("1.01.1", "1.1.1")]
    [InlineData("1.0.0+build.5", "1.0.0")]
    [InlineData("2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("2.0.0-BETA.1+meta", "2.0.0-BETA.1")]
    public void Version_is_normalized_like_nuget(string input, string expected)
    {
        Assert.Equal(expected, PackageIdentity.Create("X", input).Version);
    }

    [Fact]
    public void Unparsable_version_is_kept_verbatim()
    {
        Assert.Equal("not-a-version", PackageIdentity.Create("X", "not-a-version").Version);
    }

    [Fact]
    public void Equality_ignores_case_of_id_and_version()
    {
        var a = PackageIdentity.Create("Newtonsoft.Json", "13.0.3-BETA");
        var b = PackageIdentity.Create("newtonsoft.json", "13.0.3-beta");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Lowercase_forms_are_url_ready()
    {
        var identity = PackageIdentity.Create("Newtonsoft.Json", "13.0.3.0");

        Assert.Equal("newtonsoft.json", identity.LowerId);
        Assert.Equal("13.0.3", identity.LowerVersion);
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("X", " ")]
    public void Blank_id_or_version_throws(string id, string version)
    {
        Assert.ThrowsAny<ArgumentException>(() => PackageIdentity.Create(id, version));
    }
}
