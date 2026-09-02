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
/// A well-formed report always answers 202 with no body, whatever became of it. Two reasons. This
/// is best-effort telemetry, so a client should never retry or surface an error over it. And an
/// answer that varied with the report's content would let an unauthenticated caller probe which
/// device ids exist.
///
/// Malformed requests are rejected on shape before any of that reaches here: 400 for unparseable
/// JSON, 415 for the wrong content type, 413 for a body over 4KB. Those reveal nothing about what
/// is stored, which is why they do not break the property above.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("umbraco/pwa/api")]
public class PwaReportController : ControllerBase
{
    /// <summary>Generous for four short fields, and small enough not to be a lever.</summary>
    private const int MaxBodyBytes = 4096;

    private readonly IPwaInstallService _installs;

    public PwaReportController(IPwaInstallService installs) => _installs = installs;

    [HttpPost("report")]
    // Four small fields. The largest legitimate body is a few hundred bytes, and deviceId is cut
    // to 100 characters only after the JSON has been parsed, so without this a caller can have the
    // server materialise megabytes before anything gets to reject it.
    [RequestSizeLimit(MaxBodyBytes)]
    // Bounds how many rows one caller can have stored. Answers 202 when it declines, same as
    // always: see PwaReportRateLimitFilter for why that matters.
    [ServiceFilter(typeof(PwaReportRateLimitFilter))]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Report(
        [FromBody] PwaReportRequest report,
        CancellationToken cancellationToken)
    {
        await _installs.ReportAsync(report, cancellationToken);
        return Accepted();
    }
}
