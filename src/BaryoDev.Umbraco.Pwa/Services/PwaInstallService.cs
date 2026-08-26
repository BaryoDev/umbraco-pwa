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

    // Platform is sniffed from the user agent by the client, so anything can arrive here.
    // Anything unrecognised becomes "other" rather than being stored verbatim: the dashboard
    // groups by this column, and one crafted value would otherwise show up as a "platform".
    private static readonly string[] KnownPlatforms =
        ["ios", "android", "windows", "macos", "linux", "other"];

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
                Platform = Platform(report.Platform),
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
            existing.Platform = Platform(report.Platform);
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
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var all = db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {PwaInstallDto.TableName}");
        var installed = db.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {PwaInstallDto.TableName} WHERE installed = 1");
        var active = db.ExecuteScalar<int>(
            scope.SqlContext.Sql()
                .SelectCount()
                .From<PwaInstallDto>()
                .Where<PwaInstallDto>(x => x.Installed && x.LastSeenAt >= cutoff));
        var byPlatform = db.Fetch<dynamic>(
                scope.SqlContext.Sql()
                    .Select("platform, COUNT(*) AS count")
                    .From<PwaInstallDto>()
                    .Where<PwaInstallDto>(x => x.Installed)
                    .GroupBy("platform")
                    .OrderByDescending("count"))
            .ToDictionary(
                x => string.IsNullOrWhiteSpace((string?)x.platform) ? "other" : (string)x.platform,
                x => (int)x.count);

        return Task.FromResult(new PwaInstallSummary
        {
            TotalDevices = all,
            Installed = installed,
            ActiveLast30Days = active,
            ByPlatform = byPlatform,
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

    private static string Platform(string? value)
    {
        var cleaned = Clean(value, 32)?.ToLowerInvariant();
        return cleaned is not null && KnownPlatforms.Contains(cleaned) ? cleaned : "other";
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
