using System.Net;
using System.Text;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// The limits on the one anonymous endpoint, measured against a real server.
/// </summary>
/// <remarks>
/// These live here rather than in the integration suite because that suite is built on
/// <c>WebApplicationFactory</c>, which serves over TestServer. TestServer has no Kestrel and never
/// applies <c>MaxRequestBodySize</c>, so <c>[RequestSizeLimit]</c> silently does nothing there: an
/// 8KB body comes back 202 and the cap looks enforced while nothing enforces it.
///
/// This fixture starts the real TestSite as a child process on a loopback port, so the cap that
/// SECURITY.md and THREAT-MODEL.md describe is measured the way a site would meet it.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class ReportEndpointLimitsTests
{
    private const string Report = "/umbraco/pwa/api/report";

    private readonly LiveSiteFixture _site;

    public ReportEndpointLimitsTests(LiveSiteFixture site) => _site = site;

    private static HttpClient Client() => new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The cap the controller sets, and the three documents describe.</summary>
    private const int CapBytes = 4096;

    [Fact]
    public async Task A_body_just_over_the_cap_is_refused_before_it_is_parsed()
    {
        // Just over, not comfortably over. A 64KB body proves the cap is somewhere below 64KB,
        // which would still pass if it had drifted to 32KB, and the drift is the thing worth
        // catching: the constant and the documents have to agree.
        using var client = Client();
        var body = Envelope(new string('a', CapBytes));
        Encoding.UTF8.GetByteCount(body).ShouldBeGreaterThan(CapBytes);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(_site.BaseUrl + Report, content);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private static string Envelope(string deviceId) =>
        "{\"deviceId\":\"" + deviceId + "\",\"platform\":\"web\"}";

    [Fact]
    public async Task A_body_inside_the_cap_is_still_accepted()
    {
        // The control. A cap that refuses everything would pass the test above and turn off the
        // only write path the package has.
        using var client = Client();
        var body = "{\"deviceId\":\"probe-" + Guid.NewGuid().ToString("N")
                   + "\",\"platform\":\"web\",\"displayMode\":\"standalone\"}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(_site.BaseUrl + Report, content);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }
}
