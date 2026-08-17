using System.Reflection;
using System.Text;
using System.Text.Json;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Pwa.Controllers;

/// <summary>
/// Serves the three files that turn a site into an installable app, generated from configuration
/// so a site owner writes no JavaScript and adds no build step.
/// </summary>
/// <remarks>
/// These are served from the application root rather than App_Plugins on purpose. A service
/// worker can only control pages at or below its own URL, so a worker at
/// <c>/App_Plugins/.../sw.js</c> would control nothing. The route has to be <c>/sw.js</c>.
/// </remarks>
[ApiController]
[AllowAnonymous]
public class PwaAssetsController : ControllerBase
{
    private readonly IOptionsMonitor<PwaOptions> _options;
    private readonly IPwaAssetGenerator _generator;

    public PwaAssetsController(IOptionsMonitor<PwaOptions> options, IPwaAssetGenerator generator)
    {
        _options = options;
        _generator = generator;
    }

    [HttpGet("/manifest.webmanifest")]
    [Produces("application/manifest+json")]
    public IActionResult Manifest()
    {
        if (!_options.CurrentValue.ServeAssets) return NotFound();

        return Content(_generator.Manifest(), "application/manifest+json", Encoding.UTF8);
    }

    [HttpGet("/sw.js")]
    public IActionResult ServiceWorker()
    {
        if (!_options.CurrentValue.ServeAssets) return NotFound();

        // A cached service worker is how a site gets stuck on a broken build: the browser keeps
        // serving the old worker, which keeps serving the old shell.
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Content(_generator.ServiceWorker(), "text/javascript", Encoding.UTF8);
    }

    [HttpGet("/baryodev-pwa.js")]
    public IActionResult Client()
    {
        if (!_options.CurrentValue.ServeAssets) return NotFound();

        // PathBase, not Path: ASP.NET strips the prefix a site is mounted under before routing, so
        // this is the only place the script can learn it. Empty at a domain root.
        return Content(
            _generator.Client(Request.PathBase.Value ?? string.Empty),
            "text/javascript",
            Encoding.UTF8);
    }
}
