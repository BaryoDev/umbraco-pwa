namespace BaryoDev.Umbraco.Pwa;

/// <summary>
/// Configuration, bound from the <c>BaryoDev:Pwa</c> section of appsettings.
/// Every setting has a working default: the package is useful with no configuration at all.
/// </summary>
public class PwaOptions
{
    public const string SectionName = "BaryoDev:Pwa";

    /// <summary>
    /// Record install reports. Turning this off leaves the install prompt and the service worker
    /// working while collecting nothing, which is the right setting for a site that wants the
    /// app behaviour but no adoption data at all.
    /// </summary>
    public bool TrackInstalls { get; set; } = true;

    /// <summary>
    /// Drop reports from browsers that have only ever been seen in a normal tab. Off by default,
    /// because the browser-only rows are the denominator that makes an install rate meaningful.
    /// Turn it on to store nothing until someone actually installs.
    /// </summary>
    public bool TrackInstalledOnly { get; set; }

    /// <summary>
    /// Delete rows not seen for this many days. Zero keeps everything forever.
    /// </summary>
    public int RetentionDays { get; set; }

    /// <summary>
    /// Serve a generated <c>/manifest.webmanifest</c> and <c>/sw.js</c> from the site root.
    /// Turn this off if the site already ships its own, which is the only case where the two
    /// would fight: a service worker's scope is its own directory, so it has to be at the root.
    /// </summary>
    public bool ServeAssets { get; set; } = true;

    /// <summary>
    /// The most install reports one caller can have stored per minute. Zero turns the limit off.
    /// </summary>
    /// <remarks>
    /// The report endpoint is anonymous by necessity, and it inserts a row for each novel
    /// <c>deviceId</c>, which the client generates. A loop with fresh ids inserts rows without
    /// limit. <see cref="RetentionDays"/> is the real bound, because a table that deletes rows not
    /// seen for N days cannot grow forever whatever is posted at it. This is the cheaper first
    /// bound in front of it.
    ///
    /// Deliberately generous. Genuine traffic is one report per launch per device, but the
    /// partition is the caller's address, and every visitor behind one corporate proxy or CDN
    /// egress shares it. A tight limit would drop real reports from exactly the kind of site most
    /// worth counting. If your site sits behind a proxy, configure forwarded headers so this sees
    /// the visitor rather than the proxy, or set this to zero and rely on retention.
    ///
    /// The address is used to partition the limiter and is never written anywhere.
    /// <c>SECURITY.md</c> promises no column identifies a visitor, and that stays true: this
    /// touches an address in memory for the length of one request.
    /// </remarks>
    public int MaxReportsPerMinute { get; set; } = 120;

    public PwaManifestOptions Manifest { get; set; } = new();

    public PwaServiceWorkerOptions ServiceWorker { get; set; } = new();

    public PwaInstallPromptOptions InstallPrompt { get; set; } = new();
}

/// <summary>
/// The "add this to your home screen" banner. Without one, most visitors never discover the site
/// is installable at all: Chrome buries its own prompt, and iOS has no prompt whatsoever.
/// </summary>
public class PwaInstallPromptOptions
{
    /// <summary>On by default. An installable site nobody is told about is not much use.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Falls back to the manifest short name, then the manifest name.</summary>
    public string? AppName { get; set; }

    public string Description { get; set; } =
        "Add it to your home screen for a full-screen, app-like experience.";

    /// <summary>Colours the install button. Falls back to the manifest theme colour.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Shown beside the text. Falls back to the first manifest icon.</summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Path prefixes where the prompt is suppressed. The backoffice is excluded by default:
    /// an editor does not need to be asked to install the site they are editing.
    /// </summary>
    public List<string> HideOnPaths { get; set; } = new() { "/umbraco" };
}

/// <summary>The web app manifest, which is what makes a site installable at all.</summary>
public class PwaManifestOptions
{
    /// <summary>Falls back to the Umbraco site name when unset.</summary>
    public string? Name { get; set; }

    /// <summary>Shown under the home-screen icon, where space is tight. Defaults to Name.</summary>
    public string? ShortName { get; set; }

    public string? Description { get; set; }

    /// <summary>standalone | fullscreen | minimal-ui | browser.</summary>
    public string Display { get; set; } = "standalone";

    public string StartUrl { get; set; } = "/";

    /// <summary>Colours the OS chrome around the installed app.</summary>
    public string ThemeColor { get; set; } = "#ffffff";

    public string BackgroundColor { get; set; } = "#ffffff";

    /// <summary>
    /// Icon media paths. Android needs 192 and 512; iOS uses the apple-touch-icon link instead.
    /// Without at least one 192 and one 512, Chrome will not offer to install the site.
    /// </summary>
    public List<PwaIcon> Icons { get; set; } = new();

    /// <summary>
    /// Controls whether launching the app opens a new window or focuses an existing one.
    /// One of "auto", "focus-existing", "navigate-existing" or "navigate-new". An unrecognised
    /// value is ignored and the key is left out of the manifest.
    /// </summary>
    /// <remarks>
    /// Null by default, so the key is omitted and browsers keep doing whatever they did before.
    /// "navigate-existing" is the setting most app-like sites want, since the browser default opens
    /// a fresh window on every launch, but choosing it here would change behaviour for every site
    /// upgrading from 0.3.0 without anyone asking for it.
    /// </remarks>
    public string? LaunchHandler { get; set; }
}

public class PwaIcon
{
    public string Src { get; set; } = string.Empty;

    /// <summary>e.g. "192x192".</summary>
    public string Sizes { get; set; } = string.Empty;

    public string Type { get; set; } = "image/png";

    /// <summary>"any" or "maskable". A maskable icon avoids the white circle on Android.</summary>
    public string? Purpose { get; set; }
}

public class PwaServiceWorkerOptions
{
    /// <summary>Prefixes the cache names, so two apps on one origin cannot collide.</summary>
    public string CachePrefix { get; set; } = "umbraco";

    /// <summary>
    /// Folded into the cache names. Change it on every deploy so the new worker purges the old
    /// build's assets. Left unset, the package uses its own assembly version, which changes on
    /// package upgrade but not on a content deploy.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>Requests under this prefix are treated as live data, not shell.</summary>
    public string ApiPrefix { get; set; } = "/api/";

    /// <summary>
    /// Never cached. The Umbraco backoffice is excluded by default and should stay that way:
    /// a cached backoffice is a stale editing experience and a way to serve one user's data
    /// to another on a shared machine.
    /// </summary>
    public List<string> SkipPaths { get; set; } = new() { "/umbraco/" };

    /// <summary>Shown when a navigation fails offline and nothing is cached.</summary>
    public string NavigationFallback { get; set; } = "/";
}
