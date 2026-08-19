using NuGetCooldown.Configuration;
using NuGetCooldown.Projects;
using Xunit;

namespace NuGetCooldown.Tests;

public class InputResolverTests
{
    private const string MinimalAssets = """{ "version": 3, "libraries": {} }""";

    [Fact]
    public void Assets_file_path_is_used_directly()
    {
        using var dir = new TempDir();
        var assets = dir.WriteFile("obj/project.assets.json", MinimalAssets);

        var resolved = InputResolver.Resolve(assets);

        Assert.Equal([assets], resolved.GraphFiles);
        Assert.Empty(resolved.NotRestoredProjects);
    }

    [Fact]
    public void Project_file_resolves_to_its_assets_file()
    {
        using var dir = new TempDir();
        var project = dir.WriteFile("App/App.csproj", "<Project />");
        var assets = dir.WriteFile("App/obj/project.assets.json", MinimalAssets);

        var resolved = InputResolver.Resolve(project);

        Assert.Equal([assets], resolved.GraphFiles);
    }

    [Fact]
    public void Unrestored_single_project_throws_with_restore_hint()
    {
        using var dir = new TempDir();
        var project = dir.WriteFile("App/App.csproj", "<Project />");

        var ex = Assert.Throws<CooldownUsageException>(() => InputResolver.Resolve(project));
        Assert.Contains("dotnet restore", ex.Message);
    }

    [Fact]
    public void Directory_scan_finds_projects_recursively_and_reports_unrestored_ones()
    {
        using var dir = new TempDir();
        dir.WriteFile("src/A/A.csproj", "<Project />");
        var assetsA = dir.WriteFile("src/A/obj/project.assets.json", MinimalAssets);
        var projectB = dir.WriteFile("src/B/B.csproj", "<Project />");
        // Decoys that must be skipped:
        dir.WriteFile("src/A/bin/Fake/Fake.csproj", "<Project />");
        dir.WriteFile("node_modules/dep/Dep.csproj", "<Project />");

        var resolved = InputResolver.Resolve(dir.Path);

        Assert.Equal([assetsA], resolved.GraphFiles);
        Assert.Equal([projectB], resolved.NotRestoredProjects);
    }

    [Fact]
    public void Directory_with_a_solution_uses_the_solution()
    {
        using var dir = new TempDir();
        dir.WriteFile("App/App.csproj", "<Project />");
        var assets = dir.WriteFile("App/obj/project.assets.json", MinimalAssets);
        // A stray project NOT in the solution must be ignored.
        dir.WriteFile("Stray/Stray.csproj", "<Project />");
        dir.WriteFile("All.sln", """
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            """);

        var resolved = InputResolver.Resolve(dir.Path);

        Assert.Equal([assets], resolved.GraphFiles);
        Assert.Empty(resolved.NotRestoredProjects);
    }

    [Fact]
    public void Directory_with_two_solutions_requires_an_explicit_choice()
    {
        using var dir = new TempDir();
        dir.WriteFile("One.sln", "");
        dir.WriteFile("Two.sln", "");

        Assert.Throws<CooldownUsageException>(() => InputResolver.Resolve(dir.Path));
    }

    [Fact]
    public void Directory_with_a_single_slnx_is_not_double_counted()
    {
        // Regression: Directory.EnumerateFiles(dir, "*.sln") also matches ".slnx" via Windows 8.3
        // short-name aliasing, which used to count one .slnx solution as two and abort.
        using var dir = new TempDir();
        dir.WriteFile("App/App.csproj", "<Project />");
        var assets = dir.WriteFile("App/obj/project.assets.json", MinimalAssets);
        dir.WriteFile("App.slnx", """
            <Solution>
              <Project Path="App/App.csproj" />
            </Solution>
            """);

        var resolved = InputResolver.Resolve(dir.Path);

        Assert.Equal([assets], resolved.GraphFiles);
    }

    [Fact]
    public void Lock_file_path_is_used_directly()
    {
        using var dir = new TempDir();
        var lockFile = dir.WriteFile("packages.lock.json", """{ "version": 1, "dependencies": {} }""");

        var resolved = InputResolver.Resolve(lockFile);

        Assert.Equal([lockFile], resolved.GraphFiles);
    }

    [Fact]
    public void Project_without_assets_falls_back_to_its_lock_file()
    {
        using var dir = new TempDir();
        var project = dir.WriteFile("App/App.csproj", "<Project />");
        var lockFile = dir.WriteFile("App/packages.lock.json", """{ "version": 1, "dependencies": {} }""");

        var resolved = InputResolver.Resolve(project);

        Assert.Equal([lockFile], resolved.GraphFiles);
        Assert.Empty(resolved.NotRestoredProjects);
    }

    [Fact]
    public void Assets_file_is_preferred_over_a_lock_file()
    {
        using var dir = new TempDir();
        var project = dir.WriteFile("App/App.csproj", "<Project />");
        var assets = dir.WriteFile("App/obj/project.assets.json", MinimalAssets);
        dir.WriteFile("App/packages.lock.json", """{ "version": 1, "dependencies": {} }""");

        var resolved = InputResolver.Resolve(project);

        Assert.Equal([assets], resolved.GraphFiles);
    }

    [Fact]
    public void Empty_directory_throws()
    {
        using var dir = new TempDir();

        Assert.Throws<CooldownUsageException>(() => InputResolver.Resolve(dir.Path));
    }

    [Fact]
    public void Missing_path_throws()
    {
        Assert.Throws<CooldownUsageException>(
            () => InputResolver.Resolve(Path.Combine(Path.GetTempPath(), "ncd-missing-" + Guid.NewGuid())));
    }

    [Fact]
    public void Unsupported_file_type_throws()
    {
        using var dir = new TempDir();
        var file = dir.WriteFile("readme.txt", "hi");

        Assert.Throws<CooldownUsageException>(() => InputResolver.Resolve(file));
    }
}
