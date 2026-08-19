using System.Text.RegularExpressions;
using NuGet.Versioning;
using NuGetCooldown.Model;

namespace NuGetCooldown.Configuration;

/// <summary>
/// A list of packages exempt from the cooldown check. Each entry is either an id pattern
/// (<c>MyCompany.*</c>) that exempts every version, or <c>Id@Version</c> which exempts specific
/// versions. <c>*</c> is a wildcard in both parts; matching is case-insensitive.
/// </summary>
public sealed class AllowList
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly List<Entry> _entries;

    /// <summary>The original patterns, in the order they were supplied.</summary>
    public IReadOnlyList<string> Patterns { get; }

    /// <summary>An allow list that matches nothing.</summary>
    public static AllowList Empty { get; } = new([]);

    /// <summary>Parses <paramref name="patterns"/>, throwing <see cref="CooldownConfigException"/> on invalid entries.</summary>
    public AllowList(IEnumerable<string> patterns)
    {
        Patterns = patterns.ToArray();
        _entries = Patterns.Select(Entry.Parse).ToList();
    }

    /// <summary>True when <paramref name="package"/> matches any entry.</summary>
    public bool IsAllowed(PackageIdentity package) => _entries.Any(e => e.Matches(package));

    private sealed class Entry
    {
        private readonly Regex _idPattern;
        private readonly Regex? _versionPattern;
        private readonly string? _exactVersion;

        private Entry(Regex idPattern, Regex? versionPattern, string? exactVersion)
        {
            _idPattern = idPattern;
            _versionPattern = versionPattern;
            _exactVersion = exactVersion;
        }

        public static Entry Parse(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new CooldownConfigException("Allow-list entry must not be empty.");
            }

            var parts = pattern.Trim().Split('@');
            if (parts.Length > 2)
            {
                throw new CooldownConfigException(
                    $"Allow-list entry '{pattern}' is invalid: expected 'IdPattern' or 'IdPattern@Version'.");
            }

            var idPattern = GlobToRegex(parts[0]);

            Regex? versionPattern = null;
            string? exactVersion = null;
            if (parts.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(parts[1]))
                {
                    throw new CooldownConfigException(
                        $"Allow-list entry '{pattern}' is invalid: the version after '@' is empty.");
                }

                if (parts[1].Contains('*'))
                {
                    versionPattern = GlobToRegex(parts[1]);
                }
                else
                {
                    // Normalize so "1.0" in the allow list matches the resolved "1.0.0".
                    exactVersion = NuGetVersion.TryParse(parts[1], out var v)
                        ? v.ToNormalizedString()
                        : parts[1].Trim();
                }
            }

            return new Entry(idPattern, versionPattern, exactVersion);
        }

        public bool Matches(PackageIdentity package)
        {
            if (!_idPattern.IsMatch(package.Id))
            {
                return false;
            }

            if (_exactVersion is not null)
            {
                return string.Equals(_exactVersion, package.Version, StringComparison.OrdinalIgnoreCase);
            }

            return _versionPattern is null || _versionPattern.IsMatch(package.Version);
        }

        private static Regex GlobToRegex(string glob)
        {
            if (string.IsNullOrWhiteSpace(glob))
            {
                throw new CooldownConfigException("Allow-list entry has an empty id pattern.");
            }

            var escaped = Regex.Escape(glob.Trim()).Replace(@"\*", ".*");
            return new Regex(
                $"^{escaped}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
    }
}
