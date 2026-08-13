using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BaryoDev.Umbraco.Pwa.Controllers;

/// <summary>
/// The endpoint the browser posts to on launch. Anonymous by necessity: the visitor reporting
/// an install is a visitor, not a backoffice user.
/// </summary>
/// <remarks>
/// Always returns 202 and never a body. Two reasons. This is best-effort telemetry, so a client
/// should never retry or surface an error over it. And a distinguishable response would let an
/// unauthenticated caller probe which device ids exist.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("umbraco/pwa/api")]
public class PwaReportController : ControllerBase
{
    private readonly IPwaInstallService _installs;

    public PwaReportController(IPwaInstallService installs) => _installs = installs;

    [HttpPost("report")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Report(
        [FromBody] PwaReportRequest report,
        CancellationToken cancellationToken)
    {
        await _installs.ReportAsync(report, cancellationToken);
        return Accepted();
    }
}
