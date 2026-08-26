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
    public async Task The_backoffice_is_never_cached()
    {
        // A cached backoffice is a stale editing experience, and on a shared machine it is one
        // user's data served to another.
        var page = await Installed();

        await page.EvaluateAsync("async () => { try { await fetch('/umbraco/'); } catch {} }");
        await Settle(page);

        (await CacheKeys(page)).ShouldNotContain(k => k.StartsWith("/umbraco/"));
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

    private async Task<(IPage Page, Func<Task> GoOffline)> InstalledSession()
    {
        _site.EnableNetwork();
        var page = await _site.NewPageAsync();

        await page.GotoAsync(LiveSiteFixture.EntryPage);

        await page.EvaluateAsync(@"async () => {
            for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister();
            for (const k of await caches.keys()) await caches.delete(k);
        }");
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

}
