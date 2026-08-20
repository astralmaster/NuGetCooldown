using NuGetCooldown.Model;

namespace NuGetCooldown.Reporting;

/// <summary>
/// MSBuild-canonical output (<c>{project} : error NCD001: …</c>) so findings surface as real
/// build errors and warnings in the terminal, Visual Studio's Error List, and CI log parsers.
/// </summary>
public static class MSBuildReportWriter
{
    private const string DiagnosticsUrl = "https://github.com/astralmaster/NuGetCooldown#diagnostics";

    /// <summary>Writes findings as canonical error/warning lines attributed to <paramref name="origin"/>.</summary>
    public static void Write(CheckReport report, string origin, TextWriter output)
    {
        foreach (var result in report.Results.Where(r => r.Severity != Severity.None))
        {
            output.WriteLine(CanonicalLine(origin, result.Severity, result.DiagnosticCode!, Describe(result)));
        }

        if (report.NotRestoredSeverity != Severity.None)
        {
            foreach (var project in report.NotRestoredProjects)
            {
                output.WriteLine(CanonicalLine(
                    origin,
                    report.NotRestoredSeverity,
                    DiagnosticCodes.NotRestored,
                    $"Project {Path.GetFileName(project)} has not been restored, so its packages were not checked "
                    + "(run 'dotnet restore')."));
            }
        }

        foreach (var project in report.StaleProjects)
        {
            output.WriteLine(CanonicalLine(
                origin,
                Severity.Warning,
                DiagnosticCodes.StaleGraph,
                $"Project {Path.GetFileName(project)} was edited after its last restore; "
                + "results may be stale (run 'dotnet restore')."));
        }

        output.WriteLine(
            $"NuGetCooldown: checked {report.CheckedCount} package version(s), cooldown {report.CooldownText}" +
            (report.CacheHits > 0 ? $" ({report.CacheHits} from cache)" : "") + ".");
    }

    /// <summary>
    /// Formats a single MSBuild-canonical diagnostic line. Both the report writer and the fatal-error
    /// path use this, so the grammar the <c>.targets</c> relies on to fail the build is defined once.
    /// A message is flattened to one line, because a newline would break that grammar.
    /// </summary>
    public static string CanonicalLine(string origin, Severity severity, string code, string message)
    {
        var category = severity == Severity.Error ? "error" : "warning";
        return $"{origin} : {category} {code}: {Flatten(message)}";
    }

    private static string Describe(PackageResult result) =>
        $"Package {result.Package.Id} {result.Package.Version} {result.Message} " +
        $"[{(result.IsDirect ? "direct" : "transitive")}] ({DiagnosticsUrl})";

    private static string Flatten(string message) =>
        message.ReplaceLineEndings(" ").Trim();
}
