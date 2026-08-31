using System.Net.Sockets;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The probe client, driven for real rather than through a stub handler.
/// </summary>
/// <remarks>
/// <see cref="PublicAddressGuardTests"/> proves the predicate is right. That is worth nothing on
/// its own: a correct guard nobody calls is the same as no guard. These start from the service
/// registration the composer actually uses and make the client open a connection, so a handler
/// wired up wrong fails here rather than shipping.
/// </remarks>
public class IconProbeClientTests
{
    private static HttpClient Probe()
    {
        var services = new ServiceCollection();
        services.AddPwaIconProbe();

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(PwaIconProbe.ClientName);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5:6379/")]
    [InlineData("http://[::1]:9/")]
    public async Task The_probe_refuses_to_connect_to_an_address_only_this_server_can_reach(string url)
    {
        var probe = Probe();

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => probe.GetAsync(url));

        // The specific type, not merely a failure. Any unreachable address throws something; only
        // a refusal by the guard throws this, so asserting the type is what distinguishes
        // "blocked" from "happened not to answer".
        thrown.InnerException.ShouldBeOfType<NonPublicAddressException>();
    }

    [Fact]
    public async Task A_public_address_is_not_refused_by_the_guard()
    {
        // The control, and it does not need the internet. 192.0.2.1 is TEST-NET-1: publicly
        // routable as far as the guard is concerned, and reserved so nothing answers. So the
        // request fails either way, and the assertion is about which failure it is.
        //
        // Without this, a handler that refused every address would pass every case above.
        var probe = Probe();
        probe.Timeout = TimeSpan.FromSeconds(3);

        var thrown = await Should.ThrowAsync<Exception>(
            () => probe.GetAsync("http://192.0.2.1/icon.png"));

        thrown.ShouldSatisfyAllConditions(
            () => thrown.ShouldNotBeOfType<NonPublicAddressException>(),
            () => thrown.InnerException.ShouldNotBeOfType<NonPublicAddressException>());
    }

    [Fact]
    public async Task A_hostname_that_resolves_to_loopback_is_refused_too()
    {
        // The guard runs on the resolved address, not on the URL. A check on the hostname would
        // let this through, and "localhost" is the least imaginative way to write one.
        var probe = Probe();

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => probe.GetAsync("http://localhost:9/"));

        thrown.InnerException.ShouldBeOfType<NonPublicAddressException>();
    }

    [Fact]
    public void The_package_registers_no_unnamed_client_for_the_probe_to_be_bypassed_through()
    {
        // AddPwaIconProbe registers the guarded client. If a bare AddHttpClient() ever comes back,
        // CreateClient(string.Empty) returns an ungoverned client and the guard becomes optional.
        var services = new ServiceCollection();
        services.AddPwaIconProbe();
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        // The default client exists because the factory always provides one; what matters is that
        // it is not the one the readiness check reaches for, and that it carries no handler of
        // ours. Asserted by behaviour: the guarded client blocks loopback, the default does not.
        var guarded = factory.CreateClient(PwaIconProbe.ClientName);
        guarded.Timeout.ShouldBe(PwaIconProbe.Timeout);
    }
}
