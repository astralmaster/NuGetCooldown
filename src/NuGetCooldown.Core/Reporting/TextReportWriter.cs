using NuGetCooldown.Model;

namespace NuGetCooldown.Reporting;

/// <summary>Human-readable console output.</summary>
public sealed class TextReportWriter(TextWriter output, bool useColor)
{
    /// <summary>Writes the report. Findings are always shown; OK/allowed packages only when <paramref name="verbose"/>.</summary>
    public void Write(CheckReport report, bool verbose)
    {
        output.WriteLine(
            $"NuGetCooldown {report.ToolVersion} — cooldown: {report.CooldownText}, " +
            $"scope: {(report.Scope == DependencyScope.All ? "all packages" : "direct only")}, " +
            $"sources: {string.Join(", ", report.Sources.Select(ShortSource))}");

        if (report.ProjectNames.Count > 0)
        {
            output.WriteLine($"Projects: {string.Join(", ", report.ProjectNames)}");
        }

        output.WriteLine();

        if (report.NotRestoredSeverity != Severity.None)
        {
            var color = report.NotRestoredSeverity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
            foreach (var project in report.NotRestoredProjects)
            {
                var marker = report.NotRestoredSeverity == Severity.Error ? "x" : "!";
                WriteColored(color, $"  {marker} {Path.GetFileName(project)} has not been restored — run 'dotnet restore' to include it");
                output.WriteLine();
            }
        }

        var shown = report.Results
            .Where(r => verbose || r.Severity != Severity.None)
            .ToList();

        if (shown.Count > 0)
        {
            var width = Math.Min(60, shown.Max(r => r.Package.Id.Length + r.Package.Version.Length + 1));
            foreach (var result in shown)
            {
                WriteResult(result, width);
            }

            output.WriteLine();
        }

        WriteSummary(report);
    }

    private void WriteResult(PackageResult result, int width)
    {
        var (marker, color) = result switch
        {
            { Severity: Severity.Error } => ("x", ConsoleColor.Red),
            { Severity: Severity.Warning } => ("!", ConsoleColor.Yellow),
            { Status: PackageStatus.Allowed } => ("-", ConsoleColor.DarkGray),
            _ => ("+", ConsoleColor.DarkGreen),
        };

        var name = $"{result.Package.Id} {result.Package.Version}".PadRight(width);
        var detail = result.Message ?? DescribeOk(result);
        var origin = $"[{(result.IsDirect ? "direct" : "transitive")}; {string.Join(", ", result.Projects)}]";

        WriteColored(color, $"  {marker} {name}  {detail}  {origin}");
        output.WriteLine();
    }

    private static string DescribeOk(PackageResult result) =>
        result.PublishedUtc is { } published
            ? $"published {published:yyyy-MM-dd} ({DurationFormat.Humanize(TimeSpan.FromDays(result.AgeDays!.Value))} ago)"
            : "ok";

    private void WriteSummary(CheckReport report)
    {
        var cachePart = report.CacheHits > 0 ? $", {report.CacheHits} from cache" : "";
        output.WriteLine(
            $"Checked {report.CheckedCount} package version(s) across {report.ProjectNames.Count} project(s) " +
            $"in {report.ElapsedSeconds:0.0}s{cachePart}.");

        var findings = new List<string>();
        AddCount(findings, report.Count(PackageStatus.Violation), "violation");
        AddCount(findings, report.Count(PackageStatus.Unlisted), "unlisted version");
        AddCount(findings, report.Count(PackageStatus.Unknown), "unknown publish date");
        AddCount(findings, report.Count(PackageStatus.FeedError), "feed error");
        if (report.NotRestoredSeverity != Severity.None && report.NotRestoredProjects.Count > 0)
        {
            AddCount(findings, report.NotRestoredProjects.Count, "unrestored project");
        }

        if (findings.Count == 0)
        {
            WriteColored(ConsoleColor.DarkGreen, "All packages have cleared the cooldown window.");
            output.WriteLine();
        }
        else
        {
            var color = report.HasErrors ? ConsoleColor.Red : ConsoleColor.Yellow;
            WriteColored(color, string.Join(", ", findings) + ".");
            output.WriteLine();
        }
    }

    private static void AddCount(List<string> parts, int count, string label)
    {
        if (count > 0)
        {
            parts.Add($"{count} {label}{(count == 1 ? "" : "s")}");
        }
    }

    private static string ShortSource(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source;

    private void WriteColored(ConsoleColor color, string text)
    {
        if (!useColor)
        {
            output.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        output.Write(text);
        Console.ForegroundColor = previous;
    }
}
