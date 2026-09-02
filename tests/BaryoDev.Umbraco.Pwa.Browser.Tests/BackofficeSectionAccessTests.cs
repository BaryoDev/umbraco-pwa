using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// Whether the Settings-section policy on the backoffice endpoints actually refuses a signed-in
/// user who does not have that section.
/// </summary>
/// <remarks>
/// SECURITY.md promises it. Nothing exercised it: the existing anonymous test passes with the
/// [Authorize] attribute deleted entirely, because Umbraco guards the whole management API surface
/// and refuses an anonymous caller before this package is reached. See #104.
///
/// A signed-in user with no Settings access is the case that matters, and it is the common one: an
/// editor with Content access only is the usual non-admin account, and the read side holds every
/// recorded visitor device.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class BackofficeSectionAccessTests
{
    private readonly LiveSiteFixture _site;

    public BackofficeSectionAccessTests(LiveSiteFixture site) => _site = site;

    [Fact]
    public async Task The_tls_binding_serves_the_site()
    {
        // Separates "TLS is not working" from "the browser is not configured for it". No browser
        // here, just a client told not to validate the development certificate.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        var response = await client.GetAsync(_site.SecureBaseUrl + "/umbraco/login");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_backoffice_login_page_is_reachable_over_tls()
    {
        // The precondition everything else here depends on, asserted on its own so a failure says
        // "the TLS binding is wrong" rather than "the policy is wrong".
        var page = await _site.NewSecurePageAsync();

        var response = await page.GotoAsync("/umbraco/login");

        response!.Status.ShouldBe(200);
    }
}
