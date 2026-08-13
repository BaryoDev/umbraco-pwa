using System.Text.Json;
using BaryoDev.Umbraco.Pwa.Services;
using Shouldly;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// What has to be true for "add the package and start the site" to be the whole installation.
/// These are the assertions that catch a broken package before a user does.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class InstallationTests
{
    private readonly UmbracoSiteFixture _site;

    public InstallationTests(UmbracoSiteFixture site) => _site = site;

    [Fact]
    public void The_migration_created_the_table_on_first_start()
    {
        var scopeProvider = _site.Resolve<IScopeProvider>();

        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var count = scope.Database.ExecuteScalar<int>(
            "select count(*) from sqlite_master where type='table' and name='BaryoDevPwaInstall'");

        count.ShouldBe(1);
    }

    [Fact]
    public void The_device_id_index_is_unique()
    {
        // The whole dedupe strategy rests on this. Without it, concurrent first launches from one
        // browser would insert two rows and every count would drift upward forever.
        var scopeProvider = _site.Resolve<IScopeProvider>();

        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.Database.ExecuteScalar<string>(
            "select sql from sqlite_master where type='index' and name='IX_BaryoDevPwaInstall_deviceId'");

        sql.ShouldNotBeNull();
        sql!.ShouldContain("UNIQUE");
    }

    [Fact]
    public void Restarting_does_not_run_the_migration_again()
    {
        // Umbraco records the state each site reached. The fixture has already started once, so
        // the plan being at its final state is the evidence a second start would be a no-op.
        var scopeProvider = _site.Resolve<IScopeProvider>();

        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var state = scope.Database.ExecuteScalar<string>(
            "select value from umbracoKeyValue where key = 'Umbraco.Core.Upgrader.State+BaryoDevPwa'");

        state.ShouldBe("baryodev-pwa-init");
    }

    [Fact]
    public void The_services_resolve_from_the_container()
    {
        // A composer that registers nothing fails at the first request, not at startup.
        _site.Resolve<IPwaInstallService>().ShouldNotBeNull();
        _site.Resolve<IPwaAssetGenerator>().ShouldNotBeNull();
    }

    [Fact]
    public async Task The_backoffice_manifest_is_served_at_the_path_Umbraco_scans()
    {
        // Asserting on a file path would be wrong here: static web assets from a Razor Class
        // Library are never copied into the host's output, they are served through a manifest.
        // The only thing that matters is whether the backoffice can fetch this URL, because that
        // is literally how it discovers the dashboard.
        var response = await _site.Client.GetAsync("/App_Plugins/BaryoDev.Pwa/umbraco-package.json");

        response.IsSuccessStatusCode.ShouldBeTrue(
            $"the manifest must be reachable, got {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var extension = doc.RootElement.GetProperty("extensions")[0];

        extension.GetProperty("type").GetString().ShouldBe("dashboard");
        extension.GetProperty("element").GetString()
            .ShouldBe("/App_Plugins/BaryoDev.Pwa/dashboard.js");
    }

    [Fact]
    public async Task The_dashboard_element_the_manifest_points_at_is_served()
    {
        // A manifest pointing at a missing file is the classic way a backoffice extension fails:
        // the tab renders blank with only a console error, which nothing in CI would catch.
        var manifest = await _site.Client.GetStringAsync("/App_Plugins/BaryoDev.Pwa/umbraco-package.json");
        using var doc = JsonDocument.Parse(manifest);
        var element = doc.RootElement.GetProperty("extensions")[0].GetProperty("element").GetString()!;

        var response = await _site.Client.GetAsync(element);

        response.IsSuccessStatusCode.ShouldBeTrue($"{element} must be served");
    }

    [Fact]
    public async Task The_dashboard_escapes_every_value_it_renders()
    {
        // deviceId arrives on an anonymous endpoint and is rendered in an administrator's
        // browser, so this is a stored-XSS path. The server whitelists on the way in; this is
        // the other half of the job, and a regression here would be silent.
        var source = await _site.Client.GetStringAsync("/App_Plugins/BaryoDev.Pwa/dashboard.js");

        source.ShouldContain("escapeHtml(shortId(r.deviceId))");
        source.ShouldContain("escapeHtml(r.platform");
        source.ShouldContain("escapeHtml(r.displayMode)");
    }
}
