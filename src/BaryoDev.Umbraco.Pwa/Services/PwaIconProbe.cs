using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace BaryoDev.Umbraco.Pwa.Services;

/// <summary>
/// The one HTTP client this package uses, and the only outbound traffic it produces.
/// </summary>
/// <remarks>
/// The readiness check probes a configured icon URL to answer "why is my site not offering to
/// install?", and that URL is whatever the site owner wrote in configuration. Left ungoverned it
/// is a server-side request forgery: a way to have the server reach addresses the caller cannot,
/// and report back what it found.
///
/// The guard lives in the connect callback rather than in a check on the URL. That is deliberate.
/// A URL check happens once, before a name is resolved; the callback runs for every connection the
/// handler actually opens, which means it also covers each hop of a redirect chain and cannot be
/// walked past by a name that resolves to one address when it is validated and another when it is
/// dialled.
/// </remarks>
internal static class PwaIconProbe
{
    /// <summary>The named client. Nothing else in the package makes outbound requests.</summary>
    internal const string ClientName = "BaryoDev.Umbraco.Pwa.IconProbe";

    /// <summary>A readiness check must never be the slowest thing on a page.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Enough for a CDN's canonical redirect, not enough to be walked around a network.</summary>
    private const int MaxRedirects = 3;

    /// <summary>
    /// Registers the probe client with a handler that refuses to connect anywhere except the
    /// public internet.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    internal static void AddPwaIconProbe(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client => client.Timeout = Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = MaxRedirects,

                // Every hop opens a connection through here, so every hop is checked.
                ConnectCallback = ConnectToPublicAddressOnly,
            });
    }

    private static async ValueTask<Stream> ConnectToPublicAddressOnly(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;

        var candidates = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        // The address that passed the check is the address dialled below. Resolving here and
        // connecting to the name would leave a window where the second lookup returns something
        // else, which is the whole trick behind DNS rebinding.
        var allowed = Array.Find(candidates, PublicAddressGuard.IsPubliclyRoutable)
            ?? throw new NonPublicAddressException(
                $"{host} resolves only to addresses that are not on the public internet.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(allowed, context.DnsEndPoint.Port), ct)
                .ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The probe refused to connect because the destination is not on the public internet.
/// </summary>
/// <remarks>
/// A distinct type so the readiness check can say something useful about this case without
/// reaching into exception messages, which is what it used to do for every failure.
/// </remarks>
internal sealed class NonPublicAddressException : Exception
{
    internal NonPublicAddressException(string message) : base(message)
    {
    }
}
