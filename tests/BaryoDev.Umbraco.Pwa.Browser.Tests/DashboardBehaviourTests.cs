using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// Executes the shipped dashboard against a browser DOM instead of inspecting its source text.
/// </summary>
[Collection(LiveSiteCollection.Name)]
public class DashboardBehaviourTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly LiveSiteFixture _site;

    public DashboardBehaviourTests(LiveSiteFixture site) => _site = site;

    [Fact]
    public async Task A_normal_row_renders_as_expected()
    {
        var page = await OpenDashboard(new DashboardResponses
        {
            Rows = new object[]
            {
                new
                {
                    deviceId = "device-123456",
                    platform = "android",
                    installed = true,
                    displayMode = "standalone",
                    launchCount = 3,
                    installedAt = "2026-08-26T10:00:00Z",
                    lastSeenAt = "2026-08-26T11:00:00Z",
                },
            },
        });

        var row = page.Locator("baryodev-pwa-dashboard tbody tr").First;
        await row.WaitForAsync();

        (await row.InnerTextAsync()).ShouldContain("device-12345...");
        (await row.InnerTextAsync()).ShouldContain("android");
        (await row.InnerTextAsync()).ShouldContain("standalone");
        (await row.Locator("uui-tag[color='positive']").CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Hostile_row_values_are_text_and_do_not_create_dom_or_handlers()
    {
        var page = await OpenDashboard(new DashboardResponses
        {
            Rows = new object[]
            {
                new
                {
                    deviceId = "<img src=x onerror=window.__xss=1>",
                    platform = "<svg onload=window.__xss=2>",
                    installed = false,
                    displayMode = "<b title='x'>markup</b>",
                    launchCount = "<iframe src='x' onload=window.__xss=3>",
                    installedAt = "<script>window.__xss=4</script>",
                    lastSeenAt = "<b onclick=window.__xss=5>markup</b>",
                },
            },
        });

        var row = page.Locator("baryodev-pwa-dashboard tbody tr").First;
        await row.WaitForAsync();

        (await row.Locator("img, svg, b, script, iframe").CountAsync()).ShouldBe(0);
        (await row.Locator("td").CountAsync()).ShouldBe(7);
        (await page.EvaluateAsync<bool>("() => window.__xss === true")).ShouldBeFalse();
        (await row.InnerTextAsync()).ShouldContain("<svg onload=window.__xss=2>");
        (await row.InnerTextAsync()).ShouldContain("<b title='x'>markup</b>");
    }

    [Fact]
    public async Task Empty_response_shows_the_empty_state()
    {
        var page = await OpenDashboard(new DashboardResponses { Rows = Array.Empty<object>() });

        var empty = page.Locator("baryodev-pwa-dashboard .pwa-empty");
        await empty.WaitForAsync();

        (await empty.InnerTextAsync()).ShouldContain("Nothing recorded yet.");
        (await page.Locator("baryodev-pwa-dashboard tbody tr").CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Api_error_shows_the_error_state()
    {
        var page = await OpenDashboard(new DashboardResponses { Status = 500 });

        var empty = page.Locator("baryodev-pwa-dashboard .pwa-empty");
        await empty.WaitForAsync();

        (await empty.InnerTextAsync()).ShouldContain("Request failed (500).");
        (await page.Locator("#retry").CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Unauthorized_dashboard_response_explains_the_access_failure(int status)
    {
        var page = await OpenDashboard(new DashboardResponses { Status = status });

        var empty = page.Locator("baryodev-pwa-dashboard .pwa-empty");
        await empty.WaitForAsync();

        (await empty.InnerTextAsync()).ShouldContain("You do not have access to the Settings section.");
    }

    [Fact]
    public async Task Loading_state_is_visible_until_api_responses_complete()
    {
        var responses = new DashboardResponses { Release = new TaskCompletionSource() };
        var page = await OpenDashboard(responses, waitForResponses: false);

        (await page.Locator("baryodev-pwa-dashboard uui-loader-bar").CountAsync()).ShouldBe(1);

        responses.Release!.SetResult();
        await page.Locator("baryodev-pwa-dashboard .pwa-empty").WaitForAsync();
    }

    [Fact]
    public async Task Readiness_panel_renders_a_blocking_failure_as_not_installable()
    {
        var page = await OpenDashboard(new DashboardResponses
        {
            Readiness = new
            {
                installable = false,
                checks = new[]
                {
                    new { name = "Manifest", passed = false, detail = "The manifest is missing.", advisory = false },
                },
            },
        });

        var panel = page.Locator("baryodev-pwa-dashboard .pwa-ready.bad");
        await panel.WaitForAsync();

        (await panel.InnerTextAsync()).ShouldContain("This site is not installable yet.");
        (await panel.InnerTextAsync()).ShouldContain("The manifest is missing.");
    }

    private async Task<IPage> OpenDashboard(
        DashboardResponses responses,
        bool waitForResponses = true)
    {
        var page = await _site.NewPageAsync();
        await page.RouteAsync(
            "**/umbraco/management/api/v1/baryodev/pwa/**",
            route => Respond(route, responses));
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        if (waitForResponses)
        {
            await page.Locator("baryodev-pwa-dashboard .pwa-grid").WaitForAsync();
        }

        return page;
    }

    private static async Task Respond(IRoute route, DashboardResponses responses)
    {
        if (responses.Release is not null)
        {
            await responses.Release.Task;
        }

        var url = route.Request.Url;
        object body = url.Contains("/summary")
            ? responses.Summary
            : url.Contains("/readiness")
                ? responses.Readiness
                : responses.Rows;

        await route.FulfillAsync(new()
        {
            Status = responses.Status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonOptions),
        });
    }

    private sealed class DashboardResponses
    {
        public int Status { get; init; } = 200;
        public object Summary { get; init; } = new
        {
            installed = 0,
            totalDevices = 0,
            activeLast30Days = 0,
            byPlatform = new { },
        };
        public object Readiness { get; init; } = new
        {
            installable = true,
            checks = Array.Empty<object>(),
        };
        public object Rows { get; init; } = Array.Empty<object>();
        public TaskCompletionSource? Release { get; init; }
    }
}
