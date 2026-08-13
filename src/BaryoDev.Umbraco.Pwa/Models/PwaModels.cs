namespace BaryoDev.Umbraco.Pwa.Models;

/// <summary>
/// The body posted by the browser. Field-for-field the shape emitted by
/// <c>@baryodev/pwa-kit</c>'s <c>pwaStatus()</c>, so the same client works against this package
/// and against any other backend implementing the contract.
/// </summary>
public class PwaReportRequest
{
    /// <summary>Stable per-browser id from localStorage.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>standalone | minimal-ui | fullscreen | browser.</summary>
    public string DisplayMode { get; set; } = "browser";

    /// <summary>ios | android | windows | macos | linux | other.</summary>
    public string? Platform { get; set; }

    /// <summary>True when running as an installed app.</summary>
    public bool Installed { get; set; }
}

/// <summary>One tracked browser, as shown in the backoffice dashboard.</summary>
public class PwaInstallModel
{
    public string DeviceId { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public string DisplayMode { get; set; } = "browser";
    public bool Installed { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? InstalledAt { get; set; }
    public int LaunchCount { get; set; }
}

/// <summary>Headline numbers for the dashboard, so it does not have to total the rows itself.</summary>
public class PwaInstallSummary
{
    /// <summary>Browsers that have run the site as an installed app at least once.</summary>
    public int Installed { get; set; }

    /// <summary>Browsers seen, installed or not. The denominator for an install rate.</summary>
    public int TotalDevices { get; set; }

    /// <summary>Installed browsers seen within the last 30 days.</summary>
    public int ActiveLast30Days { get; set; }

    /// <summary>Installed-browser counts by platform, highest first.</summary>
    public Dictionary<string, int> ByPlatform { get; set; } = new();
}
