using System.Text.Json;
using System.Text.RegularExpressions;
using NuGetCooldown.Model;
using NuGetCooldown.Reporting;
using Xunit;

namespace NuGetCooldown.Tests;

public class ReportWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static PackageResult Result(
        string id,
        PackageStatus status,
        Severity severity,
        string? code = null,
        string? message = null) => new()
    {
        Package = PackageIdentity.Create(id, "1.0.0"),
        Status = status,
        Severity = severity,
        DiagnosticCode = code,
        Message = message,
        PublishedUtc = status is PackageStatus.Unknown or PackageStatus.FeedError ? null : Now.AddDays(-3),
        AgeDays = status is PackageStatus.Unknown or PackageStatus.FeedError ? null : 3,
        Listed = status != PackageStatus.Unlisted,
        IsDirect = true,
        Projects = ["App"],
        SourceUrl = "https://api.nuget.org/v3/index.json",
        FromCache = true,
    };

    private static CheckReport Report(params PackageResult[] results) => new()
    {
        ToolVersion = "1.0.0-test",
        CheckedAtUtc = Now,
        Cooldown = TimeSpan.FromDays(7),
        Scope = DependencyScope.All,
        Sources = ["https://api.nuget.org/v3/index.json"],
        ProjectNames = ["App"],
        Results = results,
        ElapsedSeconds = 1.234,
    };

    [Fact]
    public void Text_output_shows_findings_and_summary()
    {
        var report = Report(
            Result("Fresh", PackageStatus.Violation, Severity.Error, "NCD001", "published 3 days ago; cooldown is 7 days (4 days remaining)"),
            Result("Fine", PackageStatus.Ok, Severity.None));

        var writer = new StringWriter();
        new TextReportWriter(writer, useColor: false).Write(report, verbose: false);
        var text = writer.ToString();

        Assert.Contains("Fresh 1.0.0", text);
        Assert.Contains("4 days remaining", text);
        Assert.DoesNotContain("Fine 1.0.0", text); // OK packages hidden unless verbose
        Assert.Contains("1 violation", text);
        Assert.Contains("cooldown: 7 days", text);
    }

    [Fact]
    public void Text_verbose_output_includes_passing_packages()
    {
        var report = Report(Result("Fine", PackageStatus.Ok, Severity.None));

        var writer = new StringWriter();
        new TextReportWriter(writer, useColor: false).Write(report, verbose: true);

        Assert.Contains("Fine 1.0.0", writer.ToString());
        Assert.Contains("All packages have cleared the cooldown window.", writer.ToString());
    }

    [Fact]
    public void Text_output_warns_about_unrestored_projects()
    {
        var report = Report() with { NotRestoredProjects = ["/x/Skipped/Skipped.csproj"] };

        var writer = new StringWriter();
        new TextReportWriter(writer, useColor: false).Write(report, verbose: false);

        Assert.Contains("Skipped.csproj", writer.ToString());
        Assert.Contains("dotnet restore", writer.ToString());
    }

    [Fact]
    public void MSBuild_output_is_canonical_and_attributed_to_the_project()
    {
        var report = Report(
            Result("Fresh", PackageStatus.Violation, Severity.Error, "NCD001", "published 3 days ago"),
            Result("Odd", PackageStatus.Unknown, Severity.Warning, "NCD002", "not found on any configured source"),
            Result("Fine", PackageStatus.Ok, Severity.None));

        var writer = new StringWriter();
        MSBuildReportWriter.Write(report, @"C:\repo\App.csproj", writer);
        var lines = writer.ToString().Split(writer.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // MSBuild's canonical error format: "<origin> : error CODE: message".
        Assert.Matches(new Regex(@"^C:\\repo\\App\.csproj : error NCD001: .*Fresh"), lines[0]);
        Assert.Matches(new Regex(@"^C:\\repo\\App\.csproj : warning NCD002: .*Odd"), lines[1]);
        Assert.Equal(3, lines.Length); // two findings + one summary line, nothing for OK packages
        Assert.Contains("checked 3 package version(s)", lines[2]);
    }

    [Fact]
    public void MSBuild_output_surfaces_unrestored_projects_under_their_policy()
    {
        var report = Report(Result("Fine", PackageStatus.Ok, Severity.None)) with
        {
            NotRestoredProjects = [@"C:\repo\Skipped\Skipped.csproj"],
            NotRestoredSeverity = Severity.Error,
        };

        var writer = new StringWriter();
        MSBuildReportWriter.Write(report, @"C:\repo\App.csproj", writer);
        var text = writer.ToString();

        Assert.Matches(new Regex(@"error NCD005: .*Skipped\.csproj"), text);
        Assert.Contains("dotnet restore", text);
    }

    [Fact]
    public void Json_output_is_stable_and_complete()
    {
        var report = Report(
            Result("Fresh", PackageStatus.Violation, Severity.Error, "NCD001", "too young"),
            Result("Fine", PackageStatus.Ok, Severity.None));

        var writer = new StringWriter();
        JsonReportWriter.Write(report, writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(7, root.GetProperty("cooldownDays").GetDouble());
        Assert.Equal(168, root.GetProperty("cooldownHours").GetDouble());
        Assert.Equal("all", root.GetProperty("scope").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("violations").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("ok").GetInt32());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("cacheHits").GetInt32());

        var first = root.GetProperty("results")[0];
        Assert.Equal("Fresh", first.GetProperty("id").GetString());
        Assert.Equal("violation", first.GetProperty("status").GetString());
        Assert.Equal("error", first.GetProperty("severity").GetString());
        Assert.Equal("NCD001", first.GetProperty("diagnosticCode").GetString());
        Assert.Equal(3, first.GetProperty("ageDays").GetDouble());
        Assert.True(first.GetProperty("direct").GetBoolean());
    }
}
