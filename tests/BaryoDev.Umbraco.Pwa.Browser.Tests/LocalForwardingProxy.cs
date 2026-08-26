using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

internal sealed class LocalForwardingProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public bool ForwardingEnabled { get; set; } = true;
    public string Server => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public Task StartAsync()
    {
        _listener.Start();
        _ = AcceptAsync();
        return Task.CompletedTask;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
                _ = ForwardAsync(await _listener.AcceptTcpClientAsync(_stop.Token));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ForwardAsync(TcpClient client)
    {
        using (client)
        await using (var downstream = client.GetStream())
        {
            try
            {
                if (!ForwardingEnabled) return;
                var headerBytes = await ReadHeadersAsync(downstream);
                var header = Encoding.ASCII.GetString(headerBytes);
                var end = header.IndexOf("\r\n", StringComparison.Ordinal);
                if (end < 0) return;
                var requestLine = header[..end].Split(' ', 3);
                if (requestLine.Length != 3 || !Uri.TryCreate(requestLine[1], UriKind.Absolute, out var target)) return;
                if (target.Scheme != Uri.UriSchemeHttp) return;

                using var upstream = new TcpClient();
                await upstream.ConnectAsync(target.Host, target.Port, _stop.Token);
                await using var upstreamStream = upstream.GetStream();
                var rewritten = new StringBuilder()
                    .Append(requestLine[0]).Append(' ')
                    .Append(string.IsNullOrEmpty(target.PathAndQuery) ? "/" : target.PathAndQuery)
                    .Append(' ').Append(requestLine[2]).Append("\r\n");
                foreach (var line in header[(end + 2)..].Split("\r\n"))
                {
                    if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                        rewritten.Append("Host: ").Append(target.Authority).Append("\r\n");
                    else if (line.Length > 0 && !line.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase))
                        rewritten.Append(line).Append("\r\n");
                }
                rewritten.Append("Connection: close\r\n\r\n");
                await upstreamStream.WriteAsync(Encoding.ASCII.GetBytes(rewritten.ToString()), _stop.Token);
                await upstreamStream.CopyToAsync(downstream, _stop.Token);
            }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private static async Task<byte[]> ReadHeadersAsync(Stream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1024];
        while (bytes.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            if (bytes.Count >= 4 && bytes[^4..].SequenceEqual("\r\n\r\n"u8.ToArray())) break;
        }
        return bytes.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _stop.Dispose();
    }
}
