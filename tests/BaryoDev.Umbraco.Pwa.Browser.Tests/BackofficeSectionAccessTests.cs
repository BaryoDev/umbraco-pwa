using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Playwright;
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


    private async Task<IPage> SignInAsAdminAsync()
    {
        var page = await _site.NewSecurePageAsync();
        await page.GotoAsync("/umbraco/login");
        await page.FillAsync("input[name=username]", "test@example.com");
        await page.FillAsync("input[name=password]", "LocalOnly-ChangeMe-1234!");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(u => u.Contains("/umbraco/section/"), new() { Timeout = 30000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return page;
    }

    private static async Task ClickInModalAsync(ILocator locator)
    {
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30000 });
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.ClickAsync(new() { Force = true });
    }

    private static async Task SetFieldAsync(ILocator locator, string text)
    {
        await locator.EvaluateAsync(
            @"(el, v) => {
                el.value = v;
                el.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
                el.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
            }", text);
        (await locator.EvaluateAsync<string?>("el => el.value")).ShouldBe(text);
    }

    private static async Task SubmitAsync(ILocator locator)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.DispatchEventAsync("click");
    }

    private async Task<IPage> CreateApiUserAsync(string group, string name, string email)
    {
        var admin = await SignInAsAdminAsync();
        await admin.GotoAsync("/umbraco/section/user-management");
        await admin.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await admin.Locator("uui-menu-item[label='Users']").First.ClickAsync();
        await admin.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex("Test Admin") })
            .First.WaitForAsync(new() { Timeout = 30000 });

        await admin.GetByLabel("Create item for Users").First.ClickAsync();
        await ClickInModalAsync(admin.GetByRole(AriaRole.Button, new() { Name = "API User..." }).First);
        await ClickInModalAsync(admin.GetByRole(AriaRole.Button, new() { Name = "Choose" }).First);
        await ClickInModalAsync(admin.GetByRole(AriaRole.Button,
            new() { NameRegex = new System.Text.RegularExpressions.Regex("^" + group) }).First);
        await ClickInModalAsync(admin.GetByRole(AriaRole.Button, new() { Name = "Choose", Exact = true }).Last);

        await SetFieldAsync(admin.GetByRole(AriaRole.Textbox, new() { Name = "name" }).First, name);
        await SetFieldAsync(admin.GetByRole(AriaRole.Textbox, new() { Name = "email" }).First, email);
        await SubmitAsync(admin.GetByRole(AriaRole.Button, new() { Name = "Create user" }).First);

        await admin.GetByRole(AriaRole.Link, new() { Name = "Go to user profile" })
            .First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30000 });
        await ClickInModalAsync(admin.GetByRole(AriaRole.Link, new() { Name = "Go to user profile" }).First);
        await admin.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return admin;
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("installs")]
    [InlineData("readiness")]
    public async Task A_backoffice_user_without_the_settings_section_is_refused(string endpoint)
    {
        var token = await TokenForGroupAsync("Editors", "editors");

        var status = await CallAsync(endpoint, token);

        status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("installs")]
    [InlineData("readiness")]
    public async Task A_backoffice_user_with_the_settings_section_is_allowed(string endpoint)
    {
        // The control, and it carries as much weight as the refusal. SectionAccessSettings applied
        // to everyone would pass the test above and turn off the dashboard for every site.
        var token = await TokenForGroupAsync("Administrators", "admins");

        var status = await CallAsync(endpoint, token);

        status.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> CallAsync(string endpoint, string token)
    {
        using var http = Client();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_site.SecureBaseUrl}/umbraco/management/api/v1/baryodev/pwa/{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// Creates an API user in <paramref name="group"/> and returns an access token for it.
    /// </summary>
    /// <remarks>
    /// An API user rather than a person, because client credentials is the only grant that can be
    /// driven from a test. The authorization code flow cannot: the code comes back redacted, the
    /// token endpoint then reports that same code missing, and Playwright redacts the Authorization
    /// header in Headers, AllHeadersAsync and from inside the page. See #104.
    ///
    /// The credentials are chosen here rather than read back, so nothing depends on scraping a
    /// secret the dialog says cannot be retrieved again.
    /// </remarks>
    private async Task<string> TokenForGroupAsync(string group, string slug)
    {
        if (Tokens.TryGetValue(slug, out var cached)) return cached;

        var admin = await CreateApiUserAsync(group, $"{slug} api", $"{slug}-api@example.invalid");
        await AddClientCredentialAsync(admin, slug);

        using var http = Client();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId(slug),
            ["client_secret"] = Secret,
        });

        var response = await http.PostAsync(
            $"{_site.SecureBaseUrl}/umbraco/management/api/v1/security/back-office/token", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"no token for {group}: {body}");

        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString();
        token.ShouldNotBeNullOrWhiteSpace();

        Tokens[slug] = token;
        return token;
    }

    private async Task AddClientCredentialAsync(IPage admin, string slug)
    {
        await SubmitAsync(admin.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First);
        await admin.GetByRole(AriaRole.Textbox, new() { Name = "unique" })
            .First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30000 });

        // Umbraco prefixes the id with umbraco-back-office- and only takes the unique half here.
        await SetFieldAsync(admin.GetByRole(AriaRole.Textbox, new() { Name = "unique" }).First, slug);
        await SetFieldAsync(admin.GetByRole(AriaRole.Textbox, new() { Name = "secret" }).First, Secret);
        await SubmitAsync(admin.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).First);

        // The dialog closes on success, which is the only signal it gives.
        await admin.GetByRole(AriaRole.Textbox, new() { Name = "secret" })
            .First.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30000 });
    }

    private static string ClientId(string slug) => $"umbraco-back-office-{slug}";

    private const string Secret = "probe-secret-Aa1!-0123456789abcdef";

    private static readonly Dictionary<string, string> Tokens = [];

    private static HttpClient Client()
    {
        // The development certificate, which nothing in a test run has reason to trust.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}
