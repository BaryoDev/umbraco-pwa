using System.Net;
using System.Net.Sockets;

namespace BaryoDev.Umbraco.Pwa.Services;

/// <summary>
/// Decides whether an address is out on the public internet, or somewhere only this server can
/// reach.
/// </summary>
/// <remarks>
/// The readiness check fetches whatever URL the icon configuration names. Without this, that is a
/// way to ask the server what it can see on a network nobody outside it can: cloud metadata at
/// 169.254.169.254, a database on a private subnet, anything listening on loopback.
///
/// Applied to the resolved address at connect time rather than to the URL. A hostname check is
/// worth very little on its own, because the name can resolve to a private address, and can
/// resolve to a different one on the second lookup than it did on the first.
/// </remarks>
internal static class PublicAddressGuard
{
    /// <summary>
    /// Whether traffic to this address would actually leave for the public internet.
    /// </summary>
    /// <param name="address">The resolved address about to be connected to.</param>
    /// <returns><c>true</c> only for addresses this server has no special access to.</returns>
    internal static bool IsPubliclyRoutable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // An IPv4 address wearing an IPv6 costume still goes to the same place. ::ffff:127.0.0.1
        // is loopback, and checking it as IPv6 would miss that.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),

            // Anything else is not something to guess about.
            _ => false,
        };
    }

    private static bool IsPublicV4(IPAddress address)
    {
        var b = address.GetAddressBytes();

        return (b[0], b[1]) switch
        {
            // 0.0.0.0/8, this network.
            (0, _) => false,

            // 10.0.0.0/8, private.
            (10, _) => false,

            // 100.64.0.0/10, carrier-grade NAT.
            (100, >= 64 and <= 127) => false,

            // 127.0.0.0/8, loopback.
            (127, _) => false,

            // 169.254.0.0/16, link-local. Cloud instance metadata lives here, which is the single
            // most valuable thing on the other side of a request forgery.
            (169, 254) => false,

            // 172.16.0.0/12, private.
            (172, >= 16 and <= 31) => false,

            // 192.0.0.0/24, IETF protocol assignments.
            (192, 0) when b[2] == 0 => false,

            // 192.168.0.0/16, private.
            (192, 168) => false,

            // 198.18.0.0/15, benchmarking.
            (198, 18 or 19) => false,

            // 224.0.0.0/4 multicast and 240.0.0.0/4 reserved, which includes the 255.255.255.255
            // broadcast address.
            ( >= 224, _) => false,

            _ => true,
        };
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IPv6Loopback.Equals(address)) return false;
        if (IPAddress.IPv6Any.Equals(address)) return false;
        if (address.IsIPv6LinkLocal) return false;
        if (address.IsIPv6SiteLocal) return false;
        if (address.IsIPv6Multicast) return false;

        var b = address.GetAddressBytes();

        // fc00::/7, unique local. IsIPv6SiteLocal only covers the deprecated fec0::/10.
        if ((b[0] & 0xFE) == 0xFC) return false;

        // 64:ff9b::/96, the well-known NAT64 prefix. The last four bytes are an embedded IPv4
        // address, so a private one can be reached through it if it is not unwrapped here.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B
            && b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0
            && b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
        {
            return IsPublicV4(new IPAddress(b.AsSpan(12, 4).ToArray()));
        }

        return true;
    }
}
