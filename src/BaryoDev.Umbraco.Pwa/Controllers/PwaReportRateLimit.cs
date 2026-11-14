using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Pwa.Controllers;

/// <summary>
/// Bounds how many install reports one caller can have stored.
/// </summary>
/// <remarks>
/// The report endpoint has to be anonymous, and it inserts a row for each novel
/// <c>deviceId</c>, which the client generates. A loop with fresh ids inserts rows without limit.
///
/// An action filter rather than the rate limiting middleware, because this package is a composer
/// and adding middleware means reaching into the host's pipeline for one endpoint. The limiter
/// itself is <c>System.Threading.RateLimiting</c> rather than a dictionary of counters: a
/// hand-rolled one keyed by address is itself an unbounded allocation, which is the bug being
/// fixed rather than a fix for it.
/// </remarks>
internal sealed class PwaReportRateLimitFilter : IAsyncActionFilter, IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;
    private readonly IOptionsMonitor<PwaOptions> _options;
    private readonly ILogger<PwaReportRateLimitFilter> _logger;

    // Ticks of the last warning, so a flood logs once per window instead of once per request.
    private long _lastWarnedAt = long.MinValue;

    private static readonly long WindowTicks = (long)(TimeSpan.FromMinutes(1).TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

    public PwaReportRateLimitFilter(IOptionsMonitor<PwaOptions> options, ILogger<PwaReportRateLimitFilter> logger)
    {
        _options = options;
        _logger = logger;

        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var permits = options.CurrentValue.MaxReportsPerMinute;

            if (permits <= 0)
            {
                return RateLimitPartition.GetNoLimiter("off");
            }

            // The address partitions the limiter and is not stored, logged or returned. A caller
            // with no remote address, which happens in some hosting shapes, shares one partition
            // rather than escaping the limit.
            var key = PartitionKey(context.Connection.RemoteIpAddress);

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),

                // No queue. A report is best effort and the caller is told nothing either way, so
                // holding a request open to admit it later buys nothing and costs a connection.
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
        });
    }

    /// <summary>
    /// The limiter partition for a caller. IPv4 is the full address. IPv6 is the /64 prefix,
    /// because one subscriber is usually handed a whole /64 and can pick any address in it, so a
    /// per-address budget would give them 2^64 budgets. An IPv4-mapped IPv6 address is the same
    /// client as its IPv4 form and shares that budget.
    /// </summary>
    internal static string PartitionKey(IPAddress? address)
    {
        if (address is null) return "unknown";

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out _)) return address.ToString();

        return "v6/64:" + Convert.ToHexString(bytes[..8]);
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (_options.CurrentValue.MaxReportsPerMinute <= 0)
        {
            await next();
            return;
        }

        using var lease = await _limiter.AcquireAsync(context.HttpContext, 1, context.HttpContext.RequestAborted);

        if (!lease.IsAcquired)
        {
            // 202, not 429. The endpoint has one answer for everything on purpose: a
            // distinguishable response would let an anonymous caller learn something, and here it
            // would also tell them exactly where the limit is and when it resets. Dropping the
            // report is correct. Saying so is not.
            context.Result = new AcceptedResult();
            WarnOncePerWindow();
            return;
        }

        await next();
    }

    // Operators see the drop here, not the caller. Nothing identifying goes in the message: the
    // address partitions the limiter and is never logged.
    private void WarnOncePerWindow()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastWarnedAt);

        if (last != long.MinValue && now - last < WindowTicks) return;
        if (Interlocked.CompareExchange(ref _lastWarnedAt, now, last) != last) return;

        _logger.LogWarning(
            "PWA install reports are being dropped by the per-address limit of {Limit} a minute. "
            + "Behind a reverse proxy or CDN without forwarded headers every visitor shares one address, "
            + "so ordinary traffic can hit it. Configure forwarded headers or raise MaxReportsPerMinute.",
            _options.CurrentValue.MaxReportsPerMinute);
    }

    public void Dispose() => _limiter.Dispose();
}
