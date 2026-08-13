using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Persistence;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.Pwa.Services;

public interface IPwaInstallService
{
    /// <summary>Record a launch. Creates the row on first sight, updates it thereafter.</summary>
    Task ReportAsync(PwaReportRequest report, CancellationToken ct = default);

    /// <summary>Tracked browsers, most recently seen first.</summary>
    Task<IReadOnlyList<PwaInstallModel>> GetAllAsync(bool installedOnly, CancellationToken ct = default);

    Task<PwaInstallSummary> GetSummaryAsync(CancellationToken ct = default);
}

internal class PwaInstallService : IPwaInstallService
{
    private static readonly string[] KnownDisplayModes =
        ["standalone", "minimal-ui", "fullscreen", "browser"];

    private readonly IScopeProvider _scopeProvider;
    private readonly IOptionsMonitor<PwaOptions> _options;

    public PwaInstallService(IScopeProvider scopeProvider, IOptionsMonitor<PwaOptions> options)
    {
        _scopeProvider = scopeProvider;
        _options = options;
    }

    public Task ReportAsync(PwaReportRequest report, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        if (!options.TrackInstalls) return Task.CompletedTask;

        // The body is public input, so nothing from it reaches the database unvalidated.
        var deviceId = Clean(report.DeviceId, 100);
        if (string.IsNullOrWhiteSpace(deviceId)) return Task.CompletedTask;

        var displayMode = KnownDisplayModes.Contains(report.DisplayMode) ? report.DisplayMode : "browser";
        var installed = report.Installed || displayMode is "standalone" or "fullscreen";

        if (options.TrackInstalledOnly && !installed) return Task.CompletedTask;

        var now = DateTime.UtcNow;

        using var scope = _scopeProvider.CreateScope();
        var db = scope.Database;

        var existing = db.FirstOrDefault<PwaInstallDto>(
            scope.SqlContext.Sql()
                .Select<PwaInstallDto>()
                .From<PwaInstallDto>()
                .Where<PwaInstallDto>(x => x.DeviceId == deviceId));

        if (existing is null)
        {
            db.Insert(new PwaInstallDto
            {
                DeviceId = deviceId,
                Platform = Clean(report.Platform, 32),
                DisplayMode = displayMode,
                Installed = installed,
                FirstSeenAt = now,
                LastSeenAt = now,
                InstalledAt = installed ? now : null,
                LaunchCount = 1,
            });
        }
        else
        {
            existing.DisplayMode = displayMode;
            existing.Platform = Clean(report.Platform, 32) ?? existing.Platform;
            existing.LastSeenAt = now;
            existing.LaunchCount++;

            // Installed is sticky. A user who installs the app and later opens it in a tab has
            // still installed it, and flapping the flag would make the headline number meaningless.
            if (installed && !existing.Installed)
            {
                existing.Installed = true;
                existing.InstalledAt = now;
            }

            db.Update(existing);
        }

        scope.Complete();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PwaInstallModel>> GetAllAsync(bool installedOnly, CancellationToken ct = default)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);

        var sql = scope.SqlContext.Sql().Select<PwaInstallDto>().From<PwaInstallDto>();
        if (installedOnly) sql = sql.Where<PwaInstallDto>(x => x.Installed);
        sql = sql.OrderBy<PwaInstallDto>(x => x.LastSeenAt).Append("DESC");

        IReadOnlyList<PwaInstallModel> rows = scope.Database
            .Fetch<PwaInstallDto>(sql)
            .Select(Map)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task<PwaInstallSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var all = db.Fetch<PwaInstallDto>(
            scope.SqlContext.Sql().Select<PwaInstallDto>().From<PwaInstallDto>());
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var installed = all.Where(x => x.Installed).ToList();

        return Task.FromResult(new PwaInstallSummary
        {
            TotalDevices = all.Count,
            Installed = installed.Count,
            ActiveLast30Days = installed.Count(x => x.LastSeenAt >= cutoff),
            ByPlatform = installed
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Platform) ? "other" : x.Platform!)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
        });
    }

    private static PwaInstallModel Map(PwaInstallDto dto) => new()
    {
        DeviceId = dto.DeviceId,
        Platform = dto.Platform,
        DisplayMode = dto.DisplayMode,
        Installed = dto.Installed,
        FirstSeenAt = dto.FirstSeenAt,
        LastSeenAt = dto.LastSeenAt,
        InstalledAt = dto.InstalledAt,
        LaunchCount = dto.LaunchCount,
    };

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
