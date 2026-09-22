using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// Whether a site running this package is actually installable, and whether the readiness check
/// agrees with a real browser about it.
/// </summary>
/// <remarks>
/// The package's headline claim is that it makes a site installable, and until this file the only
/// thing asserting it was <c>source.ShouldContain("beforeinstallprompt")</c> against the generated
/// script. That is the same shape of assertion that let three service worker defects reach 0.2.0:
/// the strings were all present while the behaviour was wrong.
///
/// The manifest is fetched and parsed by the browser rather than read off disk, so a manifest the
/// server will not serve, will not serve as JSON, or serves with the wrong content type fails here
/// rather than passing a string check.
///
/// <para>
/// <c>beforeinstallprompt</c> does not fire in headless Chromium, so this does not assert that the
/// event arrives. What it does assert is every input the browser needs before it would: a manifest
/// it can parse, the fields it requires, icons that actually resolve, a worker whose scope covers
/// <c>start_url</c>, and a page that links the manifest at all.
/// </para>
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class InstallabilityTests
{
    private readonly LiveSiteFixture _site;

    public InstallabilityTests(LiveSiteFixture site) => _site = site;

    /// <summary>
    /// Fetched through the browser, from a page on the site, so this goes through the same origin,
    /// content type and parsing a browser applies when it follows the manifest link.
    /// </summary>
    /// <remarks>
    /// The URL comes from the page's own <c>link[rel~="manifest"]</c> rather than being hardcoded,
    /// because that is the one a browser would use. Asserting against the manifest at a path the
    /// package happens to serve would still pass if the page pointed somewhere else entirely.
    /// <c>response.url</c> comes back so the icon and <c>start_url</c> checks resolve relative
    /// references against the manifest, which is what the spec says they are relative to.
    /// </remarks>
    private async Task<JsonElement> ManifestAsync()
    {
        var page = await _site.NewPageAsync();
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        var json = await page.EvaluateAsync<string>(
            """
            async () => {
              // rel is a token list: rel="manifest alternate" is still a manifest link.
              const link = document.querySelector('link[rel~="manifest"]');
              if (!link) throw new Error('no link[rel~="manifest"] on the page');

              const response = await fetch(link.href);
              if (!response.ok) throw new Error('manifest responded ' + response.status);

              return JSON.stringify({
                url: response.url,
                contentType: response.headers.get('content-type'),
                body: await response.json(),
              });
            }
            """);

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task The_manifest_is_served_as_json_the_browser_can_parse()
    {
        var result = await ManifestAsync();

        // response.json() already threw if it were not parseable, so reaching here is the parse.
        result.GetProperty("contentType").GetString()
            .ShouldContain("application/manifest+json");
    }

    [Fact]
    public async Task The_manifest_carries_what_a_browser_requires_to_offer_an_install()
    {
        var manifest = (await ManifestAsync()).GetProperty("body");

        // Chromium's installability criteria, the subset that lives in the manifest:
        // a name, a start_url, a display mode that is not "browser", and a 192 and a 512 icon.
        manifest.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        manifest.GetProperty("start_url").GetString().ShouldNotBeNullOrWhiteSpace();
        manifest.GetProperty("display").GetString().ShouldNotBe("browser");

        var sizes = DeclaredSizes(manifest);

        sizes.ShouldContain("192x192");
        sizes.ShouldContain("512x512");
    }

    /// <summary>
    /// Every size token every icon declares.
    /// </summary>
    /// <remarks>
    /// <c>sizes</c> is a space separated set, so one icon can answer for both required sizes with
    /// <c>"192x192 512x512"</c>. Comparing the whole attribute would call that manifest incomplete
    /// while a browser, which parses it as a token list, installs the site happily.
    /// </remarks>
    private static string[] DeclaredSizes(JsonElement manifest) =>
        manifest.GetProperty("icons").EnumerateArray()
            .Select(icon => icon.GetProperty("sizes").GetString())
            .Where(sizes => !string.IsNullOrWhiteSpace(sizes))
            .SelectMany(sizes => sizes!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

    [Fact]
    public async Task Every_icon_the_manifest_declares_is_actually_fetchable()
    {
        // A manifest naming an icon that 404s is worse than one naming none: the readiness panel
        // reports an icon present and the browser refuses the install without saying why.
        var result = await ManifestAsync();
        var manifestUrl = result.GetProperty("url").GetString()!;
        var manifest = result.GetProperty("body");

        var page = await _site.NewPageAsync();
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            var src = icon.GetProperty("src").GetString();
            src.ShouldNotBeNullOrWhiteSpace();

            // Resolved against the manifest rather than the page: icons[].src is relative to the
            // manifest's own URL, and the two are only interchangeable while it sits at the root.
            var status = await page.EvaluateAsync<int>(
                "async ([src, base]) => (await fetch(new URL(src, base))).status",
                new[] { src, manifestUrl });

            status.ShouldBe(200, $"the manifest declares {src}");
        }
    }

    [Fact]
    public async Task The_service_worker_controls_the_start_url()
    {
        // The other half of installability, and the half the manifest cannot express: a worker
        // whose scope does not cover start_url leaves the site uninstallable however complete the
        // manifest is.
        var result = await ManifestAsync();
        var manifestUrl = result.GetProperty("url").GetString()!;
        var startUrl = result.GetProperty("body").GetProperty("start_url").GetString()!;

        var page = await _site.NewPageAsync();
        await page.GotoAsync(LiveSiteFixture.EntryPage);
        await page.EvaluateAsync("() => navigator.serviceWorker.ready");

        var covered = await page.EvaluateAsync<bool>(
            """
            async ([startUrl, base]) => {
              // start_url is relative to the manifest, not to the page that linked it.
              const resolved = new URL(startUrl, base).href;
              const registration = await navigator.serviceWorker.getRegistration(resolved);
              if (!registration) return false;
              return resolved.startsWith(new URL(registration.scope).href);
            }
            """, new[] { startUrl, manifestUrl });

        covered.ShouldBeTrue($"no worker scope covers {startUrl}");
    }

    [Fact]
    public async Task A_page_on_the_site_links_the_manifest_so_a_browser_would_find_it()
    {
        // The manifest being correct is worth nothing if no page points at it. This is what the
        // readiness panel tells an integrator to add by hand, and nothing checked the client
        // script does it.
        var page = await _site.NewPageAsync();
        await page.GotoAsync(LiveSiteFixture.EntryPage);
        await page.EvaluateAsync("() => navigator.serviceWorker.ready");

        var href = await page.EvaluateAsync<string?>(
            "() => document.querySelector('link[rel=\"manifest\"]')?.getAttribute('href')");

        href.ShouldNotBeNullOrWhiteSpace();

        var status = await page.EvaluateAsync<int>(
            "async href => (await fetch(href)).status", href);

        status.ShouldBe(200);
    }
}
