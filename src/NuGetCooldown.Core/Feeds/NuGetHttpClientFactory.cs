using System.Net;

namespace NuGetCooldown.Feeds;

/// <summary>Creates the <see cref="HttpClient"/> used for NuGet V3 requests.</summary>
public static class NuGetHttpClientFactory
{
    /// <summary>
    /// Creates a client with gzip decompression (the fast registration endpoints are gzip-encoded)
    /// and an identifying User-Agent.
    /// </summary>
    public static HttpClient Create(string toolVersion, int timeoutSeconds = 30)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"NuGetCooldown/{toolVersion} (+https://github.com/astralmaster/NuGetCooldown)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}
