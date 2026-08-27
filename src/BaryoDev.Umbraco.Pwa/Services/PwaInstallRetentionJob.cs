using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Core.Sync;

namespace BaryoDev.Umbraco.Pwa.Services;

internal class PwaInstallRetentionJob : IRecurringBackgroundJob
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IOptionsMonitor<PwaOptions> _options;

    public PwaInstallRetentionJob(IScopeProvider scopeProvider, IOptionsMonitor<PwaOptions> options)
    {
        _scopeProvider = scopeProvider;
        _options = options;
    }

    private Task RunRetentionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retentionDays = _options.CurrentValue.RetentionDays;
        if (retentionDays <= 0) return Task.CompletedTask;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        scope.Database.Execute(
            $"DELETE FROM {Persistence.PwaInstallDto.TableName} WHERE lastSeenAt < @cutoff",
            new {cutoff});

        return Task.CompletedTask;
    }

    internal Task RunForTestsAsync() => RunRetentionAsync(CancellationToken.None);

    public TimeSpan Period { get; } = TimeSpan.FromDays(1);

    public TimeSpan Delay { get; } = TimeSpan.FromMinutes(1);

    public TimeSpan IgnoredDelay { get; } = TimeSpan.FromMinutes(1);

    public ServerRole[] ServerRoles { get; } = Enum.GetValues<ServerRole>();

    public event EventHandler? PeriodChanged;

    public event EventHandler? IgnoredDelayChanged;

    public Task RunJobAsync() => RunRetentionAsync(CancellationToken.None);

    public Task RunJobAsync(CancellationToken cancellationToken) =>
        RunRetentionAsync(cancellationToken);
}
