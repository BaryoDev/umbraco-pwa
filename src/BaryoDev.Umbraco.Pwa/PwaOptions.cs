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

    public PwaManifestOptions Manifest { get; set; } = new();

    public PwaServiceWorkerOptions ServiceWorker { get; set; } = new();
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
