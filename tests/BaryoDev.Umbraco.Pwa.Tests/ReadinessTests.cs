using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
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

    /// <summary>Runs the check with one icon swapped in, leaving the rest of the config alone.</summary>
    private async Task<PwaReadiness> CheckWith(PwaIcon icon)
    {
        var options = _site.Resolve<IOptionsMonitor<PwaOptions>>().CurrentValue;
        var original = options.Manifest.Icons.ToList();

        try
        {
            options.Manifest.Icons.RemoveAll(i => i.Sizes == icon.Sizes && i.Purpose is null);
            options.Manifest.Icons.Add(icon);
            return await Check();
        }
        finally
        {
            options.Manifest.Icons.Clear();
            options.Manifest.Icons.AddRange(original);
        }
    }

    [Fact]
    public async Task An_icon_that_does_not_exist_is_reported_rather_than_ignored()
    {
        // The scenario this whole feature exists for: an icon is configured, so nothing looks
        // wrong, but the file is not there and the browser silently declines to install.
        //
        // Written against a deliberately missing path rather than against "no icons configured".
        // An earlier version relied on the fixture having none, and it passed for the wrong
        // reason: the icons WERE inherited from appsettings, and the check was failing on a bug
        // where a leading slash parsed as a file:// URI. Fixing the bug turned the test red,
        // which is the only reason it was noticed.
        var readiness = await CheckWith(new PwaIcon { Src = "/definitely-not-here.png", Sizes = "192x192" });

        readiness.Installable.ShouldBeFalse();

        var check = readiness.Checks.Single(c => c.Name == "Icon 192x192");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("definitely-not-here.png");
    }

    [Fact]
    public async Task A_configured_icon_that_exists_passes()
    {
        // The other half. Without this, the check could report everything as broken and still
        // look like it was working.
        var readiness = await Check();

        readiness.Checks.Single(c => c.Name == "Icon 192x192").Passed
            .ShouldBeTrue("the demo site ships this icon under wwwroot");
    }

    [Fact]
    public async Task A_site_relative_path_is_not_mistaken_for_a_file_uri()
    {
        // On Linux and macOS, Uri.TryCreate("/icon.png", UriKind.Absolute, ...) succeeds as a
        // file:// URI. Treating that as remote sends every ordinary icon down the HTTP branch,
        // where it dies with "the 'file' scheme is not supported". Platform-specific, and only on
        // the platforms this actually ships to.
        var readiness = await Check();

        foreach (var check in readiness.Checks.Where(c => c.Name.StartsWith("Icon")))
        {
            check.Detail.ShouldNotContain("file", Case.Insensitive,
                "a site-relative icon must never be resolved as a file:// URI");
        }
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
