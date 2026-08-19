using NuGetCooldown.Checking;
using NuGetCooldown.Configuration;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;
using NuGetCooldown.Projects;
using Xunit;

namespace NuGetCooldown.Tests;

public class CooldownCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static ProjectPackages Project(string name, params ResolvedPackage[] packages) =>
        new(name, $"/x/{name}/obj/project.assets.json", packages);

    private static ResolvedPackage Pkg(string id, string version = "1.0.0", bool direct = true) =>
        new(PackageIdentity.Create(id, version), direct);

    private static Task<CheckReport> CheckAsync(
        FakeProvider provider,
        CooldownSettings? settings = null,
        params ProjectPackages[] projects)
    {
        var checker = new CooldownChecker(provider, new FakeTime(Now));
        return checker.CheckAsync(
            projects.Length == 0 ? [Project("App", Pkg("Foo"))] : projects,
            settings ?? new CooldownSettings(),
            toolVersion: "1.0.0-test",
            CancellationToken.None);
    }

    [Fact]
    public async Task Package_exactly_at_the_cooldown_boundary_passes()
    {
        // Published exactly 7 days ago: age == cooldown, which counts as cleared.
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddDays(-7));

        var report = await CheckAsync(provider);

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Ok, result.Status);
        Assert.Equal(0, report.ExitCode);
        Assert.True(report.IsClean);
    }

    [Fact]
    public async Task Package_one_hour_inside_the_window_is_a_violation()
    {
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddDays(-7).AddHours(1));

        var report = await CheckAsync(provider);

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Violation, result.Status);
        Assert.Equal(Severity.Error, result.Severity);
        Assert.Equal("NCD001", result.DiagnosticCode);
        Assert.Contains("cooldown is 7 days", result.Message);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public async Task Warn_only_downgrades_violations_and_exits_zero()
    {
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddDays(-1));

        var report = await CheckAsync(provider, new CooldownSettings { WarnOnly = true });

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Violation, result.Status);
        Assert.Equal(Severity.Warning, result.Severity);
        Assert.Equal(0, report.ExitCode);
        Assert.False(report.IsClean); // warn-only findings must still block the MSBuild stamp
    }

    [Fact]
    public async Task Allowed_packages_are_not_even_looked_up()
    {
        var provider = new FakeProvider().AddPublished(Pkg("Fresh").Identity, Now.AddDays(-1));
        var settings = new CooldownSettings { Allow = new AllowList(["Fresh"]) };

        var report = await CheckAsync(provider, settings, Project("App", Pkg("Fresh")));

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Allowed, result.Status);
        Assert.Equal(Severity.None, result.Severity);
        Assert.Equal(0, report.ExitCode);
        Assert.Empty(provider.Lookups);
    }

    [Fact]
    public async Task Unlisted_old_package_follows_the_unlisted_policy()
    {
        var identity = Pkg("Withdrawn").Identity;
        var provider = new FakeProvider().AddPublished(identity, Now.AddDays(-400), listed: false);

        var warnReport = await CheckAsync(provider, new CooldownSettings(),
            Project("App", Pkg("Withdrawn")));
        Assert.Equal(Severity.Warning, Assert.Single(warnReport.Results).Severity);
        Assert.Equal(PackageStatus.Unlisted, warnReport.Results[0].Status);
        Assert.Equal("NCD003", warnReport.Results[0].DiagnosticCode);

        var errorReport = await CheckAsync(provider, new CooldownSettings { OnUnlisted = PolicyAction.Error },
            Project("App", Pkg("Withdrawn")));
        Assert.Equal(1, errorReport.ExitCode);

        var ignoreReport = await CheckAsync(provider, new CooldownSettings { OnUnlisted = PolicyAction.Ignore },
            Project("App", Pkg("Withdrawn")));
        Assert.Equal(Severity.None, Assert.Single(ignoreReport.Results).Severity);
        Assert.True(ignoreReport.IsClean);
    }

    [Fact]
    public async Task Unlisted_and_young_is_a_violation_that_mentions_unlisting()
    {
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddDays(-1), listed: false);

        var report = await CheckAsync(provider);

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Violation, result.Status);
        Assert.Contains("unlisted", result.Message);
    }

    [Fact]
    public async Task Unknown_package_follows_the_unknown_policy()
    {
        var provider = new FakeProvider(); // NotFound for everything

        var report = await CheckAsync(provider, new CooldownSettings { OnUnknown = PolicyAction.Error });

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Unknown, result.Status);
        Assert.Equal("NCD002", result.DiagnosticCode);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public async Task Feed_error_follows_the_feed_error_policy()
    {
        var provider = new FakeProvider().Add(Pkg("Foo").Identity, PublishLookupResult.Error("boom"));

        var report = await CheckAsync(provider);

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.FeedError, result.Status);
        Assert.Equal(Severity.Warning, result.Severity);
        Assert.Contains("boom", result.Message);
    }

    [Fact]
    public async Task Same_package_across_projects_is_checked_once_and_attributed_to_both()
    {
        var identity = Pkg("Shared").Identity;
        var provider = new FakeProvider().AddPublished(identity, Now.AddDays(-100));

        var report = await CheckAsync(provider, null,
            Project("Web", Pkg("Shared", direct: true)),
            Project("Worker", Pkg("Shared", direct: false)));

        var result = Assert.Single(report.Results);
        Assert.Equal(["Web", "Worker"], result.Projects);
        Assert.True(result.IsDirect); // direct anywhere counts as direct
        Assert.Equal(1, provider.Lookups[identity]);
    }

    [Fact]
    public async Task Direct_scope_skips_transitive_packages()
    {
        var provider = new FakeProvider()
            .AddPublished(Pkg("Direct").Identity, Now.AddDays(-100))
            .AddPublished(Pkg("Transitive").Identity, Now.AddDays(-1));

        var report = await CheckAsync(provider, new CooldownSettings { Scope = DependencyScope.Direct },
            Project("App", Pkg("Direct", direct: true), Pkg("Transitive", direct: false)));

        var result = Assert.Single(report.Results);
        Assert.Equal("Direct", result.Package.Id);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public async Task Results_are_sorted_most_severe_first()
    {
        var provider = new FakeProvider()
            .AddPublished(Pkg("Old").Identity, Now.AddDays(-100))
            .AddPublished(Pkg("Fresh").Identity, Now.AddDays(-1))
            .AddPublished(Pkg("Withdrawn").Identity, Now.AddDays(-100), listed: false);

        var report = await CheckAsync(provider, null,
            Project("App", Pkg("Old"), Pkg("Fresh"), Pkg("Withdrawn")));

        Assert.Equal(["Fresh", "Withdrawn", "Old"], report.Results.Select(r => r.Package.Id));
    }

    [Fact]
    public async Task Zero_cooldown_never_violates()
    {
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddMinutes(-5));

        var report = await CheckAsync(provider, new CooldownSettings { Cooldown = TimeSpan.Zero });

        Assert.Equal(PackageStatus.Ok, Assert.Single(report.Results).Status);
    }

    [Fact]
    public async Task Sub_day_cooldown_in_hours_is_honored()
    {
        // Published 4 hours ago, cooldown 12 hours: a violation that a days-only window could not express.
        var provider = new FakeProvider().AddPublished(Pkg("Foo").Identity, Now.AddHours(-4));

        var report = await CheckAsync(provider, new CooldownSettings { Cooldown = TimeSpan.FromHours(12) });

        var result = Assert.Single(report.Results);
        Assert.Equal(PackageStatus.Violation, result.Status);
        Assert.Contains("cooldown is 12 hours", result.Message);
        Assert.Contains("8 hours remaining", result.Message);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_before_any_lookup()
    {
        var provider = new FakeProvider();

        await Assert.ThrowsAsync<CooldownConfigException>(() =>
            CheckAsync(provider, new CooldownSettings { Cooldown = TimeSpan.FromDays(-1) }));
        Assert.Empty(provider.Lookups);
    }
}
