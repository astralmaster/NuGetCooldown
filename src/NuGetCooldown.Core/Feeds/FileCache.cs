using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGetCooldown.Json;
using NuGetCooldown.Model;

namespace NuGetCooldown.Feeds;

/// <summary>A cached publish-info answer for one package version.</summary>
public sealed record CacheEntry(
    int SchemaVersion,
    string Id,
    string Version,
    DateTimeOffset? PublishedUtc,
    bool Listed,
    string SourceUrl,
    bool FromCatalog,
    DateTimeOffset CachedAtUtc);

/// <summary>
/// Disk cache for publish dates. A version's publish date never changes once it is on a feed,
/// so entries never expire — repeat builds are fast and work offline. One small JSON file per
/// package version keeps concurrent builds safe: writes go to a temp file and are moved into
/// place atomically, and unreadable entries are simply refetched.
/// </summary>
/// <remarks>
/// The package id and version come from an untrusted <c>project.assets.json</c>, so they are never
/// used as path segments directly. Each file is placed under a hash of the identity, which bounds
/// the path length, cannot escape the cache root, and cannot collide; the entry additionally stores
/// its id and version so a read verifies it belongs to the package that was asked for.
/// </remarks>
public sealed class FileCache
{
    /// <summary>Environment variable that overrides the cache location.</summary>
    public const string CacheDirEnvVar = "NUGET_COOLDOWN_CACHE_DIR";

    private const int CurrentSchemaVersion = 1;

    /// <summary>The cache root directory.</summary>
    public string Root { get; }

    /// <summary>
    /// Creates a cache rooted at <paramref name="cacheDir"/>, the <c>NUGET_COOLDOWN_CACHE_DIR</c>
    /// environment variable, or the per-user default, in that order.
    /// </summary>
    public FileCache(string? cacheDir = null)
    {
        var root = cacheDir
                   ?? Environment.GetEnvironmentVariable(CacheDirEnvVar)
                   ?? DefaultRoot();

        // A trailing separator can arrive from an MSBuild directory property; drop it so the path is clean.
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NuGetCooldown", "cache", "v2");

    /// <summary>Returns the cached entry, or <see langword="null"/> when absent, unreadable, or mismatched.</summary>
    public CacheEntry? TryGet(PackageIdentity package)
    {
        var path = EntryPath(package);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var entry = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.CacheEntry);

            // Trust the entry only when it is the current schema AND actually describes this package;
            // otherwise a hash collision or a tampered file could serve one package's date as another's.
            if (entry is null
                || entry.SchemaVersion != CurrentSchemaVersion
                || !string.Equals(entry.Id, package.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or locked entries self-heal on the next successful fetch.
            return null;
        }
    }

    /// <summary>Stores an entry. Best-effort: cache write failures never fail a check.</summary>
    public void Set(PackageIdentity package, CacheEntry entry)
    {
        var path = EntryPath(package);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, CoreJsonContext.Default.CacheEntry));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or contended file systems (CI sandboxes) are fine; the feed remains the source of truth.
            TryDelete(tempPath);
        }
    }

    /// <summary>Deletes the entire cache. Returns false when there was nothing to delete.</summary>
    public bool Clear()
    {
        if (!Directory.Exists(Root))
        {
            return false;
        }

        Directory.Delete(Root, recursive: true);
        return true;
    }

    private string EntryPath(PackageIdentity package)
    {
        // Two hex chars of shard directory keep any single folder from growing without bound;
        // the remainder names the file. Both derive from a hash of the (already lowercased) identity.
        var hash = IdentityHash(package);
        return Path.Combine(Root, hash[..2], hash + ".json");
    }

    private static string IdentityHash(PackageIdentity package)
    {
        // A separator that cannot appear in either half keeps "a|b"+"c" distinct from "a"+"b|c".
        var key = package.LowerId + "\n" + package.LowerVersion;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more we can do; a stray temp file is harmless.
        }
    }
}
