using Maran.Modules.Ssl.Commands.IssuePanelCertificate;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Queries.GetPanelCertificate;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Ssl.Controllers;

/// <summary>
/// HTTP surface for the panel's OWN certificate (issue #132): read what is recorded, and order one for
/// the configured host name. Administrators only — the panel's TLS is the operator's, and a customer
/// may neither trigger an order under the panel's ACME account nor learn the panel's host name here.
/// </summary>
/// <remarks>
/// There is no endpoint that returns material, for the reason <see cref="CertificatesController"/>
/// gives, and none that takes a host name: the name is the operator's configuration, read by the
/// handler, never a request field.
/// </remarks>
[Route("api/v1/panel-certificate")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Panel certificate")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PanelCertificateController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PanelCertificateController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the trusted certificate recorded for the panel's configured host name.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PanelCertificateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<PanelCertificateDto>>(new GetPanelCertificateQuery(), cancellationToken));
    }

    /// <summary>Orders a certificate for the panel's configured host name and installs it.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(PanelCertificateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IssueAsync(CancellationToken cancellationToken)
    {
        var command = new IssuePanelCertificateCommand(ClientIpAddress, CallerUserAgent);

        return ToActionResult(
            await _bus.InvokeAsync<Result<PanelCertificateDto>>(command, cancellationToken));
    }
}
