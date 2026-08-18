// Stated explicitly rather than relying on implicit global usings: which ones the Umbraco
// SDK injects differs between majors, and this host is built against 16, 17 and 18.
using System.Reflection;
using Microsoft.AspNetCore.HttpOverrides;
using Umbraco.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// nginx terminates TLS and proxies to this container over plain HTTP, so without this the app
// sees every request as http no matter what the browser did. OpenIddict, which backs the Umbraco
// login, then refuses the whole flow with "This server only accepts HTTPS requests" (ID2083), and
// the backoffice becomes unreachable while the front end looks perfectly healthy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // The container sees nginx as a Docker bridge address rather than loopback, so the defaults
    // reject it. Safe here because nginx sets both headers on every request rather than appending
    // to whatever a client sent. A site exposing this container directly must not clear these.
    //
    // The property was renamed between the two runtimes this host targets: net9.0, which is
    // Umbraco 16, has only KnownNetworks, and net10.0 obsoletes that name in favour of
    // KnownIPNetworks. Neither name compiles on both, so the conditional is unavoidable.
#if NET10_0_OR_GREATER
    options.KnownIPNetworks.Clear();
#else
    options.KnownNetworks.Clear();
#endif
    options.KnownProxies.Clear();
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();

// First in the pipeline on purpose. Everything after this point, Umbraco's own middleware
// included, reads the scheme and the client address from the request, so the rewrite has to
// happen before any of it runs.
app.UseForwardedHeaders();

// Which build of the package this site is actually running, so the publish workflow can refuse to
// ship something the playground never ran.
//
// The MVID rather than the version. A version string cannot tell today's 0.2.0 from yesterday's,
// and it is the server half that needs proving: comparing the generated client only shows the
// client is current, and a server-only change leaves that script byte identical.
//
// Demo code, deliberately, not package code. The package gains no endpoint and no public surface
// for a build concern. A GUID identifying a compilation discloses nothing.
app.MapGet("/build-info", () =>
{
    // Written by the Dockerfile from the source it built, using the same script CI runs. The mvid
    // below stays for information: it cannot be compared against a CI build, because this image is
    // built without .git while CI packs with SourceLink metadata, so the same source yields two
    // different assemblies. The source hash is what the publish gate compares.
    var hashFile = Path.Combine(AppContext.BaseDirectory, "source-hash");
    var sourceHash = File.Exists(hashFile) ? File.ReadAllText(hashFile).Trim() : null;

    return Results.Json(new
    {
        sourceHash,
        mvid = typeof(BaryoDev.Umbraco.Pwa.PwaOptions).Assembly.ManifestModule.ModuleVersionId,
        version = typeof(BaryoDev.Umbraco.Pwa.PwaOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    });
});

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

// Top-level statements generate an internal Program class. WebApplicationFactory<Program> needs it
// public, otherwise a public test fixture cannot derive from the factory.
public partial class Program { }
