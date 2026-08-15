using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.Logging;
using Shouldly;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Pwa.Tests;

[Collection(UmbracoCollection.Name)]
public class PwaStartupReadinessTests(UmbracoSiteFixture site)
{
    private readonly UmbracoSiteFixture _site = site;

    [Fact]
    public async Task Default_unconfigured_site_establishes_baseline_without_warning()
    {
        var readinessService = _site.Resolve<IPwaStartupReadinessService>();
        var runtimeState = _site.Resolve<IRuntimeState>();
        var keyValueService = _site.Resolve<IKeyValueService>();
        var logger = new TestLogger<PwaOneShotCheckHandler>();

        keyValueService.SetValue(
            PwaOneShotCheckHandler.ReadinessStateKey,
            string.Empty);

        var handler = new PwaOneShotCheckHandler(
            readinessService,
            runtimeState,
            keyValueService,
            logger);

        await handler.HandleAsync(
            new UmbracoApplicationStartedNotification(false),
            CancellationToken.None);

        logger.Entries
            .ShouldNotContain(entry => entry.Level == LogLevel.Warning);

        keyValueService
            .GetValue(PwaOneShotCheckHandler.ReadinessStateKey)
            .ShouldBe(bool.FalseString);
    }

    [Fact]
    public async Task Installable_site_regressing_to_not_installable_logs_a_warning()
    {
        var readinessService = _site.Resolve<IPwaStartupReadinessService>();
        var runtimeState = _site.Resolve<IRuntimeState>();
        var keyValueService = _site.Resolve<IKeyValueService>();
        var logger = new TestLogger<PwaOneShotCheckHandler>();

        // Simulate a site that was installable on the previous startup.
        keyValueService.SetValue(
            PwaOneShotCheckHandler.ReadinessStateKey,
            bool.TrueString);

        keyValueService
            .GetValue(PwaOneShotCheckHandler.ReadinessStateKey)
            .ShouldBe(bool.TrueString);

        var handler = new PwaOneShotCheckHandler(
            readinessService,
            runtimeState,
            keyValueService,
            logger);

        await handler.HandleAsync(
            new UmbracoApplicationStartedNotification(false),
            CancellationToken.None);

        var warnings = logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .ToList();

        warnings.ShouldNotBeEmpty();

        warnings.ShouldContain(entry =>
            entry.Message.Contains("PWA readiness check failed"));
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
