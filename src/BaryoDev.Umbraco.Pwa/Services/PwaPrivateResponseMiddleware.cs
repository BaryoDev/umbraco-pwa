using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

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
    private readonly IPublicAccessService _publicAccess;

    public PwaPrivateResponseMiddleware(
        RequestDelegate next,
        IOptionsMonitor<PwaOptions> options,
        IPublicAccessService publicAccess)
    {
        _next = next;
        _options = options;
        _publicAccess = publicAccess;
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
            var (ctx, publicAccess) = ((HttpContext, IPublicAccessService))state;
            Apply(ctx, publicAccess);
            return Task.CompletedTask;
        }, (context, _publicAccess));

        return _next(context);
    }

    private static void Apply(HttpContext context, IPublicAccessService publicAccess)
    {
        // Never overrides a site that has already said what it wants. If someone has set
        // Cache-Control deliberately, that decision is theirs and it is already correct input for
        // the worker.
        if (!string.IsNullOrEmpty(context.Response.Headers.CacheControl))
        {
            return;
        }

        if (!IsVisitorSpecific(context, publicAccess))
        {
            return;
        }

        // "private" rather than "no-store". The visitor's own browser cache is welcome to hold
        // this; what must not happen is it landing in Cache Storage, which is shared by everyone
        // who uses the device afterwards. The worker's storable() declines both, and "private" is
        // the accurate one.
        context.Response.Headers.CacheControl = "private";
    }

    private static bool IsVisitorSpecific(HttpContext context, IPublicAccessService publicAccess)
    {
        // Anyone signed in at all, not members specifically. A response rendered for an
        // authenticated visitor may be personalised whatever scheme signed them in, and a site
        // with its own membership is exactly the case a member-cookie check would miss.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        // The path is protected even though nobody is signed in. Umbraco normally redirects to a
        // login page before this, so it is the misconfiguration case rather than the common one,
        // and it is cheap to be sure about.
        //
        // GetAll is served from Umbraco's cache and is empty on most sites, so this costs a list
        // check on the ordinary request rather than a lookup.
        try
        {
            if (!publicAccess.GetAll().Any())
            {
                return false;
            }

            return publicAccess.IsProtected(context.Request.Path.Value ?? "/").Success;
        }
        catch
        {
            // A readiness answer is not worth failing a page render over. Erring towards not
            // marking is right here: the signed-in branch above is what covers the real case, and
            // this one is the belt to its braces.
            return false;
        }
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
