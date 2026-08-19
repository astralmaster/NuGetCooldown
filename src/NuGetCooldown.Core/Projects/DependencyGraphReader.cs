using NuGetCooldown.Configuration;

namespace NuGetCooldown.Projects;

/// <summary>Reads a resolved dependency graph from either a <c>project.assets.json</c> or a <c>packages.lock.json</c>.</summary>
public static class DependencyGraphReader
{
    /// <summary>The assets file NuGet writes on restore.</summary>
    public const string AssetsFileName = "project.assets.json";

    /// <summary>Dispatches to the right reader based on the file name.</summary>
    /// <exception cref="CooldownUsageException">The file is not a recognized dependency-graph file.</exception>
    public static ProjectPackages Read(string path)
    {
        var name = Path.GetFileName(path);

        if (string.Equals(name, AssetsFileName, StringComparison.OrdinalIgnoreCase))
        {
            return AssetsFileReader.Read(path);
        }

        if (string.Equals(name, LockFileReader.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return LockFileReader.Read(path);
        }

        throw new CooldownUsageException(
            $"'{path}' is not a {AssetsFileName} or {LockFileReader.FileName} file.");
    }
}
