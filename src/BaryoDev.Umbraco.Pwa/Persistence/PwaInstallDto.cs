using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.Pwa.Persistence;

/// <summary>
/// One row per browser that has run the site, keyed by a client-generated
/// <see cref="DeviceId"/>. Repeat launches from the same browser bump <see cref="LastSeenAt"/>
/// and <see cref="LaunchCount"/> rather than adding rows.
/// </summary>
/// <remarks>
/// There is deliberately no visitor identifier here beyond the device id the browser generates
/// for itself, and no IP address. The table answers "how many people installed this, on what",
/// which is the question a site owner has, without becoming a record of who visited.
/// </remarks>
[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[ExplicitColumns]
internal class PwaInstallDto
{
    internal const string TableName = "BaryoDevPwaInstall";

    [PrimaryKeyColumn(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>Stable per-browser id from localStorage. The dedup key.</summary>
    [Column("deviceId")]
    [Length(100)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_BaryoDevPwaInstall_deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>ios | android | windows | macos | linux | other.</summary>
    [Column("platform")]
    [Length(32)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Platform { get; set; }

    /// <summary>standalone | minimal-ui | fullscreen | browser, at the last report.</summary>
    [Column("displayMode")]
    [Length(32)]
    public string DisplayMode { get; set; } = "browser";

    /// <summary>True once this browser has run the site as an installed app at least once.</summary>
    [Column("installed")]
    public bool Installed { get; set; }

    [Column("firstSeenAt")]
    public DateTime FirstSeenAt { get; set; }

    [Column("lastSeenAt")]
    [Index(IndexTypes.NonClustered, Name = "IX_BaryoDevPwaInstall_lastSeenAt")]
    public DateTime LastSeenAt { get; set; }

    /// <summary>When this browser was first seen installed. Null if only ever seen in a tab.</summary>
    [Column("installedAt")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? InstalledAt { get; set; }

    [Column("launchCount")]
    public int LaunchCount { get; set; }
}
