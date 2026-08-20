using NuGetCooldown.Configuration;

namespace NuGetCooldown.Cli;

/// <summary>
/// Hand-rolled argument parser. Deliberate: a supply-chain tool that ends up inside every build
/// should carry as few dependencies as possible, and the surface is one page of options.
/// </summary>
internal static class ArgsParser
{
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        if (args.Length == 0 || args[0] is "--help" or "-h" or "-?" or "help")
        {
            options.Command = "help";
            return options;
        }

        if (args[0] == "--version")
        {
            options.Command = "version";
            return options;
        }

        options.Command = args[0] switch
        {
            "check" or "info" or "clear-cache" => args[0],
            _ => throw new CooldownUsageException(
                $"Unknown command '{args[0]}'. Commands: check, info, clear-cache. See 'nuget-cooldown --help'."),
        };

        var positionals = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h" or "-?")
            {
                options.Command = "help";
                return options;
            }

            if (!arg.StartsWith('-'))
            {
                positionals.Add(arg);
                continue;
            }

            // Support both "--days 7" and "--days=7".
            string? inlineValue = null;
            var equals = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                inlineValue = arg[(equals + 1)..];
                arg = arg[..equals];
            }

            string Value()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new CooldownUsageException($"Option '{arg}' requires a value.");
                }

                return args[++i];
            }

            switch (options.Command, arg)
            {
                case ("check", "--days") or ("check", "-d"):
                    options.Days = ParseCount(Value(), "days");
                    break;
                case ("check", "--hours"):
                    options.Hours = ParseCount(Value(), "hours");
                    break;
                case ("check", "--config") or ("check", "-c"):
                    options.ConfigPath = Value();
                    break;
                case ("check", "--no-config"):
                    options.NoConfig = true;
                    break;
                case ("check", "--source") or ("check", "-s") or ("info", "--source") or ("info", "-s"):
                    AddSplit(options.Sources, Value());
                    break;
                case ("check", "--allow"):
                    AddSplit(options.Allow, Value());
                    break;
                case ("check", "--scope"):
                    options.Scope = Value();
                    break;
                case ("check", "--on-unknown"):
                    options.OnUnknown = Value();
                    break;
                case ("check", "--on-unlisted"):
                    options.OnUnlisted = Value();
                    break;
                case ("check", "--on-feed-error"):
                    options.OnFeedError = Value();
                    break;
                case ("check", "--on-not-restored"):
                    options.OnNotRestored = Value();
                    break;
                case ("check", "--warn-only"):
                    options.WarnOnly = true;
                    break;
                case ("check", "--timeout"):
                    options.TimeoutSeconds = ParseCount(Value(), "seconds");
                    break;
                case ("check", "--max-parallel"):
                    options.MaxParallel = ParseCount(Value(), "parallelism");
                    break;
                case ("check", "--quiet") or ("check", "-q"):
                    options.Quiet = true;
                    break;
                case ("check", "--format") or ("check", "-f"):
                    options.Format = Value() switch
                    {
                        "text" => "text",
                        "json" => "json",
                        var other => throw new CooldownUsageException(
                            $"'{other}' is not a valid format; expected one of: text, json."),
                    };
                    break;
                case ("check", "--verbose") or ("check", "-v"):
                    options.Verbose = true;
                    break;
                case ("check", "--msbuild"):
                    options.MSBuildOrigin = Value();
                    break;
                case ("check", "--stamp-file"):
                    options.StampFilePath = Value();
                    break;
                case ("check", "--no-cache") or ("info", "--no-cache"):
                    options.NoCache = true;
                    break;
                case ("check", "--offline") or ("info", "--offline"):
                    options.Offline = true;
                    break;
                case ("check", "--cache-dir") or ("info", "--cache-dir") or ("clear-cache", "--cache-dir"):
                    options.CacheDir = Value();
                    break;
                case ("info", "--days") or ("info", "-d"):
                    options.Days = ParseCount(Value(), "days");
                    break;
                case ("info", "--hours"):
                    options.Hours = ParseCount(Value(), "hours");
                    break;
                default:
                    throw new CooldownUsageException(
                        $"Unknown option '{arg}' for '{options.Command}'. See 'nuget-cooldown --help'.");
            }
        }

        AssignPositionals(options, positionals);
        return options;
    }

    private static void AssignPositionals(CliOptions options, List<string> positionals)
    {
        switch (options.Command)
        {
            case "check":
                if (positionals.Count > 1)
                {
                    throw new CooldownUsageException(
                        $"'check' takes at most one path; got: {string.Join(", ", positionals)}.");
                }

                options.Path = positionals.FirstOrDefault();
                break;

            case "info":
                if (positionals.Count != 2)
                {
                    throw new CooldownUsageException("'info' requires a package id and a version.");
                }

                options.PackageId = positionals[0];
                options.PackageVersion = positionals[1];
                break;

            case "clear-cache":
                if (positionals.Count > 0)
                {
                    throw new CooldownUsageException("'clear-cache' takes no arguments.");
                }

                break;
        }
    }

    private static int ParseCount(string value, string unit) =>
        int.TryParse(value, out var count)
            ? count
            : throw new CooldownUsageException($"'{value}' is not a valid number of {unit}.");

    /// <summary>Splits semicolon-separated values so MSBuild property lists map onto a single flag.</summary>
    private static void AddSplit(List<string> target, string value) =>
        target.AddRange(value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
