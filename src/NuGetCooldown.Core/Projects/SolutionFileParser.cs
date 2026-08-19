using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGetCooldown.Configuration;

namespace NuGetCooldown.Projects;

/// <summary>Extracts project paths from <c>.sln</c> and <c>.slnx</c> solution files.</summary>
public static partial class SolutionFileParser
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    [GeneratedRegex("""^Project\("\{[^}]*\}"\)\s*=\s*"[^"]*",\s*"([^"]*)"\s*,""", RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectLine();

    /// <summary>Returns the absolute paths of all C#/F#/VB projects in the solution.</summary>
    /// <exception cref="CooldownUsageException">The file cannot be read or parsed.</exception>
    public static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        var fullPath = Path.GetFullPath(solutionPath);
        var extension = Path.GetExtension(fullPath);

        return extension.ToLowerInvariant() switch
        {
            ".sln" => ParseSln(fullPath),
            ".slnx" => ParseSlnx(fullPath),
            _ => throw new CooldownUsageException($"'{solutionPath}' is not a .sln or .slnx file."),
        };
    }

    private static List<string> ParseSln(string path)
    {
        var solutionDir = Path.GetDirectoryName(path)!;
        var projects = new List<string>();

        foreach (var line in ReadLines(path))
        {
            var match = SlnProjectLine().Match(line);
            if (match.Success)
            {
                AddIfProject(projects, solutionDir, match.Groups[1].Value);
            }
        }

        return projects;
    }

    private static List<string> ParseSlnx(string path)
    {
        var solutionDir = Path.GetDirectoryName(path)!;
        var projects = new List<string>();

        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new CooldownUsageException($"Could not parse solution '{path}': {ex.Message}");
        }

        foreach (var element in doc.Descendants("Project"))
        {
            if (element.Attribute("Path")?.Value is { Length: > 0 } relativePath)
            {
                AddIfProject(projects, solutionDir, relativePath);
            }
        }

        return projects;
    }

    private static void AddIfProject(List<string> projects, string solutionDir, string relativePath)
    {
        // Solution files always use backslashes; normalize for the current OS.
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        if (ProjectExtensions.Contains(Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase))
        {
            projects.Add(Path.GetFullPath(Path.Combine(solutionDir, normalized)));
        }
    }

    private static string[] ReadLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CooldownUsageException($"Could not read solution '{path}': {ex.Message}");
        }
    }
}
