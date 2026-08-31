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
/// Performs PWA readiness checks that can be evaluated safely at application startup,
/// without requiring an active HTTP request.
/// </summary>
internal interface IPwaStartupReadinessService
{
    /// <summary>
    /// Checks whether the configured PWA is ready for installation using only
    /// request-independent checks.
    /// </summary>
    /// <param name="ct">Token used to cancel the readiness check.</param>
    /// <returns>The aggregated PWA readiness result.</returns>
    Task<PwaReadiness> CheckAsync(CancellationToken ct = default);
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
internal class PwaReadinessService : IPwaReadinessService, IPwaStartupReadinessService
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

    /// <summary>
    /// Checks whether the current request is served from an installable PWA configuration.
    /// </summary>
    /// <param name="request">
    /// The current HTTP request, used to evaluate request-specific requirements such as
    /// secure-origin availability.
    /// </param>
    /// <param name="ct">Token used to cancel the readiness check.</param>
    /// <returns>The aggregated PWA readiness result.</returns>
    public async Task<PwaReadiness> CheckAsync(
        HttpRequest request,
        CancellationToken ct = default)
    {
        List<PwaCheck> checks = [CreateHttpsCheck(request)];

        await AddCommonChecksAsync(checks, ct);

        return CreateResult(checks);
    }

    /// <summary>
    /// Performs request-independent readiness checks during application startup.
    /// </summary>
    /// <remarks>
    /// The secure-origin check is intentionally omitted because no reliable public request
    /// context exists during startup, particularly when TLS is terminated by a reverse proxy.
    /// </remarks>
    async Task<PwaReadiness> IPwaStartupReadinessService.CheckAsync(
        CancellationToken ct)
    {
        var checks = new List<PwaCheck>();

        await AddCommonChecksAsync(checks, ct);

        return CreateResult(checks);
    }

    /// <summary>
    /// Checks whether the current request is served from an origin that browsers
    /// consider secure enough to install a PWA.
    /// </summary>
    /// <param name="request">The current HTTP request.</param>
    /// <returns>The secure-origin readiness check.</returns>
    private static PwaCheck CreateHttpsCheck(HttpRequest request)
    {
        return new PwaCheck
        {
            Name = "Served over HTTPS",
            Passed = request.IsHttps || request.Host.Host is "localhost" or "127.0.0.1",
            Detail = request.IsHttps
                ? "Secure origin."
                : request.Host.Host is "localhost" or "127.0.0.1"
                    ? "localhost is treated as a secure origin, so this is fine in development."
                    : "Browsers refuse to register a service worker on an insecure origin. "
                      + "localhost is exempt; a public site is not.",
        };
    }

    /// <summary>
    /// Adds readiness checks that do not depend on an active HTTP request.
    /// </summary>
    /// <param name="checks">The collection that receives the generated checks.</param>
    /// <param name="ct">Token used to cancel asynchronous checks.</param>
    private async Task AddCommonChecksAsync(
        List<PwaCheck> checks,
        CancellationToken ct)
    {
        var o = _options.CurrentValue;
        var m = o.Manifest;

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

            var (reachable, detail) = await Reachable(icon.Src, ct);

            checks.Add(new PwaCheck
            {
                Name = $"Icon {required}",
                Passed = reachable,
                Detail = reachable
                    ? icon.Src
                    : $"{icon.Src} is not reachable: {detail}",
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
    }

    /// <summary>
    /// Aggregates individual readiness checks into the final PWA readiness result.
    /// </summary>
    /// <param name="checks">The completed readiness checks.</param>
    /// <returns>
    /// A readiness result that is installable when every non-advisory check has passed.
    /// </returns>
    private static PwaReadiness CreateResult(List<PwaCheck> checks)
    {
        return new PwaReadiness
        {
            Installable = checks
                .Where(c => !c.Advisory)
                .All(c => c.Passed),
            Checks = checks,
        };
    }

    /// <summary>
    /// Checks whether a configured PWA icon can be reached.
    /// </summary>
    /// <remarks>
    /// Relative paths are resolved directly against the web root. Absolute HTTP and HTTPS
    /// URLs are probed remotely because they may point to externally hosted assets.
    /// </remarks>
    /// <param name="src">The configured icon source.</param>
    /// <param name="ct">Token used to cancel a remote probe.</param>
    /// <returns>
    /// A tuple indicating whether the icon is reachable and a diagnostic description.
    /// </returns>
    private async Task<(bool Ok, string Detail)> Reachable(string src, CancellationToken ct)
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
            // The named client, which refuses to connect to anything off the public internet.
            // Not disposed: the factory owns its lifetime and disposing it here defeats the
            // handler pooling it exists to provide.
            var client = _httpClientFactory.CreateClient(PwaIconProbe.ClientName);

            using var response = await client.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return (false, $"HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            return mediaType.StartsWith("image/")
                ? (true, mediaType)
                : (false, $"served as {mediaType}, not an image");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is NonPublicAddressException)
        {
            // Worth naming, because it is the one failure the site owner caused and can fix. It
            // discloses nothing: they configured the address.
            return (false, "it is not a public address, so a visitor's browser could not load it either");
        }
        catch (TaskCanceledException)
        {
            return (false, $"it did not answer within {PwaIconProbe.Timeout.TotalSeconds:0} seconds");
        }
        catch (Exception)
        {
            // Deliberately not the exception message. This detail is rendered in the backoffice
            // and written to the application log, and HttpClient messages name hosts, ports and
            // TLS particulars. "Could not be reached" is everything a site owner can act on.
            return (false, "it could not be reached");
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
