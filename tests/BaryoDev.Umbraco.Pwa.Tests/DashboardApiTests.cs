using BaryoDev.Umbraco.Pwa.Controllers;
using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The controller behind the backoffice dashboard.
/// </summary>
/// <remarks>
/// <see cref="BackofficeAccessTests"/> proves an anonymous caller is refused and that the routes
/// are registered. The services underneath are covered separately. What was missing until these
/// tests is the layer between: the action bodies themselves never ran, so a rename, a wrong
/// return type or a broken query binding would have shipped with a green suite and an empty
/// dashboard.
///
/// The controller is exercised directly rather than over HTTP because Umbraco's management API
/// authenticates through OpenIddict, and the password grant is disabled, so acquiring a token in
/// tests would mean driving an authorization-code flow with PKCE. That would test Umbraco's
/// login rather than this package. Authorization is already asserted separately; what these
/// pin is that each action returns 200 with the payload the dashboard reads.
/// </remarks>
[Collection(UmbracoCollection.Name)]
public class DashboardApiTests
{
    private readonly UmbracoSiteFixture _site;

    public DashboardApiTests(UmbracoSiteFixture site) => _site = site;

    private PwaInstallsController Controller()
    {
        var controller = new PwaInstallsController(
            _site.Resolve<IPwaInstallService>(),
            _site.Resolve<IPwaReadinessService>());

        // Readiness reads Request, so the controller needs a context to run at all.
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        return controller;
    }

    private static T Body<T>(IActionResult result) where T : class
    {
        var ok = result.ShouldBeOfType<OkObjectResult>();
        ok.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ok.Value.ShouldNotBeNull();

        // Assignability rather than an exact type: the actions return the interface the service
        // declares, and pinning the concrete list type would break on an implementation detail.
        return (ok.Value as T)
            .ShouldNotBeNull($"expected a body assignable to {typeof(T).Name}, got {ok.Value!.GetType().Name}");
    }

    private Task Report(string deviceId, bool installed, string platform = "android") =>
        _site.ReportAsync(new
        {
            deviceId,
            displayMode = installed ? "standalone" : "browser",
            platform,
            installed,
        });

    [Fact]
    public async Task Summary_returns_the_counts_the_dashboard_renders()
    {
        var before = Body<PwaInstallSummary>(await Controller().Summary(default));

        await Report($"summary-{Guid.NewGuid():N}", installed: true);

        var after = Body<PwaInstallSummary>(await Controller().Summary(default));

        after.Installed.ShouldBe(before.Installed + 1);
        after.TotalDevices.ShouldBe(before.TotalDevices + 1);
    }

    [Fact]
    public async Task Installs_returns_rows_including_one_just_reported()
    {
        var id = $"row-{Guid.NewGuid():N}";
        await Report(id, installed: true);

        var rows = Body<IEnumerable<PwaInstallModel>>(await Controller().Installs());

        rows.ShouldContain(r => r.DeviceId == id);
    }

    [Fact]
    public async Task The_installedOnly_flag_is_honoured_by_the_action()
    {
        // The query parameter is bound by MVC in production, so a rename here breaks the
        // dashboard's filter toggle silently. This pins the action's own handling of it.
        var browserOnly = $"browser-{Guid.NewGuid():N}";
        await Report(browserOnly, installed: false);

        var all = Body<IEnumerable<PwaInstallModel>>(await Controller().Installs(installedOnly: false));
        var installedOnly = Body<IEnumerable<PwaInstallModel>>(await Controller().Installs(installedOnly: true));

        all.ShouldContain(r => r.DeviceId == browserOnly);
        installedOnly.ShouldNotContain(r => r.DeviceId == browserOnly);
    }

    [Fact]
    public async Task Readiness_returns_the_checks_rather_than_an_empty_body()
    {
        var readiness = Body<PwaReadiness>(await Controller().Readiness(default));

        readiness.Checks.ShouldNotBeEmpty();
        readiness.Checks.ShouldContain(c => c.Name == "Served over HTTPS");
        readiness.Checks.ShouldContain(c => c.Name == "Start URL has content");
    }

    [Fact]
    public async Task Every_action_returns_a_body_the_dashboard_can_read()
    {
        // Cheap guard against an action being changed to return NoContent, a bare Ok(), or a
        // different envelope. The dashboard reads all three and shows nothing if any is empty.
        var controller = Controller();

        foreach (var result in new[]
        {
            await controller.Summary(default),
            await controller.Installs(),
            await controller.Readiness(default),
        })
        {
            var ok = result.ShouldBeOfType<OkObjectResult>();
            ok.Value.ShouldNotBeNull();
        }
    }
}
