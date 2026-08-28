using System.Net;
using BaryoDev.Umbraco.Pwa.Persistence;
using BaryoDev.Umbraco.Pwa.Services;
using Shouldly;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The read side holds every recorded visitor device on the site, so the only interesting
/// question about it is who can reach it.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class BackofficeAccessTests
{
    private const string Summary = "/umbraco/management/api/v1/baryodev/pwa/summary";
    private const string Installs = "/umbraco/management/api/v1/baryodev/pwa/installs";

    private readonly UmbracoSiteFixture _site;

    public BackofficeAccessTests(UmbracoSiteFixture site) => _site = site;

    [Theory]
    [InlineData(Summary)]
    [InlineData(Installs)]
    [InlineData(Installs + "?installedOnly=true")]
    public async Task An_anonymous_caller_gets_nothing(string path)
    {
        var response = await _site.Client.GetAsync(path);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_device_data_leaks_in_the_unauthorised_response_body()
    {
        var id = $"leak-{Guid.NewGuid():N}";
        await _site.ReportAsync(new { deviceId = id, displayMode = "standalone", platform = "ios", installed = true });

        var response = await _site.Client.GetAsync(Installs);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(id);
    }

    [Fact]
    public async Task The_endpoints_are_registered_at_the_url_the_dashboard_calls()
    {
        // A 401 proves the route exists and is guarded. A 404 would mean the dashboard is calling
        // a URL that was never mapped, which looks identical to "no installs yet" in the UI.
        foreach (var path in new[] { Summary, Installs })
        {
            var response = await _site.Client.GetAsync(path);
            response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound, $"{path} should be routed");
        }
    }
}

/// <summary>
/// The aggregates behind the dashboard tiles, exercised through the service so the arithmetic is
/// tested without needing a backoffice token.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class SummaryTests
{
    private readonly UmbracoSiteFixture _site;
    private readonly IPwaInstallService _installs;

    public SummaryTests(UmbracoSiteFixture site)
    {
        _site = site;
        _installs = site.Resolve<IPwaInstallService>();
    }

    [Fact]
    public async Task Installed_and_total_move_independently()
    {
        var before = await _installs.GetSummaryAsync();

        var installed = $"sum-i-{Guid.NewGuid():N}";
        var browserOnly = $"sum-b-{Guid.NewGuid():N}";

        await _site.ReportAsync(new { deviceId = installed, displayMode = "standalone", platform = "android", installed = true });
        await _site.ReportAsync(new { deviceId = browserOnly, displayMode = "browser", platform = "windows", installed = false });

        var after = await _installs.GetSummaryAsync();

        after.Installed.ShouldBe(before.Installed + 1);
        after.TotalDevices.ShouldBe(before.TotalDevices + 2, "browser-only devices are the denominator");
    }

    [Fact]
    public async Task A_freshly_installed_device_counts_as_active()
    {
        var before = await _installs.GetSummaryAsync();

        await _site.ReportAsync(new
        {
            deviceId = $"active-{Guid.NewGuid():N}",
            displayMode = "standalone",
            platform = "ios",
            installed = true,
        });

        (await _installs.GetSummaryAsync()).ActiveLast30Days.ShouldBe(before.ActiveLast30Days + 1);
    }

    [Fact]
    public async Task Platform_counts_only_include_installed_devices()
    {
        var summaryBefore = await _installs.GetSummaryAsync();
        var linuxBefore = summaryBefore.ByPlatform.GetValueOrDefault("linux");

        await _site.ReportAsync(new
        {
            deviceId = $"plat-browser-{Guid.NewGuid():N}",
            displayMode = "browser",
            platform = "linux",
            installed = false,
        });

        var after = await _installs.GetSummaryAsync();

        after.ByPlatform.GetValueOrDefault("linux").ShouldBe(linuxBefore,
            "a browser-only device has not installed anything");
    }

    [Fact]
    public async Task Null_platform_rows_share_the_other_bucket()
    {
        var scopeProvider = _site.Resolve<IScopeProvider>();
        var now = DateTime.UtcNow;
        using (var scope = scopeProvider.CreateScope(autoComplete: true))
        {
            scope.Database.Insert(new PwaInstallDto
            {
                DeviceId = $"null-platform-{Guid.NewGuid():N}",
                Platform = null,
                DisplayMode = "standalone",
                Installed = true,
                FirstSeenAt = now,
                LastSeenAt = now,
                InstalledAt = now,
                LaunchCount = 1,
            });
            scope.Database.Insert(new PwaInstallDto
            {
                DeviceId = $"other-platform-{Guid.NewGuid():N}",
                Platform = "other",
                DisplayMode = "standalone",
                Installed = true,
                FirstSeenAt = now,
                LastSeenAt = now,
                InstalledAt = now,
                LaunchCount = 1,
            });
        }

        var summary = await _installs.GetSummaryAsync();

        summary.ByPlatform.GetValueOrDefault("other").ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task The_installed_only_filter_excludes_browser_devices()
    {
        var id = $"filter-{Guid.NewGuid():N}";
        await _site.ReportAsync(new { deviceId = id, displayMode = "browser", platform = "macos", installed = false });

        var all = await _installs.GetAllAsync(installedOnly: false);
        var onlyInstalled = await _installs.GetAllAsync(installedOnly: true);

        all.ShouldContain(x => x.DeviceId == id);
        onlyInstalled.ShouldNotContain(x => x.DeviceId == id);
    }

    [Fact]
    public async Task Rows_come_back_most_recently_seen_first()
    {
        var older = $"order-1-{Guid.NewGuid():N}";
        var newer = $"order-2-{Guid.NewGuid():N}";

        await _site.ReportAsync(new { deviceId = older, displayMode = "standalone", platform = "ios", installed = true });
        await Task.Delay(1100); // the column has second resolution on SQLite
        await _site.ReportAsync(new { deviceId = newer, displayMode = "standalone", platform = "ios", installed = true });

        var rows = await _installs.GetAllAsync(installedOnly: true);

        var olderIndex = rows.ToList().FindIndex(x => x.DeviceId == older);
        var newerIndex = rows.ToList().FindIndex(x => x.DeviceId == newer);

        newerIndex.ShouldBeLessThan(olderIndex);
    }
}
