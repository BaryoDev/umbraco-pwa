using BaryoDev.Umbraco.Pwa.Persistence;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.Configuration;
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
    public async Task An_unset_RetentionDays_keeps_a_ten_year_old_row()
    {
        await Insert("retention-unset", DateTime.UtcNow.AddYears(-10));

        await RunJob(Bound(new Dictionary<string, string?>()));

        (await Find("retention-unset")).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_explicit_400_deletes_rows_unseen_for_over_400_days_and_keeps_newer_ones()
    {
        await Insert("retention-400-old", DateTime.UtcNow.AddDays(-401));
        await Insert("retention-400-recent", DateTime.UtcNow.AddDays(-399));

        await RunJob(Bound(new Dictionary<string, string?> { ["BaryoDev:Pwa:RetentionDays"] = "400" }));

        (await Find("retention-400-old")).ShouldBeNull();
        (await Find("retention-400-recent")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Empty_table_is_harmless()
    {
        var deviceId = "retention-empty";
        await RunJob(30);

        (await Find(deviceId)).ShouldBeNull();
    }

    // Bound the way the composer binds it, so "unset" and "explicit zero" are the real thing
    // rather than two property initialisers.
    private static PwaOptions Bound(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new PwaOptions();
        configuration.GetSection(PwaOptions.SectionName).Bind(options);
        return options;
    }

    private Task RunJob(int retentionDays) => RunJob(new PwaOptions {RetentionDays = retentionDays});

    private Task RunJob(PwaOptions pwaOptions)
    {
        var options = new TestOptionsMonitor(pwaOptions);
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
