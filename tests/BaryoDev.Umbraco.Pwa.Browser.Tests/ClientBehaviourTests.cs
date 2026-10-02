using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// What the generated client script does, rather than what it says.
/// </summary>
/// <remarks>
/// Every other test of <c>PwaAssetGenerator.Client()</c> is a <c>ShouldContain</c> on its source.
/// Those would all stay green with <c>escapeHtml</c> returning its input, <c>dismissed()</c> never
/// reading the key, or the <c>navigator.standalone</c> branch gone, because the strings they look
/// for would still be there. Here the script is loaded by a page and run.
///
/// <para>
/// <c>beforeinstallprompt</c> does not fire in headless Chromium, so the event is dispatched the
/// way a qualifying browser would. Everything it triggers is the shipped script's.
/// </para>
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class ClientBehaviourTests
{
    private const string Report = "/umbraco/pwa/api/report";
    private const string Banner = "#bd-pwa-install";

    private const string Android =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Mobile Safari/537.36";

    private const string Iphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 "
        + "(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    /// <summary>A page that ships the script and nothing else, served at whatever path asks.</summary>
    private const string Shell =
        "<!doctype html><title>shell</title>"
        + "<link rel=\"manifest\" href=\"/manifest.webmanifest\">"
        + "<script src=\"/baryodev-pwa.js\" defer></script><p>shell</p>";

    private readonly LiveSiteFixture _site;

    public ClientBehaviourTests(LiveSiteFixture site) => _site = site;

    [Fact]
    public async Task The_client_registers_the_worker_without_any_help()
    {
        var page = await NewPageAsync(Android);
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        // Bounded. serviceWorker.ready never settles when nothing registered, and an unbounded
        // wait turns "the client did not register" into a hung run.
        var registration = await page.EvaluateAsync<string>(
            """
            async () => {
              const registered = navigator.serviceWorker.ready
                .then(r => JSON.stringify({ script: r.active.scriptURL, scope: r.scope }));
              const gaveUp = new Promise(resolve => setTimeout(() => resolve('null'), 15000));
              return await Promise.race([registered, gaveUp]);
            }
            """);

        registration.ShouldNotBe("null", "no worker was registered by the page");
        var json = JsonDocument.Parse(registration).RootElement;
        json.GetProperty("script").GetString().ShouldBe(_site.BaseUrl + "/sw.js");
        json.GetProperty("scope").GetString().ShouldBe(_site.BaseUrl + "/");
    }

    [Fact]
    public async Task Loading_a_page_reports_a_browser_tab_visit()
    {
        var page = await NewPageAsync(Android);

        var (body, status) = await ReportFrom(page, () => page.GotoAsync(LiveSiteFixture.EntryPage));

        status.ShouldBe(202);
        body.GetProperty("displayMode").GetString().ShouldBe("browser");
        body.GetProperty("installed").GetBoolean().ShouldBeFalse();
        body.GetProperty("platform").GetString().ShouldBe("android");

        var deviceId = body.GetProperty("deviceId").GetString();
        deviceId.ShouldNotBeNullOrWhiteSpace();

        // The next visit is the same device, not a new one. Without this every launch of an
        // installed app would be counted as another install.
        var (again, _) = await ReportFrom(page, () => page.ReloadAsync());
        again.GetProperty("deviceId").GetString().ShouldBe(deviceId);
    }

    [Fact]
    public async Task An_installed_app_reports_itself_installed_and_is_not_asked_to_install()
    {
        var page = await NewPageAsync(Android);
        await EmulateDisplayMode(page, "standalone");

        var (body, _) = await ReportFrom(page, () => page.GotoAsync(LiveSiteFixture.EntryPage));

        body.GetProperty("displayMode").GetString().ShouldBe("standalone");
        body.GetProperty("installed").GetBoolean().ShouldBeTrue();

        await OfferInstall(page);
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_browser_put_into_fullscreen_is_not_reported_as_an_install()
    {
        // (display-mode: fullscreen) matches a browser someone pressed F11 in. The fixture's
        // manifest asks for standalone, so fullscreen here cannot be the installed app.
        var page = await NewPageAsync(Android);
        await EmulateDisplayMode(page, "fullscreen");

        var (body, _) = await ReportFrom(page, () => page.GotoAsync(LiveSiteFixture.EntryPage));

        body.GetProperty("displayMode").GetString().ShouldBe("fullscreen");
        body.GetProperty("installed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task An_ios_home_screen_app_is_recognised_and_is_not_asked_to_install()
    {
        // iOS does not report a home screen app through display-mode, only navigator.standalone.
        // The control is the iOS test below: the same browser without the flag gets the banner.
        var page = await NewPageAsync(Iphone);
        await page.Context.AddInitScriptAsync(
            "Object.defineProperty(Navigator.prototype, 'standalone', { get: () => true });");

        var (body, _) = await ReportFrom(page, () => page.GotoAsync(LiveSiteFixture.EntryPage));

        body.GetProperty("displayMode").GetString().ShouldBe("standalone");
        body.GetProperty("installed").GetBoolean().ShouldBeTrue();
        body.GetProperty("platform").GetString().ShouldBe("ios");
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_banner_waits_for_the_browser_to_say_the_site_can_be_installed()
    {
        var page = await NewPageAsync(Android);
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        // An Install button that cannot install anything is worse than no banner.
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        var prevented = await OfferInstall(page);

        // Chrome shows its own mini-infobar unless the event is cancelled.
        prevented.ShouldBeTrue();
        (await page.Locator(Banner).CountAsync()).ShouldBe(1);
        (await page.Locator($"{Banner} button").AllTextContentsAsync()).ShouldContain("Install");
    }

    [Fact]
    public async Task On_ios_the_banner_gives_instructions_and_no_install_button()
    {
        // No event and no API on iOS, so the banner appears on load and can only say what to tap.
        var page = await NewPageAsync(Iphone);
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        (await page.Locator(Banner).CountAsync()).ShouldBe(1);
        (await page.Locator(Banner).InnerTextAsync()).ShouldContain("Add to Home Screen");
        (await page.Locator($"{Banner} button").AllTextContentsAsync()).ShouldNotContain("Install");
    }

    [Theory]
    [InlineData("/umbraco/client-shell")]
    [InlineData("/Umbraco/client-shell")]
    [InlineData("/%75mbraco/client-shell")]
    public async Task The_banner_stays_away_under_a_hidden_path(string path)
    {
        var page = await ShellAt(path);

        await OfferInstall(page);

        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        // The script did run on this page, so the banner is missing because it was suppressed.
        (await page.EvaluateAsync<string?>("() => localStorage.getItem('bd_pwa_device')"))
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_same_page_outside_the_hidden_paths_shows_the_banner()
    {
        // The control for the three above. "/umbrella" also proves the match is on the configured
        // prefix and not on anything that merely starts with the same letters.
        var page = await ShellAt("/umbrella/client-shell");

        await OfferInstall(page);

        (await page.Locator(Banner).CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Dismissing_the_banner_is_remembered_across_a_reload()
    {
        var page = await NewPageAsync(Android);
        await page.GotoAsync(LiveSiteFixture.EntryPage);
        await OfferInstall(page);
        (await page.Locator(Banner).CountAsync()).ShouldBe(1);

        await page.Locator($"{Banner} button[aria-label='Dismiss']").ClickAsync();
        (await page.Locator(Banner).CountAsync()).ShouldBe(0);

        await page.ReloadAsync();
        await OfferInstall(page);

        (await page.Locator(Banner).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Accepting_the_install_asks_the_browser_and_clears_the_banner()
    {
        var page = await NewPageAsync(Android);
        await page.GotoAsync(LiveSiteFixture.EntryPage);
        await OfferInstall(page);

        await page.Locator($"{Banner} button", new() { HasTextString = "Install" }).ClickAsync();

        (await page.EvaluateAsync<bool>("() => window.bdPromptCalled === true")).ShouldBeTrue();
        await page.Locator(Banner).WaitForAsync(new() { State = WaitForSelectorState.Detached });
    }

    [Theory]
    [InlineData(Android)]
    [InlineData(Iphone)]
    public async Task A_hostile_app_name_renders_as_text_not_markup(string userAgent)
    {
        // Both branches build the banner with innerHTML, and each escapes the name separately.
        var page = await NewPageAsync(userAgent);
        await page.GotoAsync(LiveSiteFixture.EntryPage);
        if (userAgent == Android) await OfferInstall(page);

        var heading = page.Locator($"{Banner} strong").First;

        (await heading.TextContentAsync()).ShouldBe("Install " + LiveSiteFixture.HostileAppName);
        (await heading.Locator("img").CountAsync()).ShouldBe(0);
    }

    private async Task<IPage> NewPageAsync(string userAgent)
    {
        _site.EnableNetwork();
        var context = await _site.Browser.NewContextAsync(new()
        {
            BaseURL = _site.BaseUrl,
            UserAgent = userAgent,
        });
        return await context.NewPageAsync();
    }

    /// <summary>
    /// Serves <see cref="Shell"/> at a path the site does not have, so the script can be run at a
    /// location of the test's choosing. The script itself is still fetched from the real site.
    /// </summary>
    private async Task<IPage> ShellAt(string path)
    {
        var page = await NewPageAsync(Android);
        await page.RouteAsync(
            url => new Uri(url).AbsolutePath.EndsWith("/client-shell", StringComparison.Ordinal),
            route => route.FulfillAsync(new() { ContentType = "text/html", Body = Shell }));

        await page.GotoAsync(path);

        // The string, not Uri.AbsolutePath: .NET decodes %75 and would hide a browser that did too.
        page.Url.ShouldBe(_site.BaseUrl + path);
        return page;
    }

    /// <summary>
    /// Dispatches <c>beforeinstallprompt</c> and returns whether the script cancelled it.
    /// </summary>
    private static Task<bool> OfferInstall(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
              const event = new Event('beforeinstallprompt', { cancelable: true });
              event.prompt = () => { window.bdPromptCalled = true; return Promise.resolve(); };
              event.userChoice = Promise.resolve({ outcome: 'accepted' });
              window.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """);

    /// <summary>The report the page sends while <paramref name="action"/> runs.</summary>
    private static async Task<(JsonElement Body, int Status)> ReportFrom(IPage page, Func<Task> action)
    {
        // Waits on the response rather than asking the request for it: IRequest.ResponseAsync has
        // no timeout, so a report the site never answered would hang the run instead of failing.
        var response = await page.RunAndWaitForResponseAsync(
            action,
            r => r.Request.Method == "POST" && new Uri(r.Url).AbsolutePath == Report);

        return (JsonDocument.Parse(response.Request.PostData!).RootElement.Clone(), response.Status);
    }

    /// <summary>
    /// Answers the <c>display-mode</c> media query the way an installed app's window would.
    /// </summary>
    /// <remarks>
    /// Headless Chromium has no installed apps, and neither Playwright's media emulation nor the
    /// protocol's will set this feature. Only the query's answer is supplied. Everything that
    /// reads it is the shipped script's.
    /// </remarks>
    private static Task EmulateDisplayMode(IPage page, string mode) =>
        page.Context.AddInitScriptAsync(
            $$"""
            (() => {
              const real = window.matchMedia.bind(window);
              window.matchMedia = query => {
                const asked = /^\(display-mode:\s*([a-z-]+)\)$/.exec(query);
                return asked ? { matches: asked[1] === '{{mode}}', media: query } : real(query);
              };
            })();
            """);
}
