using NuGetCooldown.Model;

namespace NuGetCooldown.Reporting;

/// <summary>
/// MSBuild-canonical output (<c>{project} : error NCD001: …</c>) so findings surface as real
/// build errors and warnings in the terminal, Visual Studio's Error List, and CI log parsers.
/// </summary>
public static class MSBuildReportWriter
{
    /// <summary>Writes findings as canonical error/warning lines attributed to <paramref name="origin"/>.</summary>
    public static void Write(CheckReport report, string origin, TextWriter output)
    {
        foreach (var result in report.Results.Where(r => r.Severity != Severity.None))
        {
            var category = result.Severity == Severity.Error ? "error" : "warning";
            output.WriteLine($"{origin} : {category} {result.DiagnosticCode}: {Describe(result)}");
        }

        var checkedCount = report.Results.Count(r => r.Status != PackageStatus.Allowed);
        output.WriteLine(
            $"NuGetCooldown: checked {checkedCount} package version(s), cooldown {report.CooldownDays} days" +
            (report.CacheHits > 0 ? $" ({report.CacheHits} from cache)" : "") + ".");
    }

    private static string Describe(PackageResult result) =>
        $"Package {result.Package.Id} {result.Package.Version} {result.Message} " +
        $"[{(result.IsDirect ? "direct" : "transitive")}] " +
        "(https://github.com/astralmaster/NuGetCooldown#diagnostics)";
}
