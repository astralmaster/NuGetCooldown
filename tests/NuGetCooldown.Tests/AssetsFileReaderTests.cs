using NuGetCooldown.Configuration;
using NuGetCooldown.Model;
using NuGetCooldown.Projects;
using Xunit;

namespace NuGetCooldown.Tests;

public class AssetsFileReaderTests
{
    [Fact]
    public void Reads_packages_with_direct_flags_and_skips_project_references()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("obj/project.assets.json", TestData.AssetsJson);

        var result = AssetsFileReader.Read(path);

        Assert.Equal("App", result.ProjectName);
        Assert.Equal(3, result.Packages.Count);

        var byId = result.Packages.ToDictionary(p => p.Identity.Id);
        Assert.True(byId["Newtonsoft.Json"].IsDirect);
        Assert.True(byId["Serilog.Sinks.Console"].IsDirect);
        Assert.False(byId["Serilog"].IsDirect);
        Assert.DoesNotContain("MyLib", byId.Keys);
        Assert.Equal("13.0.3", byId["Newtonsoft.Json"].Identity.Version);
    }

    [Fact]
    public void Project_name_falls_back_to_the_directory_layout()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("MyService/obj/project.assets.json", """{ "version": 3, "libraries": {} }""");

        Assert.Equal("MyService", AssetsFileReader.Read(path).ProjectName);
    }

    [Fact]
    public void Malformed_json_throws_a_usage_error_naming_the_file()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("obj/project.assets.json", "not json at all");

        var ex = Assert.Throws<CooldownUsageException>(() => AssetsFileReader.Read(path));
        Assert.Contains("project.assets.json", ex.Message);
    }

    [Fact]
    public void Missing_file_throws_a_usage_error()
    {
        Assert.Throws<CooldownUsageException>(() => AssetsFileReader.Read(
            Path.Combine(Path.GetTempPath(), "ncd-does-not-exist", "project.assets.json")));
    }

    [Fact]
    public void Direct_entry_without_version_range_is_recognized()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("obj/project.assets.json", """
            {
              "version": 3,
              "libraries": { "Bare/1.0.0": { "type": "package" } },
              "projectFileDependencyGroups": { "net8.0": [ "Bare" ] }
            }
            """);

        var package = Assert.Single(AssetsFileReader.Read(path).Packages);
        Assert.True(package.IsDirect);
        Assert.Equal(PackageIdentity.Create("Bare", "1.0.0"), package.Identity);
    }
}
