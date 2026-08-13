using System.Net;
using System.Text.Json;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The three files a site owner never writes. If any of these is wrong the site silently stops
/// being installable, with no error anywhere to notice.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class GeneratedAssetTests
{
    private readonly UmbracoSiteFixture _site;

    public GeneratedAssetTests(UmbracoSiteFixture site) => _site = site;

    [Theory]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/sw.js", "text/javascript")]
    [InlineData("/baryodev-pwa.js", "text/javascript")]
    public async Task Each_asset_is_served_with_the_right_content_type(string path, string contentType)
    {
        var response = await _site.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(contentType);
    }

    [Fact]
    public async Task The_service_worker_is_served_from_the_root()
    {
        // Not a preference. A service worker only controls pages at or below its own URL, so one
        // served from /App_Plugins/... would control nothing at all.
        var atRoot = await _site.Client.GetAsync("/sw.js");
        var underPlugins = await _site.Client.GetAsync("/App_Plugins/BaryoDev.Pwa/sw.js");

        atRoot.StatusCode.ShouldBe(HttpStatusCode.OK);
        underPlugins.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_service_worker_is_never_cached()
    {
        // A cached worker is how a site gets stuck on a broken build: the old worker keeps
        // serving the old shell, and no deploy can dislodge it.
        var response = await _site.Client.GetAsync("/sw.js");

        var cacheControl = response.Headers.CacheControl;
        cacheControl.ShouldNotBeNull();
        cacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_service_worker_never_caches_the_backoffice()
    {
        // A cached backoffice is a stale editing experience, and on a shared machine it is a way
        // to serve one editor's data to another.
        var source = await _site.Client.GetStringAsync("/sw.js");

        source.ShouldContain("/umbraco/");
        source.ShouldContain("SKIP.some");
    }

    [Fact]
    public async Task The_service_worker_reflects_configuration()
    {
        var source = await _site.Client.GetStringAsync("/sw.js");

        source.ShouldContain("\"fixture-shell-test1\"");
        source.ShouldContain("\"fixture-api-test1\"");
    }

    [Fact]
    public async Task The_manifest_is_valid_json_carrying_the_configured_identity()
    {
        var json = await _site.Client.GetStringAsync("/manifest.webmanifest");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("name").GetString().ShouldBe("Fixture Site");
        root.GetProperty("short_name").GetString().ShouldBe("Fixture");
        root.GetProperty("display").GetString().ShouldBe("standalone");
        root.GetProperty("start_url").GetString().ShouldBe("/");
    }

    [Fact]
    public async Task The_client_posts_to_the_route_the_package_actually_serves()
    {
        // This pairing is the one that breaks silently on a rename: the client keeps posting and
        // the endpoint keeps 404ing, and install tracking just quietly stops.
        var client = await _site.Client.GetStringAsync("/baryodev-pwa.js");

        client.ShouldContain("/umbraco/pwa/api/report");

        using var probe = await _site.ReportAsync(
            new { deviceId = $"route-{Guid.NewGuid():N}", displayMode = "standalone", platform = "ios", installed = true });

        probe.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task The_client_registers_the_worker_at_the_path_the_package_serves()
    {
        var client = await _site.Client.GetStringAsync("/baryodev-pwa.js");

        client.ShouldContain("navigator.serviceWorker.register(\"/sw.js\")");
    }

    [Fact]
    public async Task Every_asset_is_reachable_without_signing_in()
    {
        // A visitor is not a backoffice user. If these ever land behind auth the site stops being
        // installable for everyone except editors.
        foreach (var path in new[] { "/manifest.webmanifest", "/sw.js", "/baryodev-pwa.js" })
        {
            var response = await _site.Client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path} must be anonymous");
        }
    }
}

/// <summary>
/// The install prompt. Without it most visitors never learn the site is installable, because
/// Chrome hides its own prompt behind a menu and iOS has none at all.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class InstallPromptTests
{
    private readonly UmbracoSiteFixture _site;

    public InstallPromptTests(UmbracoSiteFixture site) => _site = site;

    private Task<string> Client() => _site.Client.GetStringAsync("/baryodev-pwa.js");

    [Fact]
    public async Task The_prompt_waits_for_the_browser_to_confirm_the_site_qualifies()
    {
        // An Install button that cannot install anything is worse than no button, so on Android
        // the banner is only built inside the beforeinstallprompt handler.
        var source = await Client();

        source.ShouldContain("beforeinstallprompt");
        source.ShouldContain("e.preventDefault()");
    }

    [Fact]
    public async Task IOS_gets_instructions_because_it_has_no_install_api()
    {
        var source = await Client();

        source.ShouldContain("Add to Home Screen");

        // iPadOS reports itself as a Mac, so touch points are the only way to tell them apart.
        source.ShouldContain("MacIntel");
        source.ShouldContain("maxTouchPoints");
    }

    [Fact]
    public async Task The_prompt_is_suppressed_in_the_backoffice()
    {
        // An editor does not need to be asked to install the site they are editing.
        var source = await Client();

        source.ShouldContain("HIDE_ON");
        source.ShouldContain("\"/umbraco\"");
    }

    [Fact]
    public async Task Dismissal_is_remembered()
    {
        var source = await Client();

        source.ShouldContain("bd_pwa_dismissed");
        source.ShouldContain("localStorage.setItem(DISMISS_KEY");
    }

    [Fact]
    public async Task The_prompt_never_shows_to_someone_who_already_installed()
    {
        var source = await Client();

        source.ShouldContain("if (displayMode() !== \"browser\") return;");
    }

    [Fact]
    public async Task Configured_text_is_escaped_before_it_reaches_innerHTML()
    {
        // These values come from appsettings rather than a visitor, so this is defence in depth
        // rather than a live hole. It costs nothing and removes the question entirely.
        var source = await Client();

        source.ShouldContain("escapeHtml(APP_NAME)");
        source.ShouldContain("escapeHtml(PROMPT_TEXT)");
    }

    [Fact]
    public async Task The_banner_is_announced_to_assistive_technology()
    {
        var source = await Client();

        source.ShouldContain("setAttribute(\"role\", \"dialog\")");
        source.ShouldContain("aria-label");
    }
}
