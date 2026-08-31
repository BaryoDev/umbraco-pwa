using System.Security.Claims;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Shouldly;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The middleware that stops one visitor's pages reaching the next person on the device.
/// </summary>
/// <remarks>
/// Umbraco sends no cache headers on a rendered page, verified against a real instance, so the
/// service worker's <c>storable()</c> found no directive and stored member content in Cache
/// Storage, which is scoped to the browser profile rather than to the member.
///
/// The decision is deferred to <c>Response.OnStarting</c> so it runs after authentication, which
/// means a test has to fire that callback rather than assume it ran. <see cref="FiringResponse"/>
/// exists for that: <c>DefaultHttpContext</c>'s own response feature discards the callback, so a
/// test written without it would pass while asserting nothing.
/// </remarks>
public class PrivateResponseMiddlewareTests
{
    private static async Task<string?> CacheControlAfter(
        bool signedIn = false,
        string path = "/members/dashboard",
        string? existingHeader = null,
        bool optionOn = true,
        params string[] protectedPaths)
    {
        var response = new FiringResponse();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Path = path;

        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "a-member")], "TestScheme"));
        }

        if (existingHeader is not null)
        {
            context.Response.Headers.CacheControl = existingHeader;
        }

        var middleware = new PwaPrivateResponseMiddleware(
            _ => Task.CompletedTask,
            new StaticOptions(new PwaOptions { MarkSignedInResponsesPrivate = optionOn }),
            new StubPublicAccess(protectedPaths));

        await middleware.InvokeAsync(context);
        await response.FireAsync();

        return context.Response.Headers.CacheControl;
    }

    [Fact]
    public async Task A_response_rendered_for_a_signed_in_visitor_is_marked_private()
    {
        (await CacheControlAfter(signedIn: true)).ShouldBe("private");
    }

    [Fact]
    public async Task An_ordinary_anonymous_page_is_left_alone()
    {
        // The control. Marking everything private would pass the test above and turn off offline
        // support for the whole site, which is most of what the package does.
        (await CacheControlAfter(signedIn: false)).ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_protected_path_is_marked_private_even_with_nobody_signed_in()
    {
        (await CacheControlAfter(signedIn: false, path: "/members/dashboard", protectedPaths: "/members"))
            .ShouldBe("private");
    }

    [Fact]
    public async Task A_public_path_on_a_site_that_has_protected_content_is_still_left_alone()
    {
        // A site with one members area must not lose caching everywhere else.
        (await CacheControlAfter(signedIn: false, path: "/news/hello", protectedPaths: "/members"))
            .ShouldBeNullOrEmpty();
    }

    [Theory]
    [InlineData("public, max-age=300")]
    [InlineData("no-store")]
    public async Task A_header_the_site_set_itself_is_never_overridden(string existing)
    {
        (await CacheControlAfter(signedIn: true, existingHeader: existing)).ShouldBe(existing);
    }

    [Fact]
    public async Task The_option_turns_it_off()
    {
        (await CacheControlAfter(signedIn: true, optionOn: false)).ShouldBeNullOrEmpty();
    }

    [Fact]
    public void It_is_on_by_default()
    {
        new PwaOptions().MarkSignedInResponsesPrivate.ShouldBeTrue();
    }

    /// <summary>
    /// A response feature that keeps its OnStarting callbacks and can be told to run them.
    /// </summary>
    private sealed class FiringResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) =>
            _callbacks.Add((callback, state));

        public async Task FireAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private sealed class StaticOptions(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue { get; } = value;
        public PwaOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PwaOptions, string?> listener) => null;
    }

}
