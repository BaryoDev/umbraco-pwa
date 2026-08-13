using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// Boots a real Umbraco against a throwaway SQLite database, once, for the whole suite.
/// </summary>
/// <remarks>
/// Deliberately not a mocked host. The things most likely to break in this package are the parts
/// only a real boot exercises: whether the migration runs and creates the table, whether the
/// composer's DI registrations resolve, and whether the routes are reachable at the URLs the
/// generated client actually calls. A test double would pass while all three were broken.
///
/// The cost is a slow first test (Umbraco cold-boots in roughly 10 to 20 seconds). That is paid
/// once per run because the fixture is shared across every test class in the collection.
/// </remarks>
public class UmbracoSiteFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), $"pwa-tests-{Guid.NewGuid():N}");

    public HttpClient Client { get; private set; } = default!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDirectory);

        // Each run gets its own database file, so tests never inherit rows from the last run and
        // the install-count assertions can be exact rather than "greater than".
        var dbPath = Path.Combine(_dataDirectory, "Umbraco.sqlite.db");

        builder.UseSetting(
            "ConnectionStrings:umbracoDbDSN",
            $"Data Source={dbPath};Cache=Shared;Foreign Keys=True;Pooling=True");
        builder.UseSetting("ConnectionStrings:umbracoDbDSN_ProviderName", "Microsoft.Data.Sqlite");

        builder.UseSetting("Umbraco:CMS:Unattended:InstallUnattended", "true");
        builder.UseSetting("Umbraco:CMS:Unattended:UnattendedUserName", "Test Admin");
        builder.UseSetting("Umbraco:CMS:Unattended:UnattendedUserEmail", "test@example.com");
        builder.UseSetting("Umbraco:CMS:Unattended:UnattendedUserPassword", "LocalOnly-ChangeMe-1234!");

        builder.UseSetting("BaryoDev:Pwa:Manifest:Name", "Fixture Site");
        builder.UseSetting("BaryoDev:Pwa:Manifest:ShortName", "Fixture");
        builder.UseSetting("BaryoDev:Pwa:ServiceWorker:CachePrefix", "fixture");
        builder.UseSetting("BaryoDev:Pwa:ServiceWorker:Version", "test1");

        builder.UseEnvironment("Development");
    }

    public async Task InitializeAsync()
    {
        Client = CreateClient();

        // Force the host to build and the application-starting notification (and therefore the
        // migration) to fire before any test asserts on the schema.
        using var response = await Client.GetAsync("/sw.js");
        response.EnsureSuccessStatusCode();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch
        {
            // A locked SQLite file on a slow CI agent should not fail an otherwise green run.
        }
    }

    /// <summary>Posts a report the way the generated client does.</summary>
    public Task<HttpResponseMessage> ReportAsync(object body) =>
        Client.PostAsJsonAsync("/umbraco/pwa/api/report", body);

    public T Resolve<T>() where T : notnull => Services.GetRequiredService<T>();
}

[CollectionDefinition(Name)]
public class UmbracoCollection : ICollectionFixture<UmbracoSiteFixture>
{
    public const string Name = "umbraco";
}
