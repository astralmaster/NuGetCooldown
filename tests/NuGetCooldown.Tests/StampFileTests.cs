using NuGetCooldown.Checking;
using NuGetCooldown.Model;
using Xunit;

namespace NuGetCooldown.Tests;

public class StampFileTests
{
    private static CheckReport Report(params PackageResult[] results) => new()
    {
        ToolVersion = "1.0.0-test",
        CheckedAtUtc = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero),
        CooldownDays = 7,
        Scope = DependencyScope.All,
        Sources = ["https://api.nuget.org/v3/index.json"],
        ProjectNames = ["App"],
        Results = results,
        ElapsedSeconds = 0.1,
    };

    private static PackageResult Result(Severity severity) => new()
    {
        Package = PackageIdentity.Create("Foo", "1.0.0"),
        Status = severity == Severity.None ? PackageStatus.Ok : PackageStatus.Violation,
        Severity = severity,
    };

    [Fact]
    public void Clean_report_writes_the_stamp()
    {
        using var dir = new TempDir();
        var stamp = dir.Combine("obj", "cooldown.stamp");

        StampFile.Update(Report(Result(Severity.None)), stamp);

        Assert.True(File.Exists(stamp));
    }

    [Fact]
    public void Findings_remove_a_stale_stamp()
    {
        using var dir = new TempDir();
        var stamp = dir.WriteFile("obj/cooldown.stamp", "old");

        StampFile.Update(Report(Result(Severity.Warning)), stamp);

        Assert.False(File.Exists(stamp));
    }

    [Fact]
    public void Unrestored_projects_block_the_stamp()
    {
        using var dir = new TempDir();
        var stamp = dir.Combine("obj", "cooldown.stamp");
        var report = Report(Result(Severity.None)) with { NotRestoredProjects = ["X.csproj"] };

        StampFile.Update(report, stamp);

        Assert.False(File.Exists(stamp));
    }
}
