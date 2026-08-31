using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// The request size limit on the anonymous report endpoint, against a real host.
/// </summary>
/// <remarks>
/// This lives here rather than beside the other report tests for the same reason the service
/// worker tests do. <c>[RequestSizeLimit]</c> is enforced by the server through
/// <c>IHttpMaxRequestBodySizeFeature</c>, and <c>WebApplicationFactory</c>'s in-memory host does
/// not apply it: the oversized body sails through and the test passes while proving nothing.
///
/// The fixture here is a real Umbraco on a real socket, which is the only place the limit can
/// actually be observed.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class ReportEndpointLimitTests
{
    private readonly LiveSiteFixture _site;

    public ReportEndpointLimitTests(LiveSiteFixture site) => _site = site;

    private static HttpClient Client() => new() { Timeout = TimeSpan.FromSeconds(30) };

    private static object Report(string deviceId) =>
        new { deviceId, displayMode = "standalone", platform = "android", installed = true };

    [Fact]
    public async Task An_oversized_body_is_refused_rather_than_materialised()
    {
        // deviceId is cut to 100 characters, but only after the JSON has been parsed. Without a
        // limit on the request itself, an anonymous caller can have the server read as much as the
        // host allows before anything gets the chance to reject it.
        using var client = Client();

        var response = await client.PostAsJsonAsync(
            $"{_site.BaseUrl}/umbraco/pwa/api/report",
            Report(new string('a', 64 * 1024)));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task An_ordinary_report_is_still_accepted()
    {
        // The control. A limit set too low, or a route that stopped working, would pass the test
        // above while breaking every install report the package exists to collect.
        using var client = Client();

        var response = await client.PostAsJsonAsync(
            $"{_site.BaseUrl}/umbraco/pwa/api/report",
            Report($"limit-control-{Guid.NewGuid():N}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }
}
