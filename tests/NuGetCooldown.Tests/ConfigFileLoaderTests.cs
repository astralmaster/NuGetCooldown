using NuGetCooldown.Configuration;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class ConfigFileLoaderTests
{
    private static CooldownSettings Load(TempDir dir, string json)
    {
        var path = dir.WriteFile(ConfigFileLoader.FileName, json);
        return ConfigFileLoader.Apply(new CooldownSettings(), path);
    }

    [Fact]
    public void Full_config_overrides_every_default()
    {
        using var dir = new TempDir();
        var settings = Load(dir, """
            {
              "$schema": "https://example.test/schema.json",
              "cooldownDays": 14,
              "scope": "direct",
              "allow": ["MyCompany.*", "Serilog@4.0.0"],
              "sources": ["https://feed.test/v3/index.json"],
              "onUnknown": "error",
              "onUnlisted": "ignore",
              "onFeedError": "error",
              "onNotRestored": "error"
            }
            """);

        Assert.Equal(TimeSpan.FromDays(14), settings.Cooldown);
        Assert.Equal(DependencyScope.Direct, settings.Scope);
        Assert.Equal(["https://feed.test/v3/index.json"], settings.Sources);
        Assert.True(settings.Allow.IsAllowed(PackageIdentity.Create("MyCompany.X", "1.0.0")));
        Assert.Equal(PolicyAction.Error, settings.OnUnknown);
        Assert.Equal(PolicyAction.Ignore, settings.OnUnlisted);
        Assert.Equal(PolicyAction.Error, settings.OnFeedError);
        Assert.Equal(PolicyAction.Error, settings.OnNotRestored);
    }

    [Fact]
    public void Empty_object_keeps_defaults()
    {
        using var dir = new TempDir();
        var settings = Load(dir, "{}");

        Assert.Equal(TimeSpan.FromDays(CooldownSettings.DefaultCooldownDays), settings.Cooldown);
        Assert.Equal([CooldownSettings.NuGetOrgServiceIndex], settings.Sources);
        Assert.Equal(PolicyAction.Warn, settings.OnUnknown);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        using var dir = new TempDir();
        var settings = Load(dir, """
            {
              // why: our packages are pre-vetted internally
              "cooldownDays": 3,
            }
            """);

        Assert.Equal(TimeSpan.FromDays(3), settings.Cooldown);
    }

    [Fact]
    public void Hours_define_a_sub_day_window()
    {
        using var dir = new TempDir();
        var settings = Load(dir, """{ "cooldownHours": 24 }""");

        Assert.Equal(TimeSpan.FromHours(24), settings.Cooldown);
    }

    [Fact]
    public void Days_and_hours_add_together()
    {
        using var dir = new TempDir();
        var settings = Load(dir, """{ "cooldownDays": 1, "cooldownHours": 12 }""");

        Assert.Equal(TimeSpan.FromHours(36), settings.Cooldown);
    }

    [Fact]
    public void Hours_only_means_zero_days_not_the_default()
    {
        using var dir = new TempDir();
        // 6 hours must not be silently widened to the 7-day default.
        var settings = Load(dir, """{ "cooldownHours": 6 }""");

        Assert.Equal(TimeSpan.FromHours(6), settings.Cooldown);
    }

    [Fact]
    public void Unknown_property_is_rejected_to_catch_typos()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<CooldownConfigException>(() => Load(dir, """{ "cooldownDayz": 3 }"""));

        Assert.Contains("cooldownDayz", ex.Message);
    }

    [Fact]
    public void Invalid_enum_lists_the_valid_values()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<CooldownConfigException>(() => Load(dir, """{ "onUnknown": "explode" }"""));

        Assert.Contains("warn", ex.Message);
        Assert.Contains("error", ex.Message);
        Assert.Contains("ignore", ex.Message);
    }

    [Fact]
    public void Malformed_json_reports_the_file()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<CooldownConfigException>(() => Load(dir, "{ not json"));

        Assert.Contains(ConfigFileLoader.FileName, ex.Message);
    }

    [Fact]
    public void Allow_entries_accumulate_over_the_base_settings()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile(ConfigFileLoader.FileName, """{ "allow": ["FromFile"] }""");

        var baseSettings = new CooldownSettings { Allow = new AllowList(["FromBase"]) };
        var settings = ConfigFileLoader.Apply(baseSettings, path);

        Assert.True(settings.Allow.IsAllowed(PackageIdentity.Create("FromBase", "1.0.0")));
        Assert.True(settings.Allow.IsAllowed(PackageIdentity.Create("FromFile", "1.0.0")));
    }

    [Fact]
    public void Probe_walks_up_to_the_nearest_config()
    {
        using var dir = new TempDir();
        var configPath = dir.WriteFile(ConfigFileLoader.FileName, "{}");
        var nested = dir.Combine("src", "deep", "deeper");
        Directory.CreateDirectory(nested);

        Assert.Equal(configPath, ConfigFileLoader.Probe(nested));
    }

    [Fact]
    public void Probe_returns_null_when_no_config_exists()
    {
        using var dir = new TempDir();

        Assert.Null(ConfigFileLoader.Probe(dir.Path));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public void Out_of_range_windows_fail_validation(int days)
    {
        var settings = new CooldownSettings { Cooldown = TimeSpan.FromDays(days) };

        Assert.Throws<CooldownConfigException>(settings.Validate);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://feed.test/index.json")]
    public void Invalid_source_fails_validation(string source)
    {
        var settings = new CooldownSettings { Sources = [source] };

        Assert.Throws<CooldownConfigException>(settings.Validate);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        new CooldownSettings().Validate();
    }
}
