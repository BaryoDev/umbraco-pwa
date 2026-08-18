using System.Net;
using System.Text.Json;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.Options;
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

    /// <summary>
    /// A generator built straight from options, for the cases the hosted site cannot show. The
    /// test host runs at a domain root, so anything about a path base is invisible through it.
    /// </summary>
    private static PwaAssetGenerator Generator(Action<PwaOptions>? configure = null)
    {
        var options = new PwaOptions();
        configure?.Invoke(options);
        return new PwaAssetGenerator(new StaticOptionsMonitor(options));
    }

    private sealed class StaticOptionsMonitor(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue { get; } = value;

        public PwaOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<PwaOptions, string?> listener) => null;
    }

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

        // Written against BASE rather than a literal path. The test host runs at a domain root, so
        // BASE is empty here and a literal "/sw.js" would pass either way; asserting the
        // expression is what makes this fail if the path base is ever dropped again.
        client.ShouldContain("register(BASE + \"/sw.js\"");
        client.ShouldContain("var BASE =");
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("/umbraco-pwa", "\"/umbraco-pwa\"")]
    [InlineData("/deep/prefix/", "\"/deep/prefix\"")]
    public void The_client_carries_the_path_base_it_was_built_for(string pathBase, string expected)
    {
        // The report endpoint and the service worker are both written from BASE, so a site mounted
        // under a prefix has to receive its prefix here. It used to receive nothing: both URLs were
        // absolute from the domain root, so install reports went somewhere that does not exist and
        // the worker registration failed outright, taking offline support with it.
        //
        // The trailing slash case matters on its own: "/deep/prefix/" + "/sw.js" would produce a
        // double slash, which a browser reads as a protocol-relative URL to a host called "sw.js".
        var script = Generator().Client(pathBase);

        script.ShouldContain($"var BASE = {expected};");

        // Declared is not the same as used, and asserting only the declaration is how the first
        // version of this test passed against the very bug it describes: BASE was emitted
        // correctly while both URLs still ignored it. These pin the two places that have to read
        // it, and no absolute form of either may survive anywhere in the script.
        script.ShouldContain("fetch(BASE + \"/umbraco/pwa/api/report\"");
        script.ShouldContain("register(BASE + \"/sw.js\"");
        script.ShouldNotContain("fetch(\"/umbraco/pwa/api/report\"");
        script.ShouldNotContain("register(\"/sw.js\"");
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData("minimal-ui")]
    public void A_site_that_does_not_ask_for_fullscreen_does_not_count_fullscreen_as_installed(string display)
    {
        // (display-mode: fullscreen) matches a browser someone pressed F11 in, not only an
        // installed app, so it is an install signal only when this site's manifest asked for it.
        // Without this every visitor who went fullscreen was recorded as an install, and the
        // dashboard's adoption number was inflated by a keystroke.
        var script = Generator(o => o.Manifest.Display = display).Client();

        script.ShouldContain($"var MANIFEST_DISPLAY = \"{display}\";");
        script.ShouldContain("if (mode === \"fullscreen\") return MANIFEST_DISPLAY === \"fullscreen\";");
    }

    [Fact]
    public void Every_cache_write_in_the_service_worker_is_guarded()
    {
        // The navigation branch used to write unconditionally while its two neighbours checked
        // the response first, so a 404 or a maintenance page served during a deploy became that
        // URL's offline experience until the cache version changed.
        //
        // Asserted structurally rather than by looking for the one line that was wrong, so a
        // fourth branch added later is covered without anyone remembering to come back here.
        var worker = Generator().ServiceWorker();
        var lines = worker.Split('\n');

        var writes = lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.Contains("c.put(req, copy)"))
            .ToList();

        // Pinned, so the test cannot pass by finding nothing to check.
        writes.Count.ShouldBe(3);

        var unguarded = writes
            .Where(x => !string.Join("\n", lines.Skip(Math.Max(0, x.index - 3)).Take(3))
                .Contains("storable(resp)"))
            .Select(x => x.index + 1)
            .ToList();

        unguarded.ShouldBeEmpty();

        // The guard lives in one function now. An inline copy is how the branches drifted apart.
        worker.ShouldNotContain("resp.ok && resp.type");
    }

    [Fact]
    public void The_service_worker_precaches_the_navigation_fallback()
    {
        // Without this the fallback is only cached if the visitor happened to navigate to it
        // while online, so the offline page was missing for exactly the person who needed it.
        var worker = Generator(o => o.ServiceWorker.NavigationFallback = "/offline").ServiceWorker();
        var install = InstallHandler(worker);

        worker.ShouldContain("const NAV_FALLBACK = \"/offline\"");
        install.ShouldContain("waitUntil");
        install.ShouldContain("addAll([NAV_FALLBACK])");
    }

    [Fact]
    public void A_fallback_that_will_not_fetch_still_leaves_a_working_service_worker()
    {
        // addAll rejects on a non-2xx, which is right for the cache and wrong for the install:
        // an unreachable fallback must not cost the site its service worker entirely.
        var install = InstallHandler(Generator().ServiceWorker());

        install.ShouldContain(".catch(");
        install.ShouldContain("skipWaiting");
    }

    [Fact]
    public void The_service_worker_honours_what_the_server_said_about_storing_a_response()
    {
        // Cache Storage enforces no HTTP semantics of its own, so a response the server marked
        // no-store or private is stored anyway unless the worker checks.
        var worker = Generator().ServiceWorker();

        worker.ShouldContain("Cache-Control");
        worker.ShouldContain("no-store");
        worker.ShouldContain("private");

        // A redirected response cached under the original URL hands back the target's body for
        // the source address, and a navigation refuses it outright.
        worker.ShouldContain("resp.redirected");
    }

    /// <summary>The install handler, isolated so a match elsewhere in the worker cannot pass.</summary>
    private static string InstallHandler(string worker)
    {
        var start = worker.IndexOf("addEventListener(\"install\"", StringComparison.Ordinal);
        var end = worker.IndexOf("function storable", StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);

        return worker[start..end];
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
