using NuGetCooldown.Configuration;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class AllowListTests
{
    private static PackageIdentity Pkg(string id, string version = "1.0.0") =>
        PackageIdentity.Create(id, version);

    [Fact]
    public void Exact_id_matches_any_version_case_insensitively()
    {
        var list = new AllowList(["serilog"]);

        Assert.True(list.IsAllowed(Pkg("Serilog", "4.0.0")));
        Assert.True(list.IsAllowed(Pkg("Serilog", "0.0.1-alpha")));
        Assert.False(list.IsAllowed(Pkg("Serilog.Sinks.Console")));
    }

    [Fact]
    public void Id_glob_matches_prefixes()
    {
        var list = new AllowList(["MyCompany.*"]);

        Assert.True(list.IsAllowed(Pkg("MyCompany.Core")));
        Assert.True(list.IsAllowed(Pkg("mycompany.web.client")));
        Assert.False(list.IsAllowed(Pkg("NotMyCompany.Core")));
    }

    [Fact]
    public void Id_at_version_matches_only_that_version()
    {
        var list = new AllowList(["Serilog@4.0.0"]);

        Assert.True(list.IsAllowed(Pkg("Serilog", "4.0.0")));
        Assert.False(list.IsAllowed(Pkg("Serilog", "4.0.1")));
    }

    [Fact]
    public void Allow_version_is_normalized_before_comparison()
    {
        // "1.0" in the config must match the resolved "1.0.0".
        var list = new AllowList(["Foo@1.0"]);

        Assert.True(list.IsAllowed(Pkg("Foo", "1.0.0")));
    }

    [Fact]
    public void Version_glob_matches_version_families()
    {
        var list = new AllowList(["Foo@2.*"]);

        Assert.True(list.IsAllowed(Pkg("Foo", "2.1.0")));
        Assert.False(list.IsAllowed(Pkg("Foo", "3.0.0")));
    }

    [Fact]
    public void Glob_special_characters_are_escaped()
    {
        // The '.' in the pattern must not act as a regex wildcard.
        var list = new AllowList(["My.Package"]);

        Assert.False(list.IsAllowed(Pkg("MyXPackage")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a@b@c")]
    [InlineData("Foo@")]
    [InlineData("@1.0.0")]
    public void Invalid_entries_throw(string pattern)
    {
        Assert.Throws<CooldownConfigException>(() => new AllowList([pattern]));
    }

    [Fact]
    public void Empty_list_allows_nothing()
    {
        Assert.False(AllowList.Empty.IsAllowed(Pkg("Anything")));
    }
}
