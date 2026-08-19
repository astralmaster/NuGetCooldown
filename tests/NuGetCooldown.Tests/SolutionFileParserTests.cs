using NuGetCooldown.Configuration;
using NuGetCooldown.Projects;
using Xunit;

namespace NuGetCooldown.Tests;

public class SolutionFileParserTests
{
    [Fact]
    public void Sln_yields_project_paths_and_skips_solution_folders()
    {
        using var dir = new TempDir();
        var slnPath = dir.WriteFile("All.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{F2A71F9B-5D33-465A-A702-920D77279786}") = "Tool", "tools/Tool.fsproj", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            Global
            EndGlobal
            """);

        var projects = SolutionFileParser.GetProjectPaths(slnPath);

        Assert.Equal(2, projects.Count);
        Assert.Contains(dir.Combine("src", "App", "App.csproj"), projects);
        Assert.Contains(dir.Combine("tools", "Tool.fsproj"), projects);
    }

    [Fact]
    public void Slnx_yields_project_paths_including_nested_folders()
    {
        using var dir = new TempDir();
        var slnxPath = dir.WriteFile("All.slnx", """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/App/App.csproj" />
              </Folder>
              <Project Path="tools\Tool.vbproj" />
              <Project Path="docs/notes.md" />
            </Solution>
            """);

        var projects = SolutionFileParser.GetProjectPaths(slnxPath);

        Assert.Equal(2, projects.Count);
        Assert.Contains(dir.Combine("src", "App", "App.csproj"), projects);
        Assert.Contains(dir.Combine("tools", "Tool.vbproj"), projects);
    }

    [Fact]
    public void Invalid_slnx_xml_throws_a_usage_error()
    {
        using var dir = new TempDir();
        var path = dir.WriteFile("Broken.slnx", "<Solution><Project</Solution>");

        Assert.Throws<CooldownUsageException>(() => SolutionFileParser.GetProjectPaths(path));
    }

    [Fact]
    public void Non_solution_extension_throws()
    {
        Assert.Throws<CooldownUsageException>(() => SolutionFileParser.GetProjectPaths("whatever.txt"));
    }
}
