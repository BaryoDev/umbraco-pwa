using Shouldly;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Pwa.Tests;

public class PwaMigrationHandlerTests
{
    [Theory]
    [InlineData(RuntimeLevel.Install)]
    [InlineData(RuntimeLevel.Upgrade)]
    public async Task Below_Run_does_not_touch_migration_dependencies(RuntimeLevel level)
    {
        var scopeProvider = TestProxy.Create<ICoreScopeProvider>();
        TestProxy.For(scopeProvider).ThrowOnInvocation = true;
        var executor = TestProxy.Create<IMigrationPlanExecutor>();
        TestProxy.For(executor).ThrowOnInvocation = true;
        var keyValueService = TestProxy.Create<IKeyValueService>();
        TestProxy.For(keyValueService).ThrowOnInvocation = true;
        var runtimeState = TestProxy.Create<IRuntimeState>();
        TestProxy.For(runtimeState).RuntimeLevel = level;

        var handler = new PwaMigrationHandler(
            scopeProvider,
            executor,
            keyValueService,
            runtimeState);

        await handler.HandleAsync(
            new UmbracoApplicationStartingNotification(level, false),
            CancellationToken.None);

        TestProxy.For(scopeProvider).Calls.ShouldBeEmpty();
        TestProxy.For(executor).Calls.ShouldBeEmpty();
        TestProxy.For(keyValueService).Calls.ShouldBeEmpty();
    }
}
