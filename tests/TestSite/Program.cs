// Stated explicitly rather than relying on implicit global usings: which ones the Umbraco
// SDK injects differs between majors, and this host is built against 16, 17 and 18.
using Umbraco.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();


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
