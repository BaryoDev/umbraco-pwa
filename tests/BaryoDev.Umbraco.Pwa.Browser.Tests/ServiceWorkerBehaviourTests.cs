using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// What the worker does, rather than what it says.
/// </summary>
/// <remarks>
/// Every one of these covers a defect that shipped in 0.2.0 while a text assertion about the same
/// line was green. The generated worker is JavaScript running in a browser, and until this file
/// existed nothing in the suite ever ran a line of it.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class ServiceWorkerBehaviourTests
{
    private const string EntryTitle = "PWA for Umbraco";
    private const string FallbackTitle = "PWA for Umbraco";

    private readonly LiveSiteFixture _site;

    public ServiceWorkerBehaviourTests(LiveSiteFixture site) => _site = site;

    [Fact]
    public async Task The_worker_registers_and_takes_control()
    {
        var page = await Installed();

        var state = await page.EvaluateAsync<string>(
            "async () => (await navigator.serviceWorker.ready).active?.state ?? 'none'");

        state.ShouldBe("activated");
    }

    [Fact]
    public async Task The_offline_fallback_is_cached_without_ever_being_visited()
    {
        // The defect this covers: the install handler cached nothing, so the fallback was only
        // present if the visitor happened to have loaded it while online.
        var page = await Installed();

        var cached = await CacheKeys(page);

        cached.ShouldContain(LiveSiteFixture.Fallback);
        page.Url.ShouldNotContain(LiveSiteFixture.Fallback);
    }

    [Fact]
    public async Task An_offline_navigation_to_a_page_never_visited_serves_the_fallback()
    {
        // The claim the package leads with, asserted end to end rather than inferred from the
        // presence of a catch handler.
        var (page, goOffline) = await InstalledSession();

        // Titles rather than bodies, and the entry page's is asserted first: both are served from
        // wwwroot, so matching only the fallback's could pass without anything having changed.
        (await page.TitleAsync()).ShouldBe(EntryTitle);

        await goOffline();
        await page.GotoAsync("/a-page-nobody-has-ever-opened");

        (await page.TitleAsync()).ShouldBe(FallbackTitle);
    }

    [Fact]
    public async Task An_offline_navigation_fails_honestly_when_nothing_was_precached()
    {
        // The mirror of the test above, and the reason the precache is load-bearing: with the
        // caches emptied and the worker still in control, there is nothing to fall back to. It
        // also keeps the test above honest, by proving offline really is offline.
        var (page, goOffline) = await InstalledSession();
        await page.EvaluateAsync("async () => { for (const k of await caches.keys()) await caches.delete(k); }");

        await goOffline();

        await Should.ThrowAsync<PlaywrightException>(
            () => page.GotoAsync("/another-page-nobody-has-opened"));
    }

    [Fact]
    public async Task An_error_response_is_never_cached()
    {
        // The defect this covers: the navigation branch wrote to the cache unconditionally, so a
        // 404 or a maintenance page became that URL's offline experience.
        var page = await Installed();
        var missing = $"/definitely-missing-{Guid.NewGuid():N}.png";

        var status = await page.EvaluateAsync<int>($"async () => (await fetch('{missing}')).status");
        await Settle(page);

        status.ShouldBe(404);
        (await CacheKeys(page)).ShouldNotContain(missing);
    }

    [Fact]
    public async Task A_successful_asset_is_still_cached()
    {
        // The control. A guard that refused everything would pass the test above and break the
        // package, so the positive case has to be asserted alongside it.
        var page = await Installed();

        await page.EvaluateAsync("async () => { await fetch('/icon-192.png'); }");
        await Settle(page);

        (await CacheKeys(page)).ShouldContain("/icon-192.png");
    }

    [Fact]
    public async Task A_no_store_response_is_never_cached_but_an_ordinary_response_is()
    {
        var page = await Installed();

        var noStoreStatus = await page.EvaluateAsync<int>(
            "async () => (await fetch('/cache-control-no-store')).status");
        var ordinaryStatus = await page.EvaluateAsync<int>(
            "async () => (await fetch('/icon-192.png')).status");
        await Settle(page);

        noStoreStatus.ShouldBe(200);
        ordinaryStatus.ShouldBe(200);
        var keys = await CacheKeys(page);
        keys.ShouldNotContain("/cache-control-no-store");
        keys.ShouldContain("/icon-192.png");
    }

    [Fact]
    public async Task A_private_response_is_never_cached_but_an_ordinary_response_is()
    {
        var page = await Installed();

        var privateStatus = await page.EvaluateAsync<int>(
            "async () => (await fetch('/cache-control-private')).status");
        var ordinaryStatus = await page.EvaluateAsync<int>(
            "async () => (await fetch('/icon-192.png')).status");
        await Settle(page);

        privateStatus.ShouldBe(200);
        ordinaryStatus.ShouldBe(200);
        var keys = await CacheKeys(page);
        keys.ShouldNotContain("/cache-control-private");
        keys.ShouldContain("/icon-192.png");
    }

    [Fact]
    public async Task A_cross_origin_request_is_left_to_the_browser_but_an_ordinary_response_is_cached()
    {
        var page = await Installed();
        var opaqueUrl = "http://opaque.test/opaque-response";

        // The worker deliberately leaves cross-origin requests to the browser before fetching
        // them. This test therefore covers routing, not the worker's non-basic storable() rule.
        // The same-origin control still takes the cache-writing path.
        await page.Context.RouteAsync("http://opaque.test/**", route =>
            route.FulfillAsync(new() { Status = 200, Body = "opaque response" }));

        var responseType = await page.EvaluateAsync<string>(
            $"async () => (await fetch('{opaqueUrl}', {{ mode: 'no-cors' }})).type");
        var ordinaryStatus = await page.EvaluateAsync<int>(
            "async () => (await fetch('/icon-192.png')).status");
        await Settle(page);

        responseType.ShouldBe("opaque");
        ordinaryStatus.ShouldBe(200);
        var keys = await CacheKeys(page);
        keys.ShouldNotContain("/opaque-response");
        keys.ShouldContain("/icon-192.png");
    }

    [Fact]
    public async Task The_backoffice_is_never_cached()
    {
        // A cached backoffice is a stale editing experience, and on a shared machine it is one
        // user's data served to another.
        var page = await Installed();

        await page.EvaluateAsync("async () => { try { await fetch('/umbraco/'); } catch {} }");
        await Settle(page);

        (await CacheKeys(page)).ShouldNotContain(k => k.StartsWith("/umbraco/"));
    }

    [Fact]
    public async Task A_successful_api_response_is_returned_live_and_cached()
    {
        var page = await Installed();

        var body = await page.EvaluateAsync<string>($"async () => await (await fetch('{LiveSiteFixture.ApiRoute}')).text()");
        await Settle(page);

        body.ShouldContain("live-api");
        (await CacheKeys(page)).ShouldContain(LiveSiteFixture.ApiRoute);
    }

    [Fact]
    public async Task An_offline_api_request_serves_its_cached_response()
    {
        var (page, goOffline) = await InstalledSession();
        var online = await page.EvaluateAsync<string>($"async () => await (await fetch('{LiveSiteFixture.ApiRoute}')).text()");
        await Settle(page);

        await goOffline();
        var offline = await page.EvaluateAsync<string>($"async () => await (await fetch('{LiveSiteFixture.ApiRoute}')).text()");

        online.ShouldContain("live-api");
        offline.ShouldBe(online);
    }

    [Fact]
    public async Task An_offline_api_request_without_a_cached_response_returns_the_documented_503()
    {
        var (page, goOffline) = await InstalledSession();
        var missing = $"/api/no-cache-{Guid.NewGuid():N}";

        await goOffline();
        var result = await page.EvaluateAsync<string>($"async () => {{ const r = await fetch('{missing}'); return JSON.stringify({{ status: r.status, body: await r.json() }}); }}");
        var json = System.Text.Json.JsonDocument.Parse(result);

        json.RootElement.GetProperty("status").GetInt32().ShouldBe(503);
        json.RootElement.GetProperty("body").GetProperty("offline").GetBoolean().ShouldBeTrue();
        (await CacheKeys(page)).ShouldContain(LiveSiteFixture.Fallback);
    }

    [Fact]
    public async Task Clearing_api_cache_preserves_the_shell_cache()
    {
        var page = await Installed();
        await page.EvaluateAsync($"async () => {{ await fetch('{LiveSiteFixture.ApiRoute}'); }}");
        await Settle(page);
        (await CacheKeys(page)).ShouldContain(LiveSiteFixture.ApiRoute);
        (await CacheNames(page)).ShouldContain(LiveSiteFixture.ShellCache);

        await page.EvaluateAsync("async () => navigator.serviceWorker.controller.postMessage('clear-api-cache')");
        await Settle(page);

        (await CacheNames(page)).ShouldNotContain(LiveSiteFixture.ApiCache);
        (await CacheNames(page)).ShouldContain(LiveSiteFixture.ShellCache);
        (await CacheKeys(page)).ShouldContain(LiveSiteFixture.Fallback);
    }

    [Fact]
    public async Task Activation_purges_stale_caches_and_preserves_current_shell_and_api_caches()
    {
        var (page, _) = await InstalledSession(async page =>
        {
            await page.EvaluateAsync($@"async () => {{
                await caches.open('stale-build').then(c => c.put('/stale', new Response('stale')));
                await caches.open('{LiveSiteFixture.ShellCache}')
                    .then(c => c.put('/shell-control', new Response('shell')));
                await caches.open('{LiveSiteFixture.ApiCache}')
                    .then(c => c.put('{LiveSiteFixture.ApiRoute}', new Response('api')));
            }}");
        });
        await Settle(page);

        (await CacheNames(page)).ShouldNotContain("stale-build");
        (await CacheNames(page)).ShouldContain(LiveSiteFixture.ShellCache);
        (await CacheNames(page)).ShouldContain(LiveSiteFixture.ApiCache);
    }

    /// <summary>A page with the worker installed and activated, from a cold start every time.</summary>
    /// <remarks>
    /// The worker is registered here rather than by loading a page that ships the client script.
    /// That keeps these tests about the worker's behaviour: whether the client registers it is a
    /// separate concern, covered elsewhere, and depending on it here meant the suite hung silently
    /// the first time the entry page turned out not to include the script.
    /// </remarks>
    private async Task<IPage> Installed()
    {
        var (page, _) = await InstalledSession();
        return page;
    }

    private async Task<(IPage Page, Func<Task> GoOffline)> InstalledSession(
        Func<IPage, Task>? seed = null)
    {
        _site.EnableNetwork();
        var page = await _site.NewPageAsync();

        await page.GotoAsync(LiveSiteFixture.EntryPage);

        await page.EvaluateAsync(@"async () => {
            for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister();
            for (const k of await caches.keys()) await caches.delete(k);
        }");
        if (seed is not null) await seed(page);
        await page.ReloadAsync();

        // Raced against a timeout on purpose. EvaluateAsync has no default timeout, so a worker
        // that never activates would otherwise hang the run instead of failing it.
        await page.EvaluateAsync(@"async () => {
            await navigator.serviceWorker.register('/sw.js');
            await Promise.race([
                navigator.serviceWorker.ready,
                new Promise((_, reject) =>
                    setTimeout(() => reject(new Error('the worker never became ready')), 20000)),
            ]);
        }");

        await page.EvaluateAsync(@"async () => {
            if (navigator.serviceWorker.controller) return;
            await Promise.race([
                new Promise((resolve) =>
                    navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true })),
                new Promise((_, reject) =>
                    setTimeout(() => reject(new Error('the worker never took control')), 20000)),
            ]);
        }");

        await Settle(page);
        return (page, async () =>
        {
            // The browser uses a local forwarding proxy. Disabling its upstream makes the real
            // socket unavailable to page and service-worker requests alike.
            _site.DisableNetwork();
        });
    }

    /// <summary>Cache writes are fire and forget inside the worker, so give them a moment.</summary>
    private static Task Settle(IPage page) => page.WaitForTimeoutAsync(1500);

    private static async Task<IReadOnlyList<string>> CacheKeys(IPage page) =>
        await page.EvaluateAsync<string[]>(@"async () => {
            const out = [];
            for (const name of await caches.keys()) {
                const cache = await caches.open(name);
                for (const request of await cache.keys()) out.push(new URL(request.url).pathname);
            }
            return out;
        }");

    private static async Task<IReadOnlyList<string>> CacheNames(IPage page) =>
        await page.EvaluateAsync<string[]>("async () => await caches.keys()");

}
