using NuGetCooldown.Configuration;
using NuGetCooldown.Model;
using NuGetCooldown.Projects;
using Xunit;

namespace NuGetCooldown.Tests;

public class LockFileReaderTests
{
    private const string LockJson = """
        {
          "version": 1,
          "dependencies": {
            "net8.0": {
              "Serilog": { "type": "Direct", "requested": "[4.0.0, )", "resolved": "4.0.0", "contentHash": "a" },
              "Serilog.Sinks.Console": { "type": "Transitive", "resolved": "6.0.0", "contentHash": "b" },
              "MyLib": { "type": "Project" },
              "Central.Pkg": { "type": "CentralTransitive", "requested": "[1.0.0, )", "resolved": "1.0.0", "contentHash": "c" }
            }
          }
        }
        """;

    [Fact]
    public void Reads_direct_and_transitive_packages_and_skips_project_references()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("App/packages.lock.json", LockJson);

        var result = LockFileReader.Read(path);

        Assert.Equal("App", result.ProjectName);
        var byId = result.Packages.ToDictionary(p => p.Identity.Id);
        Assert.Equal(3, byId.Count); // MyLib (Project) is excluded
        Assert.True(byId["Serilog"].IsDirect);
        Assert.False(byId["Serilog.Sinks.Console"].IsDirect);
        Assert.False(byId["Central.Pkg"].IsDirect); // CentralTransitive counts as transitive
        Assert.Equal("6.0.0", byId["Serilog.Sinks.Console"].Identity.Version);
    }

    [Fact]
    public void A_package_direct_in_any_framework_is_direct()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("App/packages.lock.json", """
            {
              "version": 1,
              "dependencies": {
                "net8.0": { "Shared": { "type": "Transitive", "resolved": "1.0.0" } },
                "net9.0": { "Shared": { "type": "Direct", "requested": "[1.0.0, )", "resolved": "1.0.0" } }
              }
            }
            """);

        var package = Assert.Single(LockFileReader.Read(path).Packages);
        Assert.True(package.IsDirect);
    }

    [Fact]
    public void Malformed_lock_file_throws_a_usage_error()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("App/packages.lock.json", "not json");

        Assert.Throws<CooldownUsageException>(() => LockFileReader.Read(path));
    }

    [Fact]
    public void DependencyGraphReader_dispatches_by_file_name()
    {
        using var dir = new TempDir();
        var lockPath = dir.WriteFile("App/packages.lock.json", LockJson);
        var assetsPath = dir.WriteFile("Other/obj/project.assets.json", TestData.AssetsJson);

        Assert.Equal(3, DependencyGraphReader.Read(lockPath).Packages.Count);
        Assert.Equal(3, DependencyGraphReader.Read(assetsPath).Packages.Count);
        Assert.Throws<CooldownUsageException>(
            () => DependencyGraphReader.Read(dir.WriteFile("x.txt", "")));
    }
}
