using NuGet.Versioning;

namespace NuGetCooldown.Model;

/// <summary>
/// Identifies one package version. The version is stored in NuGet-normalized form
/// (for example <c>1.0</c> becomes <c>1.0.0</c> and build metadata is stripped), and
/// equality is case-insensitive, matching NuGet's own identity semantics.
/// </summary>
public sealed record PackageIdentity
{
    /// <summary>The package id as it appears in the input (original casing preserved).</summary>
    public string Id { get; }

    /// <summary>The NuGet-normalized version string.</summary>
    public string Version { get; }

    private PackageIdentity(string id, string version)
    {
        Id = id;
        Version = version;
    }

    /// <summary>Creates an identity, normalizing <paramref name="version"/> when it parses as a NuGet version.</summary>
    /// <remarks>An unparsable version is kept verbatim; the feed lookup will then report it as unknown.</remarks>
    public static PackageIdentity Create(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var normalized = NuGetVersion.TryParse(version, out var parsed)
            ? parsed.ToNormalizedString()
            : version.Trim();

        return new PackageIdentity(id.Trim(), normalized);
    }

    /// <summary>The lowercase id, as used in NuGet V3 API URLs.</summary>
    public string LowerId => Id.ToLowerInvariant();

    /// <summary>The lowercase normalized version, as used in NuGet V3 API URLs.</summary>
    public string LowerVersion => Version.ToLowerInvariant();

    /// <inheritdoc />
    public bool Equals(PackageIdentity? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Version));

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Version}";
}
