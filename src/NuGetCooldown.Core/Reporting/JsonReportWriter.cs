using System.Text.Json;
using NuGetCooldown.Json;
using NuGetCooldown.Model;

namespace NuGetCooldown.Reporting;

/// <summary>Machine-readable JSON output for CI pipelines. The shape is versioned via <c>schemaVersion</c>.</summary>
public static class JsonReportWriter
{
    /// <summary>Serializes the full report, including OK and allowed packages.</summary>
    public static void Write(CheckReport report, TextWriter output)
    {
        var dto = new JsonReportDto(
            SchemaVersion: 1,
            ToolVersion: report.ToolVersion,
            CheckedAtUtc: report.CheckedAtUtc,
            CooldownDays: Math.Round(report.Cooldown.TotalDays, 4),
            CooldownHours: Math.Round(report.Cooldown.TotalHours, 2),
            Scope: report.Scope.ToString().ToLowerInvariant(),
            Sources: report.Sources,
            Projects: report.ProjectNames,
            NotRestoredProjects: report.NotRestoredProjects,
            NotRestoredSeverity: report.NotRestoredSeverity.ToString().ToLowerInvariant(),
            StaleProjects: report.StaleProjects,
            Summary: new JsonSummaryDto(
                Total: report.Results.Count,
                Violations: report.Count(PackageStatus.Violation),
                Unlisted: report.Count(PackageStatus.Unlisted),
                Unknown: report.Count(PackageStatus.Unknown),
                FeedErrors: report.Count(PackageStatus.FeedError),
                Allowed: report.Count(PackageStatus.Allowed),
                Ok: report.Count(PackageStatus.Ok),
                CacheHits: report.CacheHits),
            Results: report.Results.Select(r => new JsonResultDto(
                Id: r.Package.Id,
                Version: r.Package.Version,
                Status: r.Status.ToString().ToLowerInvariant(),
                Severity: r.Severity.ToString().ToLowerInvariant(),
                DiagnosticCode: r.DiagnosticCode,
                PublishedUtc: r.PublishedUtc?.ToUniversalTime(),
                AgeDays: r.AgeDays is { } age ? Math.Round(age, 2) : null,
                Listed: r.Listed,
                Direct: r.IsDirect,
                Projects: r.Projects,
                Source: r.SourceUrl,
                FromCache: r.FromCache,
                Message: r.Message)).ToArray(),
            ElapsedSeconds: Math.Round(report.ElapsedSeconds, 2),
            ExitCode: report.ExitCode);

        output.WriteLine(JsonSerializer.Serialize(dto, CoreJsonContext.Default.JsonReportDto));
    }
}

/// <summary>Root of the JSON report.</summary>
public sealed record JsonReportDto(
    int SchemaVersion,
    string ToolVersion,
    DateTimeOffset CheckedAtUtc,
    double CooldownDays,
    double CooldownHours,
    string Scope,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> NotRestoredProjects,
    string NotRestoredSeverity,
    IReadOnlyList<string> StaleProjects,
    JsonSummaryDto Summary,
    IReadOnlyList<JsonResultDto> Results,
    double ElapsedSeconds,
    int ExitCode);

/// <summary>Aggregate counts in the JSON report.</summary>
public sealed record JsonSummaryDto(
    int Total,
    int Violations,
    int Unlisted,
    int Unknown,
    int FeedErrors,
    int Allowed,
    int Ok,
    int CacheHits);

/// <summary>One package result in the JSON report.</summary>
public sealed record JsonResultDto(
    string Id,
    string Version,
    string Status,
    string Severity,
    string? DiagnosticCode,
    DateTimeOffset? PublishedUtc,
    double? AgeDays,
    bool? Listed,
    bool Direct,
    IReadOnlyList<string> Projects,
    string? Source,
    bool FromCache,
    string? Message);
