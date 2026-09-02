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
    [Fact]
    public async Task An_administrator_can_sign_in_to_the_backoffice()
    {
        // The furthest #104 has got: a real backoffice session in a real browser, over the TLS
        // binding OpenIddict requires. What remains is reading that session's token, which three
        // separate mechanisms redact. See the issue for what was measured.
        var page = await _site.NewSecurePageAsync();

        await page.GotoAsync("/umbraco/login");
        await page.FillAsync("input[name=username]", "test@example.com");
        await page.FillAsync("input[name=password]", "LocalOnly-ChangeMe-1234!");
        await page.Keyboard.PressAsync("Enter");
        // Two waits, because they are two different things. The first is the server letting go of
        // the login route; the second is the shell routing client-side to a section, which is what
        // actually demonstrates the session resolved to a user with somewhere to go.
        await page.WaitForURLAsync(u => !u.Contains("/login"), new() { Timeout = 30000 });
        await page.WaitForURLAsync(u => u.Contains("/umbraco/section/"), new() { Timeout = 30000 });

        page.Url.ShouldContain("/umbraco/section/");
    }

}
