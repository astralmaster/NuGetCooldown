using System.Text.Json;
using System.Text.Json.Serialization;
using NuGetCooldown.Configuration;
using NuGetCooldown.Feeds;
using NuGetCooldown.Reporting;

namespace NuGetCooldown.Json;

/// <summary>Source-generated JSON serialization for every DTO the tool reads or writes.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigFileDto))]
[JsonSerializable(typeof(CacheEntry))]
[JsonSerializable(typeof(JsonReportDto))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
