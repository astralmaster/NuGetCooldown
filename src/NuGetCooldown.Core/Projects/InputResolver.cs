using NuGetCooldown.Configuration;

namespace NuGetCooldown.Projects;

/// <summary>The assets files to check, plus any projects that were found but not restored.</summary>
/// <param name="AssetsFiles">Absolute paths of the <c>project.assets.json</c> files to check.</param>
/// <param name="NotRestoredProjects">Projects with no assets file — <c>dotnet restore</c> has not run.</param>
public sealed record ResolvedInputs(
    IReadOnlyList<string> AssetsFiles,
    IReadOnlyList<string> NotRestoredProjects);

/// <summary>
/// Turns the CLI's path argument — a directory, a solution, a project file, or an assets file —
/// into the set of <c>project.assets.json</c> files to check.
/// </summary>
public static class InputResolver
{
    private static readonly string[] ProjectPatterns = ["*.csproj", "*.fsproj", "*.vbproj"];

    private static readonly HashSet<string> SkippedDirectories = new(
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "packages", "artifacts"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves <paramref name="path"/> to assets files.</summary>
    /// <exception cref="CooldownUsageException">The path does not exist or contains nothing checkable.</exception>
    public static ResolvedInputs Resolve(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            return ResolveFile(fullPath);
        }

        if (Directory.Exists(fullPath))
        {
            return ResolveDirectory(fullPath);
        }

        throw new CooldownUsageException($"Path '{path}' does not exist.");
    }

    private static ResolvedInputs ResolveFile(string fullPath)
    {
        if (string.Equals(Path.GetFileName(fullPath), "project.assets.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedInputs([fullPath], []);
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".sln" or ".slnx")
        {
            return FromProjects(SolutionFileParser.GetProjectPaths(fullPath));
        }

        if (extension is ".csproj" or ".fsproj" or ".vbproj")
        {
            return FromProjects([fullPath]);
        }

        throw new CooldownUsageException(
            $"'{fullPath}' is not a solution, project, or project.assets.json file.");
    }

    private static ResolvedInputs ResolveDirectory(string directory)
    {
        var solutions = Directory.EnumerateFiles(directory, "*.sln")
            .Concat(Directory.EnumerateFiles(directory, "*.slnx"))
            .ToList();

        if (solutions.Count > 1)
        {
            throw new CooldownUsageException(
                $"Directory '{directory}' contains {solutions.Count} solution files; pass the one to check.");
        }

        if (solutions.Count == 1)
        {
            return FromProjects(SolutionFileParser.GetProjectPaths(solutions[0]));
        }

        var projects = FindProjectsRecursively(directory);
        if (projects.Count == 0)
        {
            throw new CooldownUsageException(
                $"No solution or project files found under '{directory}'.");
        }

        return FromProjects(projects);
    }

    private static List<string> FindProjectsRecursively(string root)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var pattern in ProjectPatterns)
            {
                results.AddRange(Directory.EnumerateFiles(dir, pattern));
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(subDir)))
                {
                    pending.Push(subDir);
                }
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static ResolvedInputs FromProjects(IReadOnlyList<string> projectPaths)
    {
        var assetsFiles = new List<string>();
        var notRestored = new List<string>();

        foreach (var project in projectPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var assetsFile = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
            if (File.Exists(assetsFile))
            {
                assetsFiles.Add(assetsFile);
            }
            else
            {
                notRestored.Add(project);
            }
        }

        if (assetsFiles.Count == 0)
        {
            throw new CooldownUsageException(
                notRestored.Count > 0
                    ? $"None of the {notRestored.Count} project(s) have been restored — run 'dotnet restore' first."
                    : "No projects found to check.");
        }

        return new ResolvedInputs(assetsFiles, notRestored);
    }
}
