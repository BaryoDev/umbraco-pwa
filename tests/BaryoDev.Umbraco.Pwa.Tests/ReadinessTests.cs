using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The installability preflight.
/// </summary>
/// <remarks>
/// This feature exists because of a failure found while deploying this package's own demo: the
/// manifest pointed at icons that returned 404, so Chrome silently declined to offer installation.
/// Nothing errored and nothing logged. These tests pin the checks that would have caught it.
/// </remarks>
[Collection(UmbracoCollection.Name)]
public class ReadinessTests
{
    private readonly UmbracoSiteFixture _site;

    public ReadinessTests(UmbracoSiteFixture site) => _site = site;

    private async Task<PwaReadiness> Check()
    {
        var service = _site.Resolve<IPwaReadinessService>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        return await service.CheckAsync(context.Request);
    }

    /// <summary>Runs the check with one icon swapped in, leaving the rest of the config alone.</summary>
    private async Task<PwaReadiness> CheckWith(PwaIcon icon)
    {
        var options = _site.Resolve<IOptionsMonitor<PwaOptions>>().CurrentValue;
        var original = options.Manifest.Icons.ToList();

        try
        {
            options.Manifest.Icons.RemoveAll(i => i.Sizes == icon.Sizes && i.Purpose is null);
            options.Manifest.Icons.Add(icon);
            return await Check();
        }
        finally
        {
            options.Manifest.Icons.Clear();
            options.Manifest.Icons.AddRange(original);
        }
    }

    private async Task<PwaCheck> MemberCheck(
        bool hasProtectedContent,
        bool markPrivate = true,
        params string[] skipPaths)
    {
        var options = new PwaOptions
        {
            MarkSignedInResponsesPrivate = markPrivate,
            Manifest = new PwaManifestOptions { Name = "Member check", StartUrl = "/demo.html" },
        };

        if (skipPaths.Length > 0)
        {
            options.ServiceWorker.SkipPaths = [.. skipPaths];
        }

        var service = new PwaReadinessService(
            new StaticOptionsMonitor(options),
            new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))),
            _site.Resolve<IWebHostEnvironment>(),
            _site.Resolve<IDocumentUrlService>(),
            hasProtectedContent ? new StubPublicAccess("/members") : new StubPublicAccess());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");

        var readiness = await service.CheckAsync(context.Request);
        return readiness.Checks.Single(c => c.Name == "Member content stays out of the cache");
    }

    [Fact]
    public async Task A_site_that_turned_the_protection_off_and_excluded_nothing_is_warned()
    {
        var check = await MemberCheck(hasProtectedContent: true, markPrivate: false);

        check.Passed.ShouldBeFalse();
        check.Advisory.ShouldBeTrue("a warning, not something that blocks installation");
        check.Detail.ShouldContain("SkipPaths");
    }

    [Fact]
    public async Task A_site_with_the_protection_on_is_not_warned()
    {
        // The default. A check that nags a correctly configured site is a check people learn to
        // ignore, which costs more than it saves.
        (await MemberCheck(hasProtectedContent: true, markPrivate: true)).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_site_that_excluded_its_member_paths_by_hand_is_not_warned()
    {
        (await MemberCheck(hasProtectedContent: true, markPrivate: false, "/umbraco/", "/members/"))
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_site_with_no_protected_content_is_never_warned()
    {
        (await MemberCheck(hasProtectedContent: false, markPrivate: false)).Passed.ShouldBeTrue();
    }

    private async Task<PwaReadiness> CheckRemoteWith(
        HttpMessageHandler handler,
        CancellationToken ct = default)
    {
        var options = new PwaOptions
        {
            Manifest = new PwaManifestOptions
            {
                Name = "Remote icon test",
                StartUrl = "/demo.html",
                Icons =
                [
                    new PwaIcon { Src = "https://cdn.example.test/icon.png", Sizes = "192x192" },
                    new PwaIcon { Src = "/icon-512.png", Sizes = "512x512" },
                ],
            },
        };

        var service = new PwaReadinessService(
            new StaticOptionsMonitor(options),
            new StubHttpClientFactory(handler),
            _site.Resolve<IWebHostEnvironment>(),
            _site.Resolve<IDocumentUrlService>(),
            new StubPublicAccess());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");

        return await service.CheckAsync(context.Request, ct);
    }

    private sealed class StaticOptionsMonitor(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue { get; } = value;

        public PwaOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<PwaOptions, string?> listener) => null;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(
        Func<CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(cancellationToken));
    }

    [Fact]
    public async Task A_remote_icon_with_a_non_success_status_reports_the_http_status()
    {
        var readiness = await CheckRemoteWith(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)));

        var check = readiness.Checks.Single(c => c.Name == "Icon 192x192");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("HTTP 503");
    }

    [Fact]
    public async Task A_remote_icon_served_with_a_non_image_content_type_is_rejected()
    {
        var readiness = await CheckRemoteWith(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("not an image")
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") },
                },
            }));

        var check = readiness.Checks.Single(c => c.Name == "Icon 192x192");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("served as text/plain, not an image");
    }

    [Fact]
    public async Task A_remote_icon_request_exception_becomes_a_failed_check_without_leaking_the_exception()
    {
        // This used to interpolate ex.Message straight into the detail, which is rendered in the
        // backoffice and written to the application log. HttpClient messages name hosts, ports and
        // TLS particulars, so a probe of an internal address answered with a description of it.
        var readiness = await CheckRemoteWith(new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("connection refused to 10.0.0.5:6379")));

        var check = readiness.Checks.Single(c => c.Name == "Icon 192x192");
        check.Passed.ShouldBeFalse();

        // The site owner is told what to do about it.
        check.Detail.ShouldContain("could not be reached");

        // And nothing about what the server saw. Asserted piece by piece: a single check on the
        // whole message would still pass if only the host survived.
        check.Detail.ShouldNotContain("InvalidOperationException");
        check.Detail.ShouldNotContain("connection refused");
        check.Detail.ShouldNotContain("10.0.0.5");
        check.Detail.ShouldNotContain("6379");
    }

    [Fact]
    public async Task Cancellation_of_a_remote_icon_request_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var serviceTask = CheckRemoteWith(
            new StubHttpMessageHandler(ct => throw new OperationCanceledException(ct)),
            cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => serviceTask);
    }


    /// <summary>Runs the check with StartUrl swapped in, leaving the rest of the config alone.</summary>
    private async Task<PwaReadiness> CheckWithStartUrl(string startUrl)
    {
        var options = _site.Resolve<IOptionsMonitor<PwaOptions>>().CurrentValue;
        var original = options.Manifest.StartUrl;

        try
        {
            options.Manifest.StartUrl = startUrl;
            return await Check();
        }
        finally
        {
            options.Manifest.StartUrl = original;
        }
    }

    [Fact]
    public async Task An_icon_that_does_not_exist_is_reported_rather_than_ignored()
    {
        // The scenario this whole feature exists for: an icon is configured, so nothing looks
        // wrong, but the file is not there and the browser silently declines to install.
        //
        // Written against a deliberately missing path rather than against "no icons configured".
        // An earlier version relied on the fixture having none, and it passed for the wrong
        // reason: the icons WERE inherited from appsettings, and the check was failing on a bug
        // where a leading slash parsed as a file:// URI. Fixing the bug turned the test red,
        // which is the only reason it was noticed.
        var readiness = await CheckWith(new PwaIcon { Src = "/definitely-not-here.png", Sizes = "192x192" });

        readiness.Installable.ShouldBeFalse();

        var check = readiness.Checks.Single(c => c.Name == "Icon 192x192");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("definitely-not-here.png");
    }

    [Fact]
    public async Task A_configured_icon_that_exists_passes()
    {
        // The other half. Without this, the check could report everything as broken and still
        // look like it was working.
        var readiness = await Check();

        readiness.Checks.Single(c => c.Name == "Icon 192x192").Passed
            .ShouldBeTrue("the demo site ships this icon under wwwroot");
    }

    [Fact]
    public async Task A_site_relative_path_is_not_mistaken_for_a_file_uri()
    {
        // On Linux and macOS, Uri.TryCreate("/icon.png", UriKind.Absolute, ...) succeeds as a
        // file:// URI. Treating that as remote sends every ordinary icon down the HTTP branch,
        // where it dies with "the 'file' scheme is not supported". Platform-specific, and only on
        // the platforms this actually ships to.
        var readiness = await Check();

        foreach (var check in readiness.Checks.Where(c => c.Name.StartsWith("Icon")))
        {
            check.Detail.ShouldNotContain("file", Case.Insensitive,
                "a site-relative icon must never be resolved as a file:// URI");
        }
    }

    [Fact]
    public async Task Every_failing_check_explains_what_to_do()
    {
        // A check that says "failed" and nothing else just moves the mystery.
        var readiness = await Check();

        foreach (var check in readiness.Checks.Where(c => !c.Passed))
        {
            check.Detail.ShouldNotBeNullOrWhiteSpace($"{check.Name} must explain itself");
            check.Detail.Length.ShouldBeGreaterThan(20, $"{check.Name} needs a usable explanation");
        }
    }

    [Fact]
    public async Task A_configured_name_passes()
    {
        var readiness = await Check();

        readiness.Checks.ShouldContain(c => c.Name == "Manifest has a name" && c.Passed);
    }

    [Fact]
    public async Task Https_is_checked_because_a_service_worker_will_not_register_without_it()
    {
        var readiness = await Check();

        readiness.Checks.ShouldContain(c => c.Name == "Served over HTTPS" && c.Passed);
    }

    [Fact]
    public async Task Localhost_counts_as_secure()
    {
        // Otherwise every local development setup would report itself as broken.
        var service = _site.Resolve<IPwaReadinessService>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);

        var readiness = await service.CheckAsync(context.Request);

        readiness.Checks.Single(c => c.Name == "Served over HTTPS").Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task Advisory_checks_do_not_block_installability()
    {
        // A missing maskable icon is a cosmetic problem on Android, not a reason to report the
        // site as broken.
        var readiness = await Check();

        var maskable = readiness.Checks.Single(c => c.Name == "Maskable icon");
        maskable.Advisory.ShouldBeTrue();
    }


    [Fact]
    public async Task A_start_url_at_an_empty_site_root_is_reported()
    {
        // Found on a real iPhone. Add to Home Screen worked, every check was green, and the
        // installed app opened on Umbraco's "your website doesn't contain any published content
        // yet" placeholder. StartUrl defaults to "/", and nothing is published there.
        //
        // This fails worse than the missing icon that created this feature. A missing icon is
        // loud: Chrome refuses to install and you notice. A bad start URL is silent. It installs,
        // the icon is right, and the failure only appears the first time somebody taps it.
        var readiness = await CheckWithStartUrl("/");

        var check = readiness.Checks.Single(c => c.Name == "Start URL has content");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("published", Case.Insensitive);
        readiness.Installable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_start_url_pointing_at_a_real_page_passes()
    {
        // The other half. The test site serves demo.html from wwwroot, which is exactly the fix
        // the deployed demo needed.
        var readiness = await CheckWithStartUrl("/demo.html");

        readiness.Checks.Single(c => c.Name == "Start URL has content").Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_start_url_that_points_nowhere_is_reported()
    {
        var readiness = await CheckWithStartUrl("/no-such-page.html");

        var check = readiness.Checks.Single(c => c.Name == "Start URL has content");
        check.Passed.ShouldBeFalse();
        check.Detail.ShouldContain("no-such-page.html");
    }

    [Fact]
    public async Task The_readiness_endpoint_is_backoffice_only()
    {
        // It reports configuration details, so it is not for anonymous callers.
        var response = await _site.Client.GetAsync(
            "/umbraco/management/api/v1/baryodev/pwa/readiness");

        response.StatusCode.ShouldBeOneOf(
            System.Net.HttpStatusCode.Unauthorized, System.Net.HttpStatusCode.Forbidden);
    }
}
