using NuGetCooldown.Configuration;

namespace NuGetCooldown.Projects;

/// <summary>The dependency-graph files to check, plus any projects that were found but not restored.</summary>
/// <param name="GraphFiles">Absolute paths of the <c>project.assets.json</c> / <c>packages.lock.json</c> files to check.</param>
/// <param name="NotRestoredProjects">Projects with no graph file — neither restore output nor a lock file exists.</param>
public sealed record ResolvedInputs(
    IReadOnlyList<string> GraphFiles,
    IReadOnlyList<string> NotRestoredProjects);

/// <summary>
/// Turns the CLI's path argument — a directory, a solution, a project file, an assets file, or a
/// lock file — into the set of dependency-graph files to check.
/// </summary>
public static class InputResolver
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    private static readonly HashSet<string> SkippedDirectories = new(
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "packages", "artifacts"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves <paramref name="path"/> to dependency-graph files.</summary>
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
        var name = Path.GetFileName(fullPath);
        if (string.Equals(name, DependencyGraphReader.AssetsFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LockFileReader.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedInputs([fullPath], []);
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".sln" or ".slnx")
        {
            return FromProjects(SolutionFileParser.GetProjectPaths(fullPath));
        }

        if (ProjectExtensions.Contains(extension))
        {
            return FromProjects([fullPath]);
        }

        throw new CooldownUsageException(
            $"'{fullPath}' is not a solution, project, {DependencyGraphReader.AssetsFileName}, "
            + $"or {LockFileReader.FileName} file.");
    }

    private static ResolvedInputs ResolveDirectory(string directory)
    {
        // Enumerate by exact extension: a bare "*.sln" wildcard also matches ".slnx" via Windows
        // 8.3 short-name aliasing, which would double-count a single .slnx solution.
        var solutions = EnumerateByExtension(directory, ".sln")
            .Concat(EnumerateByExtension(directory, ".slnx"))
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

            foreach (var extension in ProjectExtensions)
            {
                results.AddRange(EnumerateByExtension(dir, extension));
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

    /// <summary>Enumerates files whose extension is exactly <paramref name="extension"/> (case-insensitive).</summary>
    private static IEnumerable<string> EnumerateByExtension(string directory, string extension) =>
        Directory.EnumerateFiles(directory, "*" + extension)
            .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase));

    private static ResolvedInputs FromProjects(IReadOnlyList<string> projectPaths)
    {
        var graphFiles = new List<string>();
        var notRestored = new List<string>();

        foreach (var project in projectPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var projectDir = Path.GetDirectoryName(project)!;
            var assetsFile = Path.Combine(projectDir, "obj", DependencyGraphReader.AssetsFileName);
            var lockFile = Path.Combine(projectDir, LockFileReader.FileName);

            // Prefer the restore output; fall back to a committed lock file so the check can run pre-restore.
            if (File.Exists(assetsFile))
            {
                graphFiles.Add(assetsFile);
            }
            else if (File.Exists(lockFile))
            {
                graphFiles.Add(lockFile);
            }
            else
            {
                notRestored.Add(project);
            }
        }

        if (graphFiles.Count == 0)
        {
            throw new CooldownUsageException(
                notRestored.Count > 0
                    ? $"None of the {notRestored.Count} project(s) have been restored — run 'dotnet restore' first."
                    : "No projects found to check.");
        }

        return new ResolvedInputs(graphFiles, notRestored);
    }
}
