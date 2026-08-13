using BaryoDev.Umbraco.Pwa.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace BaryoDev.Umbraco.Pwa.Migrations;

/// <summary>
/// The install table's schema history. Adding a column later means adding a step here and
/// chaining it after the previous state string, never editing an existing step: Umbraco records
/// the last state a site reached, so an edited step never re-runs on sites that already passed it.
/// </summary>
internal class PwaMigrationPlan : MigrationPlan
{
    public const string InitialState = "baryodev-pwa-init";

    public PwaMigrationPlan() : base("BaryoDevPwa")
    {
        From(string.Empty).To<AddPwaInstallTable>(InitialState);
    }
}

internal class AddPwaInstallTable : AsyncMigrationBase
{
    public AddPwaInstallTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(PwaInstallDto.TableName))
        {
            Logger.LogDebug("{Table} already exists, skipping", PwaInstallDto.TableName);
            return Task.CompletedTask;
        }

        Create.Table<PwaInstallDto>().Do();
        return Task.CompletedTask;
    }
}
