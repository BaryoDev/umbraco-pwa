using System.Net;
using BaryoDev.Umbraco.Pwa.Services;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The guard that decides whether the icon probe is allowed to connect somewhere.
/// </summary>
/// <remarks>
/// The readiness check fetches whatever URL the icon configuration names. Without this guard that
/// is a way to ask the server what it can reach on a network nobody outside it can see, and the
/// reply distinguishes open from closed from filtered.
///
/// Every range is covered here rather than a representative sample, because the failure mode is
/// one missing branch rather than a wrong shape, and one missing branch is the whole hole.
/// </remarks>
public class PublicAddressGuardTests
{
    [Theory]
    // Cloud instance metadata. The single most valuable thing on the far side of a request
    // forgery, and the reason link-local is not merely a tidiness rule.
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    // Loopback.
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    // Private ranges.
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    // Carrier-grade NAT.
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    // This network, IETF assignments, benchmarking.
    [InlineData("0.0.0.0")]
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    // Multicast, reserved and broadcast.
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6 loopback, unspecified, link-local, unique local, multicast.
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("ff02::1")]
    // An IPv4 address wearing an IPv6 costume still arrives at loopback.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    // NAT64's well-known prefix with a private IPv4 embedded in the low 32 bits.
    [InlineData("64:ff9b::169.254.169.254")]
    [InlineData("64:ff9b::10.0.0.1")]
    public void Addresses_only_this_server_can_reach_are_refused(string address)
    {
        PublicAddressGuard.IsPubliclyRoutable(IPAddress.Parse(address)).ShouldBeFalse(
            $"{address} is not on the public internet and the probe must not connect to it");
    }

    [Theory]
    // The control. A guard that refused everything would pass every case above and break the
    // feature, which is the whole point of the readiness check.
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    // Deliberately just outside each blocked range, because an off-by-one in a range check is the
    // likeliest way this goes wrong and it fails open.
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("169.253.255.255")]
    [InlineData("169.255.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("223.255.255.255")]
    // Ordinary public IPv6, including one just outside fc00::/7.
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("fe00::1")]
    // NAT64 prefix carrying a genuinely public IPv4.
    [InlineData("64:ff9b::8.8.8.8")]
    public void Ordinary_public_addresses_are_allowed(string address)
    {
        PublicAddressGuard.IsPubliclyRoutable(IPAddress.Parse(address)).ShouldBeTrue(
            $"{address} is a public address and the readiness check has to be able to reach it");
    }

    [Fact]
    public void A_null_address_is_a_programming_error_rather_than_a_refusal()
    {
        // Returning false would be the quiet option and would hide a caller that lost its address
        // somewhere upstream.
        Should.Throw<ArgumentNullException>(() => PublicAddressGuard.IsPubliclyRoutable(null!));
    }
}
