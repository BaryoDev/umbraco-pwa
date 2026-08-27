using System.Net;
using BaryoDev.Umbraco.Pwa.Controllers;
using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Persistence;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shouldly;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Pwa.Tests;

[Collection(UmbracoCollection.Name)]
public class OptionSwitchTests
{
    private readonly UmbracoSiteFixture _site;

    public OptionSwitchTests(UmbracoSiteFixture site) => _site = site;

    [Fact]
    public void ServeAssets_false_returns_not_found_for_each_generated_asset()
    {
        var options = new StaticOptionsMonitor(new PwaOptions { ServeAssets = false });
        var controller = new PwaAssetsController(options, new PwaAssetGenerator(options));

        controller.Manifest().ShouldBeOfType<NotFoundResult>();
        controller.ServiceWorker().ShouldBeOfType<NotFoundResult>();
        controller.Client().ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task TrackInstalls_false_returns_202_and_persists_nothing()
    {
        var id = $"disabled-{Guid.NewGuid():N}";
        var service = new PwaInstallService(
            _site.Resolve<IScopeProvider>(),
            new StaticOptionsMonitor(new PwaOptions { TrackInstalls = false }));

        await service.ReportAsync(new PwaReportRequest
        {
            DeviceId = id,
            DisplayMode = "standalone",
            Platform = "android",
            Installed = true,
        });

        var response = await new PwaReportController(new NoOpInstallService()).Report(
            new PwaReportRequest { DeviceId = id, DisplayMode = "standalone", Platform = "android", Installed = true },
            default);
        response.ShouldBeOfType<AcceptedResult>().StatusCode.ShouldBe((int)HttpStatusCode.Accepted);
        (await _site.Resolve<IPwaInstallService>().GetAllAsync(false)).ShouldNotContain(x => x.DeviceId == id);
    }

    [Fact]
    public async Task TrackInstalledOnly_ignores_browser_reports_but_stores_installed_reports()
    {
        var browserId = $"browser-only-{Guid.NewGuid():N}";
        var installedId = $"installed-only-{Guid.NewGuid():N}";
        var service = new PwaInstallService(
            _site.Resolve<IScopeProvider>(),
            new StaticOptionsMonitor(new PwaOptions { TrackInstalledOnly = true }));

        await service.ReportAsync(new PwaReportRequest
        {
            DeviceId = browserId, DisplayMode = "browser", Platform = "android", Installed = false,
        });
        await service.ReportAsync(new PwaReportRequest
        {
            DeviceId = installedId, DisplayMode = "standalone", Platform = "android", Installed = true,
        });

        var rows = await _site.Resolve<IPwaInstallService>().GetAllAsync(false);
        rows.ShouldNotContain(x => x.DeviceId == browserId);
        rows.ShouldContain(x => x.DeviceId == installedId && x.Installed);
    }

    private sealed class StaticOptionsMonitor(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue { get; } = value;
        public PwaOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PwaOptions, string?> listener) => null;
    }

    private sealed class NoOpInstallService : IPwaInstallService
    {
        public Task ReportAsync(PwaReportRequest report, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PwaInstallModel>> GetAllAsync(bool installedOnly, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PwaInstallModel>>([]);
        public Task<PwaInstallSummary> GetSummaryAsync(CancellationToken ct = default) =>
            Task.FromResult(new PwaInstallSummary());
    }
}
