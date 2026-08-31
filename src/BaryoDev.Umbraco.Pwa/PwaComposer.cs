using BaryoDev.Umbraco.Pwa.Migrations;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.Pwa;

/// <summary>
/// Wires the package up. Nothing else is required of the host: dropping the NuGet reference in
/// is the whole installation.
/// </summary>
public class PwaComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<PwaOptions>()
            .Bind(builder.Config.GetSection(PwaOptions.SectionName));

        builder.Services.AddSingleton<IPwaInstallService, PwaInstallService>();
        builder.Services.AddRecurringBackgroundJob<PwaInstallRetentionJob>();
        builder.Services.AddSingleton<IPwaAssetGenerator, PwaAssetGenerator>();

        builder.Services.AddSingleton<PwaReadinessService>();
        builder.Services.AddSingleton<IPwaReadinessService>(sp => sp.GetRequiredService<PwaReadinessService>());
        builder.Services.AddSingleton<IPwaStartupReadinessService>(sp => sp.GetRequiredService<PwaReadinessService>());

        // The named probe client, and no bare AddHttpClient(). An unnamed client would be an
        // ungoverned one, and the readiness probe is the only outbound request this package makes.
        builder.Services.AddPwaIconProbe();

        // Marks visitor-specific responses private so the worker declines to cache them. See
        // PwaPrivateResponseMiddleware: Umbraco sends no cache headers, so without this the worker
        // has nothing to go on.
        builder.Services.AddSingleton<
            Microsoft.AspNetCore.Hosting.IStartupFilter,
            Services.PwaPrivateResponseStartupFilter>();

        // Singleton: the limiter holds the per-caller windows, so a new instance per request would
        // be a limiter that never limits.
        builder.Services.AddSingleton<Controllers.PwaReportRateLimitFilter>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, PwaMigrationHandler>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, PwaOneShotCheckHandler>();
    }
}

/// <summary>
/// Creates the install table on first start, and on upgrade when a later version adds a step.
/// Umbraco records the state each site has reached, so this is a no-op on every start after the
/// first.
/// </summary>
internal class PwaMigrationHandler : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IMigrationPlanExecutor _executor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;

    public PwaMigrationHandler(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor executor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState)
    {
        _scopeProvider = scopeProvider;
        _executor = executor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
    }

    public async Task HandleAsync(
        UmbracoApplicationStartingNotification notification,
        CancellationToken cancellationToken)
    {
        // Below Run the database may not be ready, and an install or upgrade is already in progress.
        if (_runtimeState.Level < RuntimeLevel.Run) return;

        await new Upgrader(new PwaMigrationPlan())
            .ExecuteAsync(_executor, _scopeProvider, _keyValueService);
    }
}

/// <summary>
/// Performs a one-time PWA readiness check after Umbraco has completed startup
/// and reports configuration problems to the application log.
/// </summary>
internal sealed class PwaOneShotCheckHandler(
    IPwaStartupReadinessService readinessService,
    IRuntimeState runtimeState,
    IKeyValueService keyValueService,
    ILogger<PwaOneShotCheckHandler> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    internal const string ReadinessStateKey = "BaryoDevPwa:Readiness:Installable";

    /// <summary>
    /// Runs the startup readiness check when Umbraco is operational and logs
    /// actionable details for checks that prevent PWA installation.
    /// </summary>
    /// <param name="notification">The Umbraco application-started notification.</param>
    /// <param name="cancellationToken">
    /// Token signaled when application startup processing is cancelled.
    /// </param>
    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification,
        CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        try
        {
            var readiness = await readinessService.CheckAsync(cancellationToken);

            var previousValue = keyValueService.GetValue(ReadinessStateKey);
            var previousInstallable = bool.TryParse(previousValue, out var value)
                ? value
                : (bool?)null;

            keyValueService.SetValue(
                ReadinessStateKey,
                readiness.Installable.ToString());

            // First run establishes the baseline only.
            if (previousInstallable is null)
            {
                return;
            }

            // Only warn when an installable site regresses.
            if (previousInstallable == true && !readiness.Installable)
            {
                foreach (var check in readiness.Checks.Where(c => !c.Advisory && !c.Passed))
                {
                    logger.LogWarning(
                        "PWA readiness check failed: {CheckName}. {Detail}",
                        check.Name,
                        check.Detail);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown or startup cancellation is not a readiness failure.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "PWA startup readiness check could not be completed.");
        }
    }
}
