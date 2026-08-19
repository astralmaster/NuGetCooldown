using System.Text.Json;
using NuGetCooldown.Configuration;
using NuGetCooldown.Model;

namespace NuGetCooldown.Projects;

/// <summary>
/// Reads the resolved dependency graph from a project's <c>packages.lock.json</c>. Unlike the
/// assets file, the lock file exists <em>before</em> a full restore (when
/// <c>RestorePackagesWithLockFile</c> is enabled), so this lets the check run pre-restore and in
/// environments that only commit the lock file.
/// </summary>
public static class LockFileReader
{
    /// <summary>The well-known lock file name.</summary>
    public const string FileName = "packages.lock.json";

    /// <summary>Parses <paramref name="lockFilePath"/>.</summary>
    /// <exception cref="CooldownUsageException">The file is missing or not a valid lock file.</exception>
    public static ProjectPackages Read(string lockFilePath)
    {
        JsonDocument doc;
        try
        {
            using var stream = File.OpenRead(lockFilePath);
            doc = JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CooldownUsageException(
                $"Could not read lock file '{lockFilePath}': {ex.Message}");
        }

        using (doc)
        {
            var packages = new Dictionary<PackageIdentity, bool>();

            if (doc.RootElement.TryGetProperty("dependencies", out var frameworks)
                && frameworks.ValueKind == JsonValueKind.Object)
            {
                foreach (var framework in frameworks.EnumerateObject())
                {
                    if (framework.Value.ValueKind == JsonValueKind.Object)
                    {
                        ReadFramework(framework.Value, packages);
                    }
                }
            }

            var resolved = packages
                .Select(kv => new ResolvedPackage(kv.Key, kv.Value))
                .ToList();

            return new ProjectPackages(InferProjectName(lockFilePath), lockFilePath, resolved);
        }
    }

    private static void ReadFramework(JsonElement framework, Dictionary<PackageIdentity, bool> packages)
    {
        foreach (var dependency in framework.EnumerateObject())
        {
            if (dependency.Value.ValueKind != JsonValueKind.Object
                || !dependency.Value.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var type = typeElement.GetString();

            // "Project" is a project-to-project reference, not a package; everything else with a
            // resolved version is a real package (Direct, Transitive, or CentralTransitive under CPM).
            var isDirect = string.Equals(type, "Direct", StringComparison.OrdinalIgnoreCase);
            var isPackage = isDirect
                || string.Equals(type, "Transitive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "CentralTransitive", StringComparison.OrdinalIgnoreCase);

            if (!isPackage
                || !dependency.Value.TryGetProperty("resolved", out var resolvedElement)
                || resolvedElement.ValueKind != JsonValueKind.String
                || resolvedElement.GetString() is not { Length: > 0 } version)
            {
                continue;
            }

            var identity = PackageIdentity.Create(dependency.Name, version);

            // The same package can appear under several frameworks; keep it direct if it is direct anywhere.
            packages[identity] = packages.TryGetValue(identity, out var existingDirect)
                ? existingDirect || isDirect
                : isDirect;
        }
    }

    private static string InferProjectName(string lockFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(lockFilePath));
        return Path.GetFileName(directory) ?? "unknown";
    }
}
