using BaryoDev.Umbraco.Pwa.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Pwa.Services;

public interface IPwaReadinessService
{
    Task<PwaReadiness> CheckAsync(HttpRequest request, CancellationToken ct = default);
}

/// <summary>
/// Answers "why is my site not offering to install?" before someone has to ask it.
/// </summary>
/// <remarks>
/// This exists because of a real failure seen during testing: the manifest pointed at icons that
/// returned 404, so Chrome quietly declined to offer installation. Nothing errored, nothing
/// logged, and the only symptom was a banner that never appeared. Every check here is a condition
/// a browser enforces silently.
/// </remarks>
internal class PwaReadinessService : IPwaReadinessService
{
    private readonly IOptionsMonitor<PwaOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;

    public PwaReadinessService(
        IOptionsMonitor<PwaOptions> options,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
    }

    public async Task<PwaReadiness> CheckAsync(HttpRequest request, CancellationToken ct = default)
    {
        var o = _options.CurrentValue;
        var m = o.Manifest;
        var checks = new List<PwaCheck>();

        checks.Add(new PwaCheck
        {
            Name = "Served over HTTPS",
            Passed = request.IsHttps || request.Host.Host is "localhost" or "127.0.0.1",
            Detail = request.IsHttps
                ? "Secure origin."
                : request.Host.Host is "localhost" or "127.0.0.1"
                    ? "localhost is treated as a secure origin, so this is fine in development."
                    : "Browsers refuse to register a service worker on an insecure origin. "
                      + "localhost is exempt; a public site is not.",
        });

        checks.Add(new PwaCheck
        {
            Name = "Manifest has a name",
            Passed = !string.IsNullOrWhiteSpace(m.Name),
            Detail = string.IsNullOrWhiteSpace(m.Name)
                ? "Set BaryoDev:Pwa:Manifest:Name. Without it the install prompt has nothing to call your app."
                : m.Name!,
        });

        checks.Add(new PwaCheck
        {
            Name = "Display mode is app-like",
            Passed = m.Display is "standalone" or "fullscreen" or "minimal-ui",
            Detail = m.Display == "browser"
                ? "A display of \"browser\" tells the browser not to treat this as an app, so it will not offer to install it."
                : m.Display,
        });

        // The two icon sizes Chrome requires. This is the check that would have caught the demo.
        foreach (var required in new[] { "192x192", "512x512" })
        {
            var icon = m.Icons.FirstOrDefault(i => i.Sizes == required);

            if (icon is null)
            {
                checks.Add(new PwaCheck
                {
                    Name = $"Icon {required}",
                    Passed = false,
                    Detail = $"Not configured. Chrome will not offer to install a site without a {required} icon.",
                });
                continue;
            }

            var (reachable, detail) = await Reachable(request, icon.Src, ct);
            checks.Add(new PwaCheck
            {
                Name = $"Icon {required}",
                Passed = reachable,
                Detail = reachable ? icon.Src : $"{icon.Src} is not reachable: {detail}",
            });
        }

        checks.Add(new PwaCheck
        {
            Name = "Maskable icon",
            Passed = m.Icons.Any(i => i.Purpose?.Contains("maskable") == true),
            Detail = m.Icons.Any(i => i.Purpose?.Contains("maskable") == true)
                ? "Present."
                : "Optional. Without one, Android crops your icon into a white circle.",
            Advisory = true,
        });

        return new PwaReadiness
        {
            Installable = checks.Where(c => !c.Advisory).All(c => c.Passed),
            Checks = checks,
        };
    }

    /// <summary>
    /// Checks an icon actually exists.
    /// </summary>
    /// <remarks>
    /// A relative path is resolved against the web root rather than fetched over HTTP. A server
    /// issuing a request to itself from inside a request handler is fragile: it consumes a second
    /// connection while holding the first, it fails behind proxies that do not route the public
    /// host back internally, and on Kestrel it threw NotSupportedException outright, which is how
    /// this was found. Umbraco stores media under the web root by default, so the file check
    /// covers both static assets and uploaded media.
    ///
    /// Absolute URLs are a different case: those are genuinely elsewhere, so they still get a
    /// probe, and that is not a self-request.
    /// </remarks>
    private async Task<(bool Ok, string Detail)> Reachable(HttpRequest request, string src, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(src)) return (false, "no source set");

        // A leading slash is a site-relative path, but on Unix Uri.TryCreate happily parses
        // "/icon.png" as an absolute file:// URI, so testing IsAbsoluteUri alone sends the common
        // case down the HTTP branch and it fails with "the 'file' scheme is not supported".
        // Platform-dependent, and only on Linux and macOS: exactly where this ships.
        Uri? remote = null;
        if (!src.StartsWith('/')
            && Uri.TryCreate(src, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            remote = parsed;
        }

        if (remote is null)
        {
            var relative = src.StartsWith('/') ? src[1..] : src;
            var file = _environment.WebRootFileProvider.GetFileInfo(relative);

            return file.Exists
                ? (true, $"{file.Length / 1024}KB on disk")
                : (false, "no such file under wwwroot");
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            using var response = await client.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return (false, $"HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            return mediaType.StartsWith("image/")
                ? (true, mediaType)
                : (false, $"served as {mediaType}, not an image");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
