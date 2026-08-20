using NuGetCooldown.Cli;
using NuGetCooldown.Configuration;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class SettingsBuilderTests
{
    [Fact]
    public void Command_line_beats_config_file_beats_defaults()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "cooldownDays": 14, "scope": "direct" }""");

        var options = new CliOptions { Command = "check", Days = 30 };
        var (settings, configPath) = SettingsBuilder.Build(options, dir.Path);

        Assert.Equal(TimeSpan.FromDays(30), settings.Cooldown);     // CLI wins
        Assert.Equal(DependencyScope.Direct, settings.Scope);       // config wins over default
        Assert.Equal([CooldownSettings.NuGetOrgServiceIndex], settings.Sources); // default survives
        Assert.NotNull(configPath);
    }

    [Fact]
    public void Command_line_hours_replace_the_config_window()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "cooldownDays": 14 }""");

        var (settings, _) = SettingsBuilder.Build(new CliOptions { Command = "check", Hours = 48 }, dir.Path);

        Assert.Equal(TimeSpan.FromHours(48), settings.Cooldown);
    }

    [Fact]
    public void Config_is_probed_upward_from_the_start_directory()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "cooldownDays": 21 }""");
        var nested = dir.Combine("src", "App", "obj");
        Directory.CreateDirectory(nested);

        var (settings, _) = SettingsBuilder.Build(new CliOptions { Command = "check" }, nested);

        Assert.Equal(TimeSpan.FromDays(21), settings.Cooldown);
    }

    [Fact]
    public void No_config_flag_ignores_an_existing_config()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "cooldownDays": 21 }""");

        var (settings, configPath) = SettingsBuilder.Build(
            new CliOptions { Command = "check", NoConfig = true }, dir.Path);

        Assert.Equal(TimeSpan.FromDays(CooldownSettings.DefaultCooldownDays), settings.Cooldown);
        Assert.Null(configPath);
    }

    [Fact]
    public void Explicit_missing_config_is_an_error()
    {
        using var dir = new TempDir();
        var options = new CliOptions { Command = "check", ConfigPath = dir.Combine("nope.json") };

        Assert.Throws<CooldownConfigException>(() => SettingsBuilder.Build(options, dir.Path));
    }

    [Fact]
    public void Allow_entries_from_config_and_command_line_accumulate()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "allow": ["FromConfig"] }""");

        var options = new CliOptions { Command = "check" };
        options.Allow.Add("FromCli");
        var (settings, _) = SettingsBuilder.Build(options, dir.Path);

        Assert.True(settings.Allow.IsAllowed(PackageIdentity.Create("FromConfig", "1.0.0")));
        Assert.True(settings.Allow.IsAllowed(PackageIdentity.Create("FromCli", "1.0.0")));
    }

    [Fact]
    public void Enum_like_options_are_parsed_case_insensitively()
    {
        using var dir = new TempDir();
        var options = new CliOptions
        {
            Command = "check",
            Scope = "Direct",
            OnUnknown = "ERROR",
            OnUnlisted = "Ignore",
            OnFeedError = "error",
            OnNotRestored = "Error",
            WarnOnly = true,
        };

        var (settings, _) = SettingsBuilder.Build(options, dir.Path);

        Assert.Equal(DependencyScope.Direct, settings.Scope);
        Assert.Equal(PolicyAction.Error, settings.OnUnknown);
        Assert.Equal(PolicyAction.Ignore, settings.OnUnlisted);
        Assert.Equal(PolicyAction.Error, settings.OnFeedError);
        Assert.Equal(PolicyAction.Error, settings.OnNotRestored);
        Assert.True(settings.WarnOnly);
    }

    [Fact]
    public void Invalid_enum_value_from_the_command_line_is_rejected()
    {
        using var dir = new TempDir();
        var options = new CliOptions { Command = "check", Scope = "everything" };

        var ex = Assert.Throws<CooldownConfigException>(() => SettingsBuilder.Build(options, dir.Path));
        Assert.Contains("--scope", ex.Message);
    }

    [Fact]
    public void Timeout_and_parallel_come_from_the_command_line()
    {
        using var dir = new TempDir();
        var options = new CliOptions { Command = "check", TimeoutSeconds = 60, MaxParallel = 2 };

        var (settings, _) = SettingsBuilder.Build(options, dir.Path);

        Assert.Equal(60, settings.TimeoutSeconds);
        Assert.Equal(2, settings.MaxConcurrency);
    }

    [Fact]
    public void Cli_sources_replace_config_sources()
    {
        using var dir = new TempDir();
        dir.WriteFile(ConfigFileLoader.FileName, """{ "sources": ["https://cfg.test/index.json"] }""");

        var options = new CliOptions { Command = "check" };
        options.Sources.Add("https://cli.test/index.json");
        var (settings, _) = SettingsBuilder.Build(options, dir.Path);

        Assert.Equal(["https://cli.test/index.json"], settings.Sources);
    }
}
