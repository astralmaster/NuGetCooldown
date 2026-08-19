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
        Root = cacheDir
               ?? Environment.GetEnvironmentVariable(CacheDirEnvVar)
               ?? DefaultRoot();
    }

    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NuGetCooldown", "cache", "v1");

    /// <summary>Returns the cached entry, or <see langword="null"/> when absent or unreadable.</summary>
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
            return entry?.SchemaVersion == CurrentSchemaVersion ? entry : null;
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, CoreJsonContext.Default.CacheEntry));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or contended file systems (CI sandboxes) are fine; the feed remains the source of truth.
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

    private string EntryPath(PackageIdentity package) =>
        Path.Combine(Root, Sanitize(package.LowerId), Sanitize(package.LowerVersion) + ".json");

    /// <summary>Package ids/versions are already filesystem-safe; this guards against hostile input.</summary>
    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' ? c : '_';
        }

        return new string(buffer);
    }
}
