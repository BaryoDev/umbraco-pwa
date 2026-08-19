using System.Text.RegularExpressions;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The demo host's root has to serve the demo.
/// </summary>
/// <remarks>
/// This is a test about the demo site rather than the package, which is unusual, and it is here
/// because the failure it guards was invisible to everything else. The demo has no published
/// content, so Umbraco served its "Welcome to your Umbraco installation" screen at <c>/</c>. That
/// is a valid 200, so nothing failed. Two things broke quietly:
///
/// <list type="number">
///   <item>Every listing that sends people to the demo (README, NuGet, the Marketplace
///   description) points at the bare origin, so the package's shop window was an empty-Umbraco
///   installer page.</item>
///   <item><see cref="PwaOptions.NavigationFallback"/> defaults to <c>/</c>, so the worker
///   precached that same screen. The demonstration of "works offline" was the installer page.</item>
/// </list>
///
/// The last test is the one that matters long term: it reads the fallback out of the generated
/// worker rather than hard-coding <c>/</c>, so changing either the route or the option keeps it
/// honest instead of quietly making it vacuous.
/// </remarks>
[Collection(UmbracoCollection.Name)]
public class DemoLandingPageTests
{
    private const string DemoMarker = "This Umbraco site is an app";
    private const string UmbracoNoContentMarker = "No Published Content";

    private readonly UmbracoSiteFixture _site;

    public DemoLandingPageTests(UmbracoSiteFixture site) => _site = site;

    [Fact]
    public async Task The_root_serves_the_demo_page()
    {
        var body = await _site.Client.GetStringAsync("/");

        body.ShouldContain(DemoMarker);
    }

    [Fact]
    public async Task The_root_is_not_umbraco_s_no_published_content_screen()
    {
        var body = await _site.Client.GetStringAsync("/");

        body.ShouldNotContain(UmbracoNoContentMarker);
    }

    [Fact]
    public async Task The_root_carries_the_two_lines_that_make_a_site_installable()
    {
        var body = await _site.Client.GetStringAsync("/");

        // Without both of these on the landing page there is nothing for a visitor to install,
        // however good the page looks.
        body.ShouldContain("rel=\"manifest\"");
        body.ShouldContain("baryodev-pwa.js");
    }

    [Fact]
    public async Task The_page_the_worker_precaches_offline_is_the_demo()
    {
        var worker = await _site.Client.GetStringAsync("/sw.js");

        var fallback = Regex.Match(worker, @"NAV_FALLBACK\s*=\s*""([^""]+)""");
        fallback.Success.ShouldBeTrue("the worker should declare a navigation fallback");

        var body = await _site.Client.GetStringAsync(fallback.Groups[1].Value);

        body.ShouldContain(DemoMarker);
        body.ShouldNotContain(UmbracoNoContentMarker);
    }
}
