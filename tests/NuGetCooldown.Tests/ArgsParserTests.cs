using NuGetCooldown.Cli;
using NuGetCooldown.Configuration;
using Xunit;

namespace NuGetCooldown.Tests;

public class ArgsParserTests
{
    [Fact]
    public void No_arguments_shows_help()
    {
        Assert.Equal("help", ArgsParser.Parse([]).Command);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_flags_show_help(string arg)
    {
        Assert.Equal("help", ArgsParser.Parse([arg]).Command);
    }

    [Fact]
    public void Help_after_a_command_shows_help()
    {
        Assert.Equal("help", ArgsParser.Parse(["check", "--help"]).Command);
    }

    [Fact]
    public void Version_flag_is_recognized()
    {
        Assert.Equal("version", ArgsParser.Parse(["--version"]).Command);
    }

    [Fact]
    public void Check_parses_the_full_option_surface()
    {
        var options = ArgsParser.Parse([
            "check", "some/dir",
            "--days", "14",
            "--hours", "6",
            "--config", "cfg.json",
            "--source", "https://a.test/index.json",
            "-s", "https://b.test/index.json",
            "--allow", "MyCompany.*;Serilog@4.0.0",
            "--scope", "direct",
            "--on-unknown", "error",
            "--on-unlisted", "ignore",
            "--on-feed-error", "error",
            "--on-not-restored", "error",
            "--warn-only",
            "--format", "json",
            "--verbose",
            "--no-cache",
            "--offline",
            "--cache-dir", "/tmp/cache",
            "--msbuild", "Proj.csproj",
            "--stamp-file", "obj/x.stamp",
        ]);

        Assert.Equal("check", options.Command);
        Assert.Equal("some/dir", options.Path);
        Assert.Equal(14, options.Days);
        Assert.Equal(6, options.Hours);
        Assert.Equal("cfg.json", options.ConfigPath);
        Assert.Equal(["https://a.test/index.json", "https://b.test/index.json"], options.Sources);
        Assert.Equal(["MyCompany.*", "Serilog@4.0.0"], options.Allow);
        Assert.Equal("direct", options.Scope);
        Assert.Equal("error", options.OnUnknown);
        Assert.Equal("ignore", options.OnUnlisted);
        Assert.Equal("error", options.OnFeedError);
        Assert.Equal("error", options.OnNotRestored);
        Assert.True(options.WarnOnly);
        Assert.Equal("json", options.Format);
        Assert.True(options.Verbose);
        Assert.True(options.NoCache);
        Assert.True(options.Offline);
        Assert.Equal("/tmp/cache", options.CacheDir);
        Assert.Equal("Proj.csproj", options.MSBuildOrigin);
        Assert.Equal("obj/x.stamp", options.StampFilePath);
    }

    [Fact]
    public void Equals_syntax_is_supported()
    {
        var options = ArgsParser.Parse(["check", "--days=30", "--format=json"]);

        Assert.Equal(30, options.Days);
        Assert.Equal("json", options.Format);
    }

    [Fact]
    public void Info_takes_id_and_version()
    {
        var options = ArgsParser.Parse(["info", "Serilog", "4.0.0", "--days", "14"]);

        Assert.Equal("info", options.Command);
        Assert.Equal("Serilog", options.PackageId);
        Assert.Equal("4.0.0", options.PackageVersion);
        Assert.Equal(14, options.Days);
    }

    [Theory]
    [InlineData(new[] { "frobnicate" }, "Unknown command")]
    [InlineData(new[] { "check", "--frobnicate" }, "Unknown option")]
    [InlineData(new[] { "check", "--days" }, "requires a value")]
    [InlineData(new[] { "check", "--days", "soon" }, "not a valid number")]
    [InlineData(new[] { "check", "--format", "xml" }, "not a valid format")]
    [InlineData(new[] { "check", "a", "b" }, "at most one path")]
    [InlineData(new[] { "info", "OnlyId" }, "requires a package id and a version")]
    [InlineData(new[] { "clear-cache", "stray" }, "takes no arguments")]
    [InlineData(new[] { "info", "--warn-only", "X", "1.0.0" }, "Unknown option")]
    public void Invalid_input_fails_with_a_helpful_message(string[] args, string expectedFragment)
    {
        var ex = Assert.Throws<CooldownUsageException>(() => ArgsParser.Parse(args));

        Assert.Contains(expectedFragment, ex.Message);
    }
}
