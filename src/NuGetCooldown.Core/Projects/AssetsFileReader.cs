using System.Text.Json;
using NuGetCooldown.Configuration;
using NuGetCooldown.Model;

namespace NuGetCooldown.Projects;

/// <summary>One package in a project's resolved dependency graph.</summary>
/// <param name="Identity">The resolved package version.</param>
/// <param name="IsDirect">True when the project references the package directly.</param>
public sealed record ResolvedPackage(PackageIdentity Identity, bool IsDirect);

/// <summary>The packages resolved for one project.</summary>
/// <param name="ProjectName">The project name recorded in (or inferred for) the source file.</param>
/// <param name="SourceFilePath">The file the data came from (<c>project.assets.json</c> or <c>packages.lock.json</c>).</param>
/// <param name="Packages">Every resolved package: direct and transitive.</param>
public sealed record ProjectPackages(
    string ProjectName,
    string SourceFilePath,
    IReadOnlyList<ResolvedPackage> Packages);

/// <summary>
/// Reads the full resolved dependency graph — direct and transitive — from a project's
/// <c>project.assets.json</c>, the file NuGet writes on restore.
/// </summary>
public static class AssetsFileReader
{
    /// <summary>Parses <paramref name="assetsFilePath"/>.</summary>
    /// <exception cref="CooldownUsageException">The file is missing or not a valid assets file.</exception>
    public static ProjectPackages Read(string assetsFilePath)
    {
        JsonDocument doc;
        try
        {
            using var stream = File.OpenRead(assetsFilePath);
            doc = JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CooldownUsageException(
                $"Could not read assets file '{assetsFilePath}': {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            var directIds = ReadDirectPackageIds(root);
            var packages = new List<ResolvedPackage>();

            if (root.TryGetProperty("libraries", out var libraries)
                && libraries.ValueKind == JsonValueKind.Object)
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    // Library keys look like "Serilog/4.2.0"; type distinguishes packages from project references.
                    if (!library.Value.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.String
                        || !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var separator = library.Name.IndexOf('/');
                    if (separator <= 0 || separator == library.Name.Length - 1)
                    {
                        continue;
                    }

                    var id = library.Name[..separator];
                    var version = library.Name[(separator + 1)..];
                    packages.Add(new ResolvedPackage(
                        PackageIdentity.Create(id, version),
                        directIds.Contains(id)));
                }
            }

            return new ProjectPackages(ReadProjectName(root, assetsFilePath), assetsFilePath, packages);
        }
    }

    private static HashSet<string> ReadDirectPackageIds(JsonElement root)
    {
        // projectFileDependencyGroups holds the project's own PackageReferences per framework,
        // as strings like "Serilog >= 4.2.0".
        var directIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("projectFileDependencyGroups", out var groups)
            || groups.ValueKind != JsonValueKind.Object)
        {
            return directIds;
        }

        foreach (var group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in group.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = entry.GetString()!;
                var space = text.IndexOf(' ');
                directIds.Add(space > 0 ? text[..space] : text);
            }
        }

        return directIds;
    }

    private static string ReadProjectName(JsonElement root, string assetsFilePath)
    {
        if (root.TryGetProperty("project", out var project)
            && project.ValueKind == JsonValueKind.Object
            && project.TryGetProperty("restore", out var restore)
            && restore.ValueKind == JsonValueKind.Object
            && restore.TryGetProperty("projectName", out var projectName)
            && projectName.ValueKind == JsonValueKind.String)
        {
            return projectName.GetString()!;
        }

        // The assets file conventionally lives at <project>/obj/project.assets.json.
        var objDir = Path.GetDirectoryName(Path.GetFullPath(assetsFilePath));
        return Path.GetFileName(Path.GetDirectoryName(objDir)) ?? "unknown";
    }
}
