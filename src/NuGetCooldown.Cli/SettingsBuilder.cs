using NuGetCooldown.Configuration;
using NuGetCooldown.Model;

namespace NuGetCooldown.Cli;

/// <summary>Merges defaults, the config file, and command-line options into effective settings.</summary>
internal static class SettingsBuilder
{
    /// <summary>
    /// Builds settings. Precedence, lowest to highest: built-in defaults, then the config file
    /// (explicit <c>--config</c>, or the nearest <c>nuget-cooldown.json</c> walking up from
    /// <paramref name="probeStartDirectory"/>), then command-line options. Allow-list entries
    /// accumulate across layers instead of replacing each other.
    /// </summary>
    public static (CooldownSettings Settings, string? ConfigPath) Build(
        CliOptions options,
        string probeStartDirectory)
    {
        var settings = new CooldownSettings();

        string? configPath = null;
        if (!options.NoConfig)
        {
            if (options.ConfigPath is not null)
            {
                if (!File.Exists(options.ConfigPath))
                {
                    throw new CooldownConfigException($"Config file '{options.ConfigPath}' does not exist.");
                }

                configPath = Path.GetFullPath(options.ConfigPath);
            }
            else
            {
                configPath = ConfigFileLoader.Probe(probeStartDirectory);
            }

            if (configPath is not null)
            {
                settings = ConfigFileLoader.Apply(settings, configPath);
            }
        }

        // Specifying either unit on the command line replaces the window entirely.
        if (options.Days is not null || options.Hours is not null)
        {
            settings = settings with { Cooldown = ConfigFileLoader.ToWindow(options.Days, options.Hours) };
        }

        if (options.Scope is { } scope)
        {
            settings = settings with
            {
                Scope = ConfigFileLoader.ParseEnum<DependencyScope>(scope, "--scope"),
            };
        }

        if (options.Sources.Count > 0)
        {
            settings = settings with { Sources = options.Sources };
        }

        if (options.Allow.Count > 0)
        {
            settings = settings with
            {
                Allow = new AllowList([.. settings.Allow.Patterns, .. options.Allow]),
            };
        }

        if (options.OnUnknown is { } onUnknown)
        {
            settings = settings with
            {
                OnUnknown = ConfigFileLoader.ParseEnum<PolicyAction>(onUnknown, "--on-unknown"),
            };
        }

        if (options.OnUnlisted is { } onUnlisted)
        {
            settings = settings with
            {
                OnUnlisted = ConfigFileLoader.ParseEnum<PolicyAction>(onUnlisted, "--on-unlisted"),
            };
        }

        if (options.OnFeedError is { } onFeedError)
        {
            settings = settings with
            {
                OnFeedError = ConfigFileLoader.ParseEnum<PolicyAction>(onFeedError, "--on-feed-error"),
            };
        }

        if (options.OnNotRestored is { } onNotRestored)
        {
            settings = settings with
            {
                OnNotRestored = ConfigFileLoader.ParseEnum<PolicyAction>(onNotRestored, "--on-not-restored"),
            };
        }

        if (options.WarnOnly)
        {
            settings = settings with { WarnOnly = true };
        }

        if (options.TimeoutSeconds is { } timeout)
        {
            settings = settings with { TimeoutSeconds = timeout };
        }

        if (options.MaxParallel is { } maxParallel)
        {
            settings = settings with { MaxConcurrency = maxParallel };
        }

        settings.Validate();
        return (settings, configPath);
    }
}
