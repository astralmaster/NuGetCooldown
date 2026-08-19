using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using NuGetCooldown.Model;

namespace NuGetCooldown.Feeds;

/// <summary>
/// Resolves package publish dates from NuGet V3 feeds using the registration API:
/// one small request per package version (<c>{registrationBase}/{id}/{version}.json</c>).
/// For unlisted versions — whose registration <c>published</c> field is a 1900-01-01 sentinel —
/// the true upload time is fetched from the catalog entry instead.
/// </summary>
public sealed class NuGetV3Client(HttpClient http, IReadOnlyList<string> serviceIndexUrls)
    : IPackagePublishInfoProvider
{
    private const int MaxAttempts = 3;

    /// <summary>Registration resource types, most capable first (semver2 + gzip preferred).</summary>
    private static readonly string[] RegistrationResourceTypes =
    [
        "RegistrationsBaseUrl/3.6.0",
        "RegistrationsBaseUrl/Versioned",
        "RegistrationsBaseUrl/3.4.0",
        "RegistrationsBaseUrl/3.0.0-rc",
        "RegistrationsBaseUrl/3.0.0-beta",
        "RegistrationsBaseUrl",
    ];

    /// <summary>Publish dates before this are the "unlisted" sentinel (1900-01-01), not real dates.</summary>
    private static readonly DateTimeOffset MinimumPlausibleDate = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _registrationBases = new();

    /// <inheritdoc />
    public async Task<PublishLookupResult> GetPublishInfoAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var source in serviceIndexUrls)
        {
            string registrationBase;
            try
            {
                registrationBase = await GetRegistrationBaseAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{source}: {ex.Message}");
                continue;
            }

            var leafUrl = $"{registrationBase}{package.LowerId}/{package.LowerVersion}.json";
            try
            {
                var info = await QueryLeafAsync(leafUrl, source, cancellationToken).ConfigureAwait(false);
                if (info is not null)
                {
                    return PublishLookupResult.Found(info);
                }

                // 404: unknown to this source; try the next one.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{source}: {ex.Message}");
            }
        }

        return errors.Count > 0
            ? PublishLookupResult.Error(string.Join("; ", errors))
            : PublishLookupResult.NotFound();
    }

    /// <summary>Returns the leaf's publish info, or <see langword="null"/> when the source returns 404.</summary>
    private async Task<PackagePublishInfo?> QueryLeafAsync(
        string leafUrl,
        string source,
        CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync(leafUrl, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var doc = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var listed = !root.TryGetProperty("listed", out var listedElement)
                     || listedElement.ValueKind != JsonValueKind.False;

        // Unlisted versions report the 1900-01-01 sentinel instead of their real date.
        var published = TryGetDate(root, "published");
        if (published < MinimumPlausibleDate)
        {
            published = null;
        }

        // No usable date (typically unlisted): recover the true upload time from the catalog entry.
        var fromCatalog = false;
        if (published is null
            && root.TryGetProperty("catalogEntry", out var catalogEntry)
            && catalogEntry.ValueKind == JsonValueKind.String)
        {
            published = await TryGetCatalogCreatedAsync(catalogEntry.GetString()!, cancellationToken)
                .ConfigureAwait(false);
            fromCatalog = published is not null;
        }

        return new PackagePublishInfo(published, listed, source, fromCatalog, FromCache: false);
    }

    private async Task<DateTimeOffset?> TryGetCatalogCreatedAsync(
        string catalogUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await GetWithRetryAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return TryGetDate(doc.RootElement, "created") ?? TryGetDate(doc.RootElement, "published");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Best-effort: without the catalog the version is still reported, just with an unknown date.
            return null;
        }
    }

    private async Task<string> GetRegistrationBaseAsync(string serviceIndexUrl, CancellationToken cancellationToken)
    {
        // Lazy so concurrent lookups share a single service-index request per source.
        var lazy = _registrationBases.GetOrAdd(
            serviceIndexUrl,
            url => new Lazy<Task<string>>(() => ResolveRegistrationBaseAsync(url, cancellationToken)));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Do not cache failures; a later call may succeed.
            _registrationBases.TryRemove(serviceIndexUrl, out _);
            throw;
        }
    }

    private async Task<string> ResolveRegistrationBaseAsync(
        string serviceIndexUrl,
        CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync(serviceIndexUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("resources", out var resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"'{serviceIndexUrl}' is not a NuGet V3 service index.");
        }

        foreach (var wantedType in RegistrationResourceTypes)
        {
            foreach (var resource in resources.EnumerateArray())
            {
                if (ResourceHasType(resource, wantedType)
                    && resource.TryGetProperty("@id", out var id)
                    && id.ValueKind == JsonValueKind.String)
                {
                    var baseUrl = id.GetString()!;
                    return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
                }
            }
        }

        throw new InvalidOperationException(
            $"'{serviceIndexUrl}' exposes no RegistrationsBaseUrl resource.");
    }

    private static bool ResourceHasType(JsonElement resource, string wantedType)
    {
        if (!resource.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), wantedType, StringComparison.Ordinal),
            JsonValueKind.Array => type.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String
                && string.Equals(t.GetString(), wantedType, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (attempt < MaxAttempts && IsTransient(response.StatusCode))
                {
                    response.Dispose();
                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                // HttpClient timeout, not user cancellation.
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        (int)statusCode >= 500
        || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private static Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds((500 << (attempt - 1)) + Random.Shared.Next(0, 250));
        return Task.Delay(delay, cancellationToken);
    }

    private static async Task<JsonDocument> ParseJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset? TryGetDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.TryGetDateTimeOffset(out var value))
        {
            return value;
        }

        return null;
    }
}
