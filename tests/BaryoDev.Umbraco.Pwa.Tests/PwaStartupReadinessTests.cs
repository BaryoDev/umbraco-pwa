using BaryoDev.Umbraco.Pwa.Services;
using BaryoDev.Umbraco.Pwa.Models;
using Microsoft.Extensions.Logging;
using Shouldly;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Pwa.Tests;

[Collection(UmbracoCollection.Name)]
public class PwaStartupReadinessTests(UmbracoSiteFixture site)
{
    private readonly UmbracoSiteFixture _site = site;

    [Fact]
    public async Task Below_Run_does_not_check_readiness_or_touch_state()
    {
        var runtimeState = TestProxy.Create<IRuntimeState>();
        TestProxy.For(runtimeState).RuntimeLevel = RuntimeLevel.Install;
        var readinessService = TestProxy.Create<IPwaStartupReadinessService>();
        TestProxy.For(readinessService).ThrowOnInvocation = true;
        var keyValueService = TestProxy.Create<IKeyValueService>();
        TestProxy.For(keyValueService).ThrowOnInvocation = true;
        var logger = new TestLogger<PwaOneShotCheckHandler>();

        var handler = new PwaOneShotCheckHandler(
            readinessService,
            runtimeState,
            keyValueService,
            logger);

        await handler.HandleAsync(
            new UmbracoApplicationStartedNotification(false),
            CancellationToken.None);

        TestProxy.For(readinessService).Calls.ShouldBeEmpty();
        TestProxy.For(keyValueService).Calls.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Not_installable_site_recovering_to_installable_logs_nothing()
    {
        var runtimeState = TestProxy.Create<IRuntimeState>();
        TestProxy.For(runtimeState).RuntimeLevel = RuntimeLevel.Run;
        var keyValueService = TestProxy.Create<IKeyValueService>();
        TestProxy.For(keyValueService).KeyValue = bool.FalseString;
        var logger = new TestLogger<PwaOneShotCheckHandler>();
        var handler = new PwaOneShotCheckHandler(
            new StubStartupReadinessService(new PwaReadiness { Installable = true }),
            runtimeState,
            keyValueService,
            logger);

        await handler.HandleAsync(
            new UmbracoApplicationStartedNotification(false),
            CancellationToken.None);

        logger.Entries.ShouldBeEmpty();
        TestProxy.For(keyValueService).KeyValue.ShouldBe(bool.TrueString);
    }

    [Fact]
    public async Task Readiness_check_exception_is_logged_as_a_warning_and_not_rethrown()
    {
        var runtimeState = TestProxy.Create<IRuntimeState>();
        TestProxy.For(runtimeState).RuntimeLevel = RuntimeLevel.Run;
        var logger = new TestLogger<PwaOneShotCheckHandler>();
        var handler = new PwaOneShotCheckHandler(
            new StubStartupReadinessService(new PwaReadiness(), new InvalidOperationException("readiness failed")),
            runtimeState,
            TestProxy.Create<IKeyValueService>(),
            logger);

        await handler.HandleAsync(
            new UmbracoApplicationStartedNotification(false),
            CancellationToken.None);

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("PWA startup readiness check could not be completed."));
    }

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

    private sealed class StubStartupReadinessService(PwaReadiness result, Exception? exception = null)
        : IPwaStartupReadinessService
    {
        public Task<PwaReadiness> CheckAsync(CancellationToken ct = default)
            => exception is null ? Task.FromResult(result) : Task.FromException<PwaReadiness>(exception);
    }
}
