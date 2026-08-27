using BaryoDev.Umbraco.Pwa.Persistence;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.Options;
using Shouldly;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Pwa.Tests;

[Collection(UmbracoCollection.Name)]
public class PwaInstallRetentionTests
{
    private readonly UmbracoSiteFixture _site;

    public PwaInstallRetentionTests(UmbracoSiteFixture site) => _site = site;

    [Fact]
    public async Task Deletes_rows_older_than_the_configured_window()
    {
        await Insert("retention-old", DateTime.UtcNow.AddDays(-31));

        await RunJob(30);

        (await Find("retention-old")).ShouldBeNull();
    }

    [Fact]
    public async Task Preserves_rows_inside_the_configured_window()
    {
        await Insert("retention-recent", DateTime.UtcNow.AddDays(-29));

        await RunJob(30);

        (await Find("retention-recent")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Zero_retention_keeps_even_old_rows()
    {
        await Insert("retention-forever", DateTime.UtcNow.AddYears(-10));

        await RunJob(0);

        (await Find("retention-forever")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Empty_table_is_harmless()
    {
        var deviceId = "retention-empty";
        await RunJob(30);

        (await Find(deviceId)).ShouldBeNull();
    }

    private Task RunJob(int retentionDays)
    {
        var options = new TestOptionsMonitor(new PwaOptions {RetentionDays = retentionDays});
        var job = new PwaInstallRetentionJob(
            _site.Resolve<IScopeProvider>(),
            options);
        return job.RunForTestsAsync();
    }

    private Task Insert(string deviceId, DateTime lastSeenAt)
    {
        using var scope = _site.Resolve<IScopeProvider>().CreateScope();
        var row = new PwaInstallDto
        {
            DeviceId = deviceId,
            DisplayMode = "browser",
            FirstSeenAt = lastSeenAt,
            LastSeenAt = lastSeenAt,
            LaunchCount = 1,
        };
        scope.Database.Insert(row);
        scope.Complete();
        return Task.CompletedTask;
    }

    private Task<PwaInstallDto?> Find(string deviceId)
    {
        using var scope = _site.Resolve<IScopeProvider>().CreateScope(autoComplete: true);
        return Task.FromResult<PwaInstallDto?>(scope.Database.FirstOrDefault<PwaInstallDto>(
            scope.SqlContext.Sql().Select<PwaInstallDto>().From<PwaInstallDto>()
                .Where<PwaInstallDto>(x => x.DeviceId == deviceId)));
    }

    private sealed class TestOptionsMonitor(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue => value;

        public PwaOptions Get(string? name) => value;

        public IDisposable OnChange(Action<PwaOptions, string?> listener) =>
            NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
