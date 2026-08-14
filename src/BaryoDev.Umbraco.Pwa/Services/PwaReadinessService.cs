using BaryoDev.Umbraco.Pwa.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

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
    private readonly IDocumentUrlService _documentUrls;

    public PwaReadinessService(
        IOptionsMonitor<PwaOptions> options,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        IDocumentUrlService documentUrls)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _documentUrls = documentUrls;
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

        var (startOk, startDetail) = StartUrlHasContent(m.StartUrl);
        checks.Add(new PwaCheck
        {
            Name = "Start URL has content",
            Passed = startOk,
            Detail = startDetail,
        });

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

    /// <summary>
    /// Checks the installed app will open on something.
    /// </summary>
    /// <remarks>
    /// Found on a real iPhone. Add to Home Screen succeeded, every other check was green, and the
    /// app opened on Umbraco's "your website doesn't contain any published content yet" page,
    /// because StartUrl defaults to "/" and nothing was published there.
    ///
    /// This is a worse failure than the missing icons that created this service. A missing icon is
    /// loud, since Chrome refuses to install. A bad start URL is silent: installation succeeds and
    /// the fault only shows the first time somebody taps the icon.
    ///
    /// No HTTP self-request here, for the reasons in Reachable below. A static file is checked on
    /// disk, and anything else is asked of Umbraco directly, which is a question only a CMS-side
    /// package can answer.
    /// </remarks>
    private (bool Ok, string Detail) StartUrlHasContent(string startUrl)
    {
        var path = string.IsNullOrWhiteSpace(startUrl) ? "/" : startUrl.Trim();

        // Query strings and fragments are not part of the route.
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        if (path.Length == 0) path = "/";

        // A static file under wwwroot is a legitimate start URL and needs no CMS lookup.
        if (path != "/")
        {
            var file = _environment.WebRootFileProvider.GetFileInfo(path.TrimStart('/'));
            if (file.Exists) return (true, $"{path} is served from wwwroot.");
        }

        try
        {
            // IDocumentUrlService is the one route API present unchanged across Umbraco 16, 17
            // and 18. IPublishedContentCache.GetByRoute and GetAtRoot were obsoleted in 16 and
            // removed in 17, so using those would have needed conditional compilation.
            if (!_documentUrls.HasAny())
            {
                return (false, path == "/"
                    ? "Start URL is the site root, but nothing is published, so the installed app "
                      + "opens on Umbraco's default page. Publish a home page, or point "
                      + "BaryoDev:Pwa:Manifest:StartUrl at a page that exists."
                    : $"{path} cannot resolve because nothing is published yet.");
            }

            var key = _documentUrls.GetDocumentKeyByRoute(path, null, null, false);

            if (key is not null) return (true, $"{path} resolves to published content.");

            return (false, path == "/"
                ? "Start URL is the site root, but no published page answers it, so the installed "
                  + "app opens on Umbraco's default page. Publish a home page, or point "
                  + "BaryoDev:Pwa:Manifest:StartUrl at a page that exists."
                : $"{path} is not a file under wwwroot and does not resolve to published content, "
                  + "so the installed app opens on a 404.");
        }
        catch (Exception ex)
        {
            // Never fail the whole preflight because one lookup threw.
            return (true, $"Could not be checked: {ex.GetType().Name}.");
        }
    }

}
