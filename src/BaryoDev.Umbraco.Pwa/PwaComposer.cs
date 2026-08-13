using BaryoDev.Umbraco.Pwa.Migrations;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.Extensions.DependencyInjection;
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
        builder.Services.AddSingleton<IPwaAssetGenerator, PwaAssetGenerator>();
        builder.Services.AddSingleton<IPwaReadinessService, PwaReadinessService>();
        builder.Services.AddHttpClient();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, PwaMigrationHandler>();
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
