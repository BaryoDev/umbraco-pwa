using System.Net;
using System.Net.Http.Json;
using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The write path, which is the only part of this package an anonymous caller can reach.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class ReportEndpointTests
{
    private readonly UmbracoSiteFixture _site;
    private readonly IPwaInstallService _installs;

    public ReportEndpointTests(UmbracoSiteFixture site)
    {
        _site = site;
        _installs = site.Resolve<IPwaInstallService>();
    }

    private static object Report(
        string deviceId,
        string displayMode = "standalone",
        string platform = "android",
        bool installed = true) =>
        new { deviceId, displayMode, platform, installed };

    private async Task<PwaInstallModel?> Find(string deviceId) =>
        (await _installs.GetAllAsync(installedOnly: false))
        .FirstOrDefault(x => x.DeviceId == deviceId);

    [Theory]
    // Anything that could open a tag or an attribute in the dashboard, which renders this value
    // into an administrator's browser. The escaping there is still correct; it is no longer the
    // only thing holding.
    [InlineData("<script>alert(1)</script>")]
    [InlineData("\" onmouseover=\"alert(1)")]
    [InlineData("id<img src=x onerror=alert(1)>")]
    [InlineData("id'--")]
    [InlineData("id&amp;")]
    [InlineData("id with spaces")]
    [InlineData("id/../../etc/passwd")]
    [InlineData("id\u0000null")]
    public async Task A_device_id_that_is_not_browser_shaped_is_dropped(string deviceId)
    {
        var response = await _site.ReportAsync(Report(deviceId));

        // Still 202. A distinguishable response would tell an anonymous caller which ids exist,
        // which is the reason this endpoint has one answer for everything.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await Find(deviceId)).ShouldBeNull();
    }

    [Theory]
    // The control. A check that refused everything would pass every case above and stop the
    // package working, and these are the exact shapes the generated client emits.
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("k3n8vq2x1z9")]
    [InlineData("ephemeral-a1b2c3")]
    [InlineData("base64url_id.with-all:allowed")]
    public async Task A_browser_shaped_device_id_is_still_stored(string deviceId)
    {
        var unique = $"{deviceId}-{Guid.NewGuid():N}"[..Math.Min(100, deviceId.Length + 33)];

        var response = await _site.ReportAsync(Report(unique));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Find(unique)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_report_is_accepted_and_stored()
    {
        var id = $"store-{Guid.NewGuid():N}";

        var response = await _site.ReportAsync(Report(id, platform: "ios"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var row = await Find(id);
        row.ShouldNotBeNull();
        row!.Platform.ShouldBe("ios");
        row.Installed.ShouldBeTrue();
        row.LaunchCount.ShouldBe(1);
        row.InstalledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Repeat_launches_bump_the_count_instead_of_adding_rows()
    {
        var id = $"dedupe-{Guid.NewGuid():N}";

        await _site.ReportAsync(Report(id));
        await _site.ReportAsync(Report(id));
        await _site.ReportAsync(Report(id));

        var all = await _installs.GetAllAsync(installedOnly: false);

        all.Count(x => x.DeviceId == id).ShouldBe(1);
        all.Single(x => x.DeviceId == id).LaunchCount.ShouldBe(3);
    }

    [Fact]
    public async Task Concurrent_first_reports_create_one_row_and_keep_both_launches()
    {
        var id = $"race-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(
            _site.ReportAsync(Report(id)),
            _site.ReportAsync(Report(id)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Accepted);
        var row = await Find(id);
        row.ShouldNotBeNull();
        row!.LaunchCount.ShouldBe(2);
        (await _installs.GetAllAsync(installedOnly: false)).Count(x => x.DeviceId == id).ShouldBe(1);
    }

    [Fact]
    public async Task Installed_is_sticky_once_set()
    {
        // Someone installs the app, then later opens the site in an ordinary tab. They have still
        // installed it, and the headline number would be meaningless if this flapped.
        var id = $"sticky-{Guid.NewGuid():N}";

        await _site.ReportAsync(Report(id, "standalone", installed: true));
        await _site.ReportAsync(Report(id, "browser", installed: false));

        var row = await Find(id);
        row!.Installed.ShouldBeTrue();
        row.InstalledAt.ShouldNotBeNull();
        row.DisplayMode.ShouldBe("browser", "the last mode is still recorded truthfully");
    }

    [Fact]
    public async Task A_standalone_display_mode_counts_as_installed_even_if_the_flag_says_otherwise()
    {
        // The two fields can disagree when a client is out of date. Display mode is the one the
        // browser actually knows.
        var id = $"infer-{Guid.NewGuid():N}";

        await _site.ReportAsync(Report(id, "standalone", installed: false));

        (await Find(id))!.Installed.ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_device_id_is_ignored_rather_than_stored()
    {
        var before = (await _installs.GetAllAsync(installedOnly: false)).Count;

        var response = await _site.ReportAsync(Report("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await _installs.GetAllAsync(installedOnly: false)).Count.ShouldBe(before);
    }

    [Theory]
    [InlineData("HACK")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public async Task An_unrecognised_display_mode_falls_back_to_browser(string displayMode)
    {
        var id = $"mode-{Guid.NewGuid():N}";

        await _site.ReportAsync(new { deviceId = id, displayMode, platform = "ios", installed = false });

        (await Find(id))!.DisplayMode.ShouldBe("browser");
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("PlayStation")]
    [InlineData("")]
    public async Task An_unrecognised_platform_becomes_other(string platform)
    {
        // The dashboard groups by this column. One crafted value would otherwise appear in the
        // backoffice as though it were a real platform.
        var id = $"plat-{Guid.NewGuid():N}";

        await _site.ReportAsync(new { deviceId = id, displayMode = "standalone", platform, installed = true });

        (await Find(id))!.Platform.ShouldBe("other");
    }

    [Fact]
    public async Task Oversized_fields_are_truncated_rather_than_rejected()
    {
        // Telemetry should degrade, never fail: a client sending junk still gets counted.
        var id = new string('x', 400);

        var response = await _site.ReportAsync(Report(id));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var stored = (await _installs.GetAllAsync(installedOnly: false))
            .FirstOrDefault(x => x.DeviceId.StartsWith("xxxx"));

        stored.ShouldNotBeNull();
        stored!.DeviceId.Length.ShouldBe(100, "the column is 100 wide and the write must not throw");
    }

    [Fact]
    public async Task The_endpoint_never_reveals_whether_a_device_is_known()
    {
        // A distinguishable response would let an anonymous caller enumerate device ids.
        var known = $"probe-{Guid.NewGuid():N}";
        await _site.ReportAsync(Report(known));

        var repeat = await _site.ReportAsync(Report(known));
        var fresh = await _site.ReportAsync(Report($"probe-{Guid.NewGuid():N}"));

        repeat.StatusCode.ShouldBe(fresh.StatusCode);
        (await repeat.Content.ReadAsStringAsync()).ShouldBe(await fresh.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_malformed_body_is_rejected_without_a_server_error()
    {
        using var content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json");

        var response = await _site.Client.PostAsync("/umbraco/pwa/api/report", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
