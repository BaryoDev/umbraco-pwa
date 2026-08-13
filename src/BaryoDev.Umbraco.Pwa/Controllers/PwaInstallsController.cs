using BaryoDev.Umbraco.Pwa.Models;
using BaryoDev.Umbraco.Pwa.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.Pwa.Controllers;

/// <summary>
/// Backoffice-only read side, feeding the dashboard. This is the reason the package exists:
/// the adoption numbers live in the same backoffice as everything else, rather than behind a
/// separate login on a third-party service.
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("baryodev/pwa")]
// No [MapToApi]: ManagementApiControllerBase already maps to the "management"
// document, so these endpoints show up alongside Umbraco's own without this package
// having to own an OpenAPI document that moves between majors.
[ApiExplorerSettings(GroupName = "PWA")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
public class PwaInstallsController : ManagementApiControllerBase
{
    private readonly IPwaInstallService _installs;

    public PwaInstallsController(IPwaInstallService installs) => _installs = installs;

    [HttpGet("summary")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(PwaInstallSummary), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
        => Ok(await _installs.GetSummaryAsync(cancellationToken));

    [HttpGet("installs")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(IEnumerable<PwaInstallModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Installs(
        [FromQuery] bool installedOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await _installs.GetAllAsync(installedOnly, cancellationToken));
}
