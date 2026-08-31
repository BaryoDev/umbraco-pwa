using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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

    public PwaReportRateLimitFilter(IOptionsMonitor<PwaOptions> options)
    {
        _options = options;

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
            var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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
            return;
        }

        await next();
    }

    public void Dispose() => _limiter.Dispose();
}
