using System.Reflection;
using NuGetCooldown.Checking;
using NuGetCooldown.Configuration;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using NuGetCooldown.Projects;
using NuGetCooldown.Reporting;

namespace NuGetCooldown.Cli;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        CliOptions? options = null;
        try
        {
            TryEnableUtf8Output();
            options = ArgsParser.Parse(args);

            return options.Command switch
            {
                "check" => await RunCheckAsync(options, cts.Token),
                "info" => await RunInfoAsync(options, cts.Token),
                "clear-cache" => RunClearCache(options),
                "version" => Print(ToolVersion),
                _ => Print(HelpText.Text),
            };
        }
        catch (Exception ex) when (ex is CooldownUsageException or CooldownConfigException)
        {
            ReportFatal(options, DiagnosticCodes.Usage, ex.Message);
            return 2;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("nuget-cooldown: canceled.");
            return 3;
        }
        catch (Exception ex)
        {
            ReportFatal(options, DiagnosticCodes.Internal, $"unexpected failure: {ex}");
            return 3;
        }
    }

    private static async Task<int> RunCheckAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(options.Path ?? ".");
        var inputs = InputResolver.Resolve(inputPath);

        var probeStart = Directory.Exists(inputPath) ? inputPath : Path.GetDirectoryName(inputPath)!;
        var (settings, _) = SettingsBuilder.Build(options, probeStart);

        var projects = inputs.GraphFiles.Select(DependencyGraphReader.Read).ToList();

        var checker = new CooldownChecker(
            CreateProvider(options, settings), TimeProvider.System, settings.MaxConcurrency);
        var report = await checker.CheckAsync(projects, settings, ToolVersion, cancellationToken);
        report = report with
        {
            NotRestoredProjects = inputs.NotRestoredProjects,
            NotRestoredSeverity = settings.OnNotRestored.ToSeverity(),
            StaleProjects = inputs.StaleProjects,
        };

        if (options.MSBuildOrigin is { } origin)
        {
            MSBuildReportWriter.Write(report, origin, Console.Out);
        }
        else if (options.Format == "json")
        {
            JsonReportWriter.Write(report, Console.Out);
        }
        else
        {
            new TextReportWriter(Console.Out, UseColor()).Write(report, options.Verbose, options.Quiet);
        }

        if (options.StampFilePath is { } stampPath)
        {
            StampFile.Update(report, stampPath);
        }

        return report.ExitCode;
    }

    private static async Task<int> RunInfoAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var (settings, _) = SettingsBuilder.Build(options, Directory.GetCurrentDirectory());
        var package = PackageIdentity.Create(options.PackageId!, options.PackageVersion!);

        var lookup = await CreateProvider(options, settings)
            .GetPublishInfoAsync(package, cancellationToken);

        Console.WriteLine(package);
        switch (lookup)
        {
            case { Outcome: LookupOutcome.Found, Info: { } info }:
                var published = info.PublishedUtc?.ToUniversalTime();
                var age = published is { } p ? TimeProvider.System.GetUtcNow() - p : (TimeSpan?)null;
                Console.WriteLine(published is { } d
                    ? $"  published: {d:yyyy-MM-dd HH:mm} UTC ({DurationFormat.Humanize(age!.Value)} ago)"
                    : "  published: unknown");
                Console.WriteLine($"  listed:    {(info.Listed ? "yes" : "no (withdrawn?)")}");
                Console.WriteLine($"  source:    {info.SourceUrl}{(info.FromCache ? " (cached)" : "")}");
                if (age is { } a)
                {
                    var window = settings.Cooldown;
                    Console.WriteLine(a >= window
                        ? $"  cooldown:  cleared (window is {settings.CooldownText})"
                        : $"  cooldown:  ACTIVE — {DurationFormat.Humanize(window - a)} remaining of {settings.CooldownText}");
                }

                return 0;

            case { Outcome: LookupOutcome.NotFound }:
                Console.WriteLine($"  {lookup.Message ?? "not found on any configured source"}");
                return 1;

            default:
                Console.WriteLine($"  lookup failed: {lookup.Message}");
                return 1;
        }
    }

    private static int RunClearCache(CliOptions options)
    {
        var cache = new FileCache(options.CacheDir);
        Console.WriteLine(cache.Clear()
            ? $"Cache cleared: {cache.Root}"
            : $"Cache is already empty: {cache.Root}");
        return 0;
    }

    private static IPackagePublishInfoProvider CreateProvider(CliOptions options, CooldownSettings settings)
    {
        if (options.Offline)
        {
            return new OfflineCacheProvider(new FileCache(options.CacheDir));
        }

        var client = new NuGetV3Client(
            NuGetHttpClientFactory.Create(ToolVersion, settings.TimeoutSeconds), settings.Sources);
        return options.NoCache
            ? client
            : new CachingPublishInfoProvider(client, new FileCache(options.CacheDir), TimeProvider.System);
    }

    private static void ReportFatal(CliOptions? options, string code, string message)
    {
        if (options?.MSBuildOrigin is { } origin)
        {
            // MSBuild mode parses stdout; a canonical error line makes the build fail visibly.
            Console.Out.WriteLine(
                MSBuildReportWriter.CanonicalLine(origin, Severity.Error, code, $"NuGetCooldown: {message}"));
        }
        else
        {
            Console.Error.WriteLine($"nuget-cooldown: error: {message}");
            if (code == DiagnosticCodes.Usage)
            {
                Console.Error.WriteLine("Run 'nuget-cooldown --help' for usage.");
            }
        }
    }

    private static int Print(string text)
    {
        Console.WriteLine(text);
        return 0;
    }

    private static bool UseColor() =>
        !Console.IsOutputRedirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    private static void TryEnableUtf8Output()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            // Some hosts (services, exotic terminals) refuse; plain output still works.
        }
    }

    /// <summary>The package version, without source-control metadata.</summary>
    internal static string ToolVersion { get; } = GetToolVersion();

    private static string GetToolVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+');
        return plus > 0 ? informational[..plus] : informational;
    }
}
