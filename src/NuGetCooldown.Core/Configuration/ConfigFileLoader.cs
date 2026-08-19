using System.Text.Json;
using System.Text.Json.Serialization;
using NuGetCooldown.Json;
using NuGetCooldown.Model;

namespace NuGetCooldown.Configuration;

/// <summary>Shape of <c>nuget-cooldown.json</c>. Every property is optional.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfigFileDto
{
    /// <summary>Optional JSON schema reference; ignored.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>Minimum age, in days, a package version must have.</summary>
    public int? CooldownDays { get; set; }

    /// <summary>Minimum age, in hours, a package version must have. Added to <see cref="CooldownDays"/> when both are set.</summary>
    public int? CooldownHours { get; set; }

    /// <summary><c>all</c> or <c>direct</c>.</summary>
    public string? Scope { get; set; }

    /// <summary>Allow-list entries: <c>IdPattern</c> or <c>IdPattern@Version</c>.</summary>
    public string[]? Allow { get; set; }

    /// <summary>NuGet V3 service index URLs.</summary>
    public string[]? Sources { get; set; }

    /// <summary><c>warn</c>, <c>error</c>, or <c>ignore</c>.</summary>
    public string? OnUnknown { get; set; }

    /// <summary><c>warn</c>, <c>error</c>, or <c>ignore</c>.</summary>
    public string? OnUnlisted { get; set; }

    /// <summary><c>warn</c>, <c>error</c>, or <c>ignore</c>.</summary>
    public string? OnFeedError { get; set; }

    /// <summary><c>warn</c>, <c>error</c>, or <c>ignore</c>.</summary>
    public string? OnNotRestored { get; set; }
}

/// <summary>Finds and applies <c>nuget-cooldown.json</c> configuration files.</summary>
public static class ConfigFileLoader
{
    /// <summary>The well-known config file name.</summary>
    public const string FileName = "nuget-cooldown.json";

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> up to the filesystem root and returns the first
    /// <c>nuget-cooldown.json</c> found, or <see langword="null"/>.
    /// </summary>
    public static string? Probe(string startDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Parses <paramref name="path"/> and overlays its values onto <paramref name="baseSettings"/>.
    /// Allow-list entries are appended, scalar values replace the base value when present.
    /// </summary>
    public static CooldownSettings Apply(CooldownSettings baseSettings, string path)
    {
        ConfigFileDto dto;
        try
        {
            using var stream = File.OpenRead(path);
            dto = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.ConfigFileDto)
                  ?? throw new CooldownConfigException($"Config file '{path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new CooldownConfigException($"Config file '{path}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new CooldownConfigException($"Config file '{path}' could not be read: {ex.Message}", ex);
        }

        var settings = baseSettings;

        // If either unit is set, it defines the whole window (the unset unit contributes zero);
        // if neither is set, the base window is kept.
        if (dto.CooldownDays is not null || dto.CooldownHours is not null)
        {
            settings = settings with { Cooldown = ToWindow(dto.CooldownDays, dto.CooldownHours) };
        }

        if (dto.Scope is { } scope)
        {
            settings = settings with { Scope = ParseEnum<DependencyScope>(scope, "scope", path) };
        }

        if (dto.Sources is { Length: > 0 } sources)
        {
            settings = settings with { Sources = sources };
        }

        if (dto.Allow is { Length: > 0 } allow)
        {
            settings = settings with { Allow = new AllowList([.. settings.Allow.Patterns, .. allow]) };
        }

        if (dto.OnUnknown is { } onUnknown)
        {
            settings = settings with { OnUnknown = ParseEnum<PolicyAction>(onUnknown, "onUnknown", path) };
        }

        if (dto.OnUnlisted is { } onUnlisted)
        {
            settings = settings with { OnUnlisted = ParseEnum<PolicyAction>(onUnlisted, "onUnlisted", path) };
        }

        if (dto.OnFeedError is { } onFeedError)
        {
            settings = settings with { OnFeedError = ParseEnum<PolicyAction>(onFeedError, "onFeedError", path) };
        }

        if (dto.OnNotRestored is { } onNotRestored)
        {
            settings = settings with { OnNotRestored = ParseEnum<PolicyAction>(onNotRestored, "onNotRestored", path) };
        }

        return settings;
    }

    /// <summary>Combines optional day and hour counts into a single cooldown window.</summary>
    public static TimeSpan ToWindow(int? days, int? hours) =>
        TimeSpan.FromHours(((long)(days ?? 0) * 24) + (hours ?? 0));

    /// <summary>Parses a case-insensitive enum value, with a helpful error listing the valid names.</summary>
    public static TEnum ParseEnum<TEnum>(string value, string settingName, string? origin = null)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        var valid = string.Join(", ", Enum.GetNames<TEnum>().Select(n => n.ToLowerInvariant()));
        var where = origin is null ? "" : $" (in '{origin}')";
        throw new CooldownConfigException(
            $"'{value}' is not a valid value for {settingName}{where}; expected one of: {valid}.");
    }
}
