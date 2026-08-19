using System.Collections.Concurrent;
using System.Net;
using System.Text;
using NuGetCooldown.Feeds;
using NuGetCooldown.Model;

namespace NuGetCooldown.Tests;

/// <summary>A unique temporary directory, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ncd-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS temp cleaner will get it eventually.
        }
    }
}

/// <summary>A clock frozen at a fixed instant.</summary>
internal sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Publish-info provider backed by a dictionary; records which packages were looked up.</summary>
internal sealed class FakeProvider : IPackagePublishInfoProvider
{
    private readonly Dictionary<PackageIdentity, PublishLookupResult> _results = [];

    public ConcurrentDictionary<PackageIdentity, int> Lookups { get; } = new();

    public FakeProvider Add(PackageIdentity package, PublishLookupResult result)
    {
        _results[package] = result;
        return this;
    }

    public FakeProvider AddPublished(PackageIdentity package, DateTimeOffset published, bool listed = true)
        => Add(package, PublishLookupResult.Found(
            new PackagePublishInfo(published, listed, "https://fake.test/index.json", FromCatalog: !listed, FromCache: false)));

    public Task<PublishLookupResult> GetPublishInfoAsync(PackageIdentity package, CancellationToken cancellationToken)
    {
        Lookups.AddOrUpdate(package, 1, (_, count) => count + 1);
        return Task.FromResult(_results.TryGetValue(package, out var result)
            ? result
            : PublishLookupResult.NotFound());
    }
}

/// <summary>HTTP handler with per-URL responders; counts requests per URL.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<int, HttpResponseMessage>> _routes = [];

    public ConcurrentDictionary<string, int> RequestCounts { get; } = new();

    public FakeHttpHandler Map(string url, Func<HttpResponseMessage> responder)
        => Map(url, _ => responder());

    /// <summary>The responder receives the 1-based attempt number for that URL.</summary>
    public FakeHttpHandler Map(string url, Func<int, HttpResponseMessage> responder)
    {
        _routes[url] = responder;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var attempt = RequestCounts.AddOrUpdate(url, 1, (_, count) => count + 1);
        return Task.FromResult(_routes.TryGetValue(url, out var responder)
            ? responder(attempt)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);

    public HttpClient CreateClient() => new(this, disposeHandler: false);
}

/// <summary>Reusable fixture content.</summary>
internal static class TestData
{
    /// <summary>
    /// Realistic assets file: two direct packages, one transitive, one project reference
    /// (which must be ignored).
    /// </summary>
    public const string AssetsJson = """
        {
          "version": 3,
          "targets": {
            "net8.0": {}
          },
          "libraries": {
            "Newtonsoft.Json/13.0.3": {
              "sha512": "abc",
              "type": "package",
              "path": "newtonsoft.json/13.0.3"
            },
            "Serilog/4.0.0": {
              "sha512": "def",
              "type": "package",
              "path": "serilog/4.0.0"
            },
            "Serilog.Sinks.Console/6.0.0": {
              "sha512": "ghi",
              "type": "package",
              "path": "serilog.sinks.console/6.0.0"
            },
            "MyLib/1.0.0": {
              "type": "project",
              "path": "../MyLib/MyLib.csproj"
            }
          },
          "projectFileDependencyGroups": {
            "net8.0": [
              "Newtonsoft.Json >= 13.0.3",
              "Serilog.Sinks.Console >= 6.0.0"
            ]
          },
          "project": {
            "version": "1.0.0",
            "restore": {
              "projectName": "App"
            }
          }
        }
        """;
}
