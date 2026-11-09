using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// The engagement gate on the install prompt: second visit, then enough scroll or enough time.
/// </summary>
/// <remarks>
/// The fixture site runs with every threshold at 0 so the other banner tests see the prompt at
/// once. Here each page gets its own thresholds by rewriting the three <c>var MIN_... = N;</c>
/// constants in the served <c>/baryodev-pwa.js</c>. That tests the client logic with the values
/// the test picks. That the options reach those constants is covered by
/// <c>GeneratedAssetTests</c>, which read the emitted script. Time is driven with Playwright's
/// clock, so no test waits 30 real seconds.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class EngagementGateTests
{
    private const string Banner = "#bd-pwa-install";

    private const string Android =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Mobile Safari/537.36";

    private const string Iphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 "
        + "(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    /// <summary>Three screens tall, so half a page is a real scroll.</summary>
    private const string TallShell =
        "<!doctype html><title>shell</title>"
        + "<link rel=\"manifest\" href=\"/manifest.webmanifest\">"
        + "<script src=\"/baryodev-pwa.js\" defer></script>"
        + "<div style=\"height:3000px\">tall</div>";

    private readonly LiveSiteFixture _site;

    public EngagementGateTests(LiveSiteFixture site) => _site = site;

    [Fact]
    public async Task A_first_visit_never_shows_the_banner_when_two_visits_are_needed()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 50, minSeconds: 30, previousVisit: false);
        await OfferInstall(page);

        await ScrollToBottom(page);
        await page.Clock.FastForwardAsync(60_000);
        await page.WaitForTimeoutAsync(300);

        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task On_a_second_visit_the_banner_waits_for_half_the_page_to_be_scrolled()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 50, minSeconds: 30, previousVisit: true);
        await OfferInstall(page);

        // Some time has passed, but not the 30 seconds.
        await page.Clock.FastForwardAsync(10_000);
        await page.WaitForTimeoutAsync(300);
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        await ScrollToBottom(page);

        await page.Locator(Banner).WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task A_small_scroll_is_not_half_the_page()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 50, minSeconds: 30, previousVisit: true);
        await OfferInstall(page);

        // 3000px page in a 720px window: about 2280px to scroll, so 200px is under 10%.
        await page.EvaluateAsync("() => window.scrollTo(0, 200)");
        await page.WaitForTimeoutAsync(300);

        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task On_a_second_visit_the_timer_alone_is_enough_without_scrolling()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 50, minSeconds: 2, previousVisit: true);
        await OfferInstall(page);
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        await page.Clock.FastForwardAsync(2_500);

        await page.Locator(Banner).WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task A_zero_scroll_threshold_shows_the_banner_at_once_even_with_a_long_timer()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 0, minSeconds: 30, previousVisit: true);

        await OfferInstall(page);

        await page.Locator(Banner).WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task A_zero_time_threshold_shows_the_banner_at_once_even_with_a_scroll_threshold()
    {
        var page = await OpenAsync(Android, minVisits: 2, minScroll: 50, minSeconds: 0, previousVisit: true);

        await OfferInstall(page);

        await page.Locator(Banner).WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task On_ios_a_first_visit_never_shows_the_banner_when_two_visits_are_needed()
    {
        var page = await OpenAsync(Iphone, minVisits: 2, minScroll: 50, minSeconds: 30, previousVisit: false);

        await ScrollToBottom(page);
        await page.Clock.FastForwardAsync(60_000);
        await page.WaitForTimeoutAsync(300);

        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task On_ios_a_second_visit_waits_for_the_scroll_and_then_shows_instructions()
    {
        var page = await OpenAsync(Iphone, minVisits: 2, minScroll: 50, minSeconds: 30, previousVisit: true);
        await page.WaitForTimeoutAsync(300);
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        await ScrollToBottom(page);

        await page.Locator(Banner).WaitForAsync(new() { Timeout = 5000 });
        (await page.Locator(Banner).InnerTextAsync()).ShouldContain("Add to Home Screen");
    }

    /// <summary>
    /// Opens the tall shell page with the script's thresholds replaced and the clock under the
    /// test's control. <paramref name="previousVisit"/> seeds a visit from an earlier day, so the
    /// page load counts as the second.
    /// </summary>
    private async Task<IPage> OpenAsync(
        string userAgent, int minVisits, int minScroll, int minSeconds, bool previousVisit)
    {
        _site.EnableNetwork();
        var context = await _site.Browser.NewContextAsync(new()
        {
            BaseURL = _site.BaseUrl,
            UserAgent = userAgent,
            // A worker controlling the page would answer the script request before the route.
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        var page = await context.NewPageAsync();

        if (previousVisit)
        {
            await context.AddInitScriptAsync(
                "if (!localStorage.getItem('bd_pwa_visits')) "
                + "localStorage.setItem('bd_pwa_visits', JSON.stringify({ n: 1, last: '2000-01-01' }));");
        }

        await page.RouteAsync(
            url => new Uri(url).AbsolutePath.EndsWith("/client-shell", StringComparison.Ordinal),
            route => route.FulfillAsync(new() { ContentType = "text/html", Body = TallShell }));

        await page.RouteAsync(
            url => new Uri(url).AbsolutePath == "/baryodev-pwa.js",
            async route =>
            {
                var response = await route.FetchAsync();
                var script = await response.TextAsync();
                script = Replace(script, "MIN_VISITS", minVisits);
                script = Replace(script, "MIN_SCROLL", minScroll);
                script = Replace(script, "MIN_SECONDS", minSeconds);
                await route.FulfillAsync(new()
                {
                    ContentType = "application/javascript",
                    Body = script,
                });
            });

        await page.Clock.InstallAsync();
        await page.GotoAsync("/client-shell");
        return page;
    }

    /// <summary>Fails loudly when the constant is gone, so a rename cannot turn these into no-ops.</summary>
    private static string Replace(string script, string name, int value)
    {
        var pattern = new Regex($@"var {name} = \d+;");
        pattern.IsMatch(script).ShouldBeTrue($"the served script has no 'var {name} = N;'");
        return pattern.Replace(script, $"var {name} = {value};");
    }

    private static Task ScrollToBottom(IPage page) =>
        page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");

    private static Task<bool> OfferInstall(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
              const event = new Event('beforeinstallprompt', { cancelable: true });
              event.prompt = () => Promise.resolve();
              event.userChoice = Promise.resolve({ outcome: 'accepted' });
              window.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """);
}
