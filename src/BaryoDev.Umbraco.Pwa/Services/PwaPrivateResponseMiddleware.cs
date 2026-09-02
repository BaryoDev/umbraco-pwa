using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Pwa.Services;

/// <summary>
/// Marks a response as private when it may have been rendered for one particular visitor, so the
/// service worker declines to cache it.
/// </summary>
/// <remarks>
/// The worker decides what to cache from <c>Cache-Control</c>, and Umbraco sends none. Measured
/// against a real instance, a rendered page comes back with no <c>Cache-Control</c>, no
/// <c>Expires</c> and no <c>Pragma</c> at all, so the worker's check found no directive and stored
/// it. On a site using member protection that put one member's pages in Cache Storage, which is
/// scoped to the origin and the browser profile rather than to the member, and the next person on
/// that device was served them offline.
///
/// Fixed here rather than in the worker on purpose. The worker cannot see a <c>Set-Cookie</c>
/// header, which is a forbidden response-header name, and the cookie store API that would work
/// around it is not in Safari. The server already knows the answer, so the fix belongs where the
/// knowledge is, and the worker needs no change at all.
/// </remarks>
internal sealed class PwaPrivateResponseMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<PwaOptions> _options;

    public PwaPrivateResponseMiddleware(
        RequestDelegate next,
        IOptionsMonitor<PwaOptions> options)
    {
        _next = next;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.CurrentValue.MarkSignedInResponsesPrivate)
        {
            return _next(context);
        }

        // Registered before authentication runs, so the decision is deferred to the point the
        // response actually starts. By then the pipeline has finished and context.User is set.
        context.Response.OnStarting(static state =>
        {
            Apply((HttpContext)state);
            return Task.CompletedTask;
        }, context);

        return _next(context);
    }

    private static void Apply(HttpContext context)
    {
        // Never overrides a site that has already said what it wants. If someone has set
        // Cache-Control deliberately, that decision is theirs and it is already correct input for
        // the worker.
        if (!string.IsNullOrEmpty(context.Response.Headers.CacheControl))
        {
            return;
        }

        if (!IsVisitorSpecific(context))
        {
            return;
        }

        // "private" rather than "no-store". The visitor's own browser cache is welcome to hold
        // this; what must not happen is it landing in Cache Storage, which is shared by everyone
        // who uses the device afterwards. The worker's storable() declines both, and "private" is
        // the accurate one.
        context.Response.Headers.CacheControl = "private";
    }

    private static bool IsVisitorSpecific(HttpContext context)
    {
        // Anyone signed in at all, not members specifically. A response rendered for an
        // authenticated visitor may be personalised whatever scheme signed them in, and a site
        // with its own membership is exactly the case a member-cookie check would miss.
        //
        // Every identity, not ClaimsPrincipal.Identity, which returns the first one only.
        // Umbraco's PreviewAuthenticationMiddleware runs ahead of UseAuthentication and appends
        // the backoffice identity with AddIdentity, so on a preview render the anonymous identity
        // is the one in front and the editor reads as signed out.
        if (context.User?.Identities.Any(i => i.IsAuthenticated) == true)
        {
            return true;
        }

        // There was a second branch here that asked IPublicAccessService whether the path was
        // protected, for the case where nobody is signed in. It never fired. That overload takes
        // a comma-separated content path of the form -1,1055,1060 and was being handed
        // Request.Path, which yields no node ids, so the lookup always failed. Removed rather
        // than repaired: Umbraco redirects an unauthenticated visitor off protected content
        // before this runs, and the check above already covers the member who is signed in.
        return false;
    }
}

/// <summary>
/// Puts <see cref="PwaPrivateResponseMiddleware"/> at the front of the pipeline.
/// </summary>
/// <remarks>
/// <c>IStartupFilter</c> rather than Umbraco's pipeline filters, because this needs nothing
/// Umbraco-specific about its position: it registers a callback for when the response starts and
/// the ordering that matters is that the callback runs after authentication, which it always does.
/// </remarks>
internal sealed class PwaPrivateResponseStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.UseMiddleware<PwaPrivateResponseMiddleware>();
            next(app);
        };
    }
}
