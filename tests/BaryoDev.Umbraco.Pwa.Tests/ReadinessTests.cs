using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The installability preflight.
/// </summary>
/// <remarks>
/// This feature exists because of a failure found while deploying this package's own demo: the
/// manifest pointed at icons that returned 404, so Chrome silently declined to offer installation.
/// Nothing errored and nothing logged. These tests pin the checks that would have caught it.
/// </remarks>
[Collection(UmbracoCollection.Name)]
public class ReadinessTests
{
    private readonly UmbracoSiteFixture _site;

    public ReadinessTests(UmbracoSiteFixture site) => _site = site;

    private async Task<PwaReadiness> Check()
    {
        var service = _site.Resolve<IPwaReadinessService>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        return await service.CheckAsync(context.Request);
    }

    [Fact]
    public async Task Unreachable_icons_are_reported_rather_than_ignored()
    {
        // The fixture configures no icons at all, which is the same class of failure as icons
        // that 404: the browser will not offer to install either way.
        var readiness = await Check();

        readiness.Installable.ShouldBeFalse();
        readiness.Checks.ShouldContain(c => c.Name == "Icon 192x192" && !c.Passed);
        readiness.Checks.ShouldContain(c => c.Name == "Icon 512x512" && !c.Passed);
    }

    [Fact]
    public async Task Every_failing_check_explains_what_to_do()
    {
        // A check that says "failed" and nothing else just moves the mystery.
        var readiness = await Check();

        foreach (var check in readiness.Checks.Where(c => !c.Passed))
        {
            check.Detail.ShouldNotBeNullOrWhiteSpace($"{check.Name} must explain itself");
            check.Detail.Length.ShouldBeGreaterThan(20, $"{check.Name} needs a usable explanation");
        }
    }

    [Fact]
    public async Task A_configured_name_passes()
    {
        var readiness = await Check();

        readiness.Checks.ShouldContain(c => c.Name == "Manifest has a name" && c.Passed);
    }

    [Fact]
    public async Task Https_is_checked_because_a_service_worker_will_not_register_without_it()
    {
        var readiness = await Check();

        readiness.Checks.ShouldContain(c => c.Name == "Served over HTTPS" && c.Passed);
    }

    [Fact]
    public async Task Localhost_counts_as_secure()
    {
        // Otherwise every local development setup would report itself as broken.
        var service = _site.Resolve<IPwaReadinessService>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);

        var readiness = await service.CheckAsync(context.Request);

        readiness.Checks.Single(c => c.Name == "Served over HTTPS").Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task Advisory_checks_do_not_block_installability()
    {
        // A missing maskable icon is a cosmetic problem on Android, not a reason to report the
        // site as broken.
        var readiness = await Check();

        var maskable = readiness.Checks.Single(c => c.Name == "Maskable icon");
        maskable.Advisory.ShouldBeTrue();
    }

    [Fact]
    public async Task The_readiness_endpoint_is_backoffice_only()
    {
        // It reports configuration details, so it is not for anonymous callers.
        var response = await _site.Client.GetAsync(
            "/umbraco/management/api/v1/baryodev/pwa/readiness");

        response.StatusCode.ShouldBeOneOf(
            System.Net.HttpStatusCode.Unauthorized, System.Net.HttpStatusCode.Forbidden);
    }
}
