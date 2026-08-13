using BaryoDev.Umbraco.Pwa.Models;
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

    public PwaReadinessService(IOptionsMonitor<PwaOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
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

    private async Task<(bool Ok, string Detail)> Reachable(HttpRequest request, string src, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(src)) return (false, "no source set");

        try
        {
            var url = Uri.TryCreate(src, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri($"{request.Scheme}://{request.Host}{(src.StartsWith('/') ? src : "/" + src)}");

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return (false, $"HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            return mediaType.StartsWith("image/")
                ? (true, mediaType)
                : (false, $"served as {mediaType}, not an image");
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name);
        }
    }
}
