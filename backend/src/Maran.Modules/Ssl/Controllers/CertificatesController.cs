using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Commands.RemoveCertificate;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Queries.ListCertificates;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Ssl.Controllers;

/// <summary>
/// HTTP surface for TLS certificates. Thin by design (rules/csharp.md "Controller shape is fixed"):
/// binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No
/// business logic, no data access.
///
/// Open to any signed-in caller, because a certificate belongs to a customer's site and a customer
/// manages their own. What they can SEE is not decided here: every read and every mutation goes
/// through <c>SslDbContext</c> and the tenant-scoped site directory, so a certificate belonging to
/// somebody else answers 404 — never 403, which would confirm it exists (spec §8, rules/testing.md
/// item 3).
///
/// There is deliberately no endpoint that returns certificate material. A customer's browser can read
/// the certificate off their own site, and the private key is not theirs to fetch: a site's PHP runs
/// as that customer, so an endpoint that returned a key would be an endpoint any script on the site
/// could call (rules/security.md item 8).
/// </summary>
[Route("api/v1/certificates")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Certificates")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class CertificatesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public CertificatesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the certificates the caller may see, soonest expiry first.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CertificateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListCertificatesQuery();
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<CertificateDto>>>(query, cancellationToken));
    }

    /// <summary>Orders a certificate for one of the caller's sites and installs it.</summary>
    /// <param name="command">The domain to issue for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CertificateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueAsync(
        [FromBody] IssueCertificateCommand command,
        CancellationToken cancellationToken)
    {
        // The coalescing is the one the removed request type performed: a JSON null lands as null in
        // a non-nullable member, and the validator's host-name rule is written for a string.
        command = command with
        {
            Domain = command.Domain is null ? string.Empty : command.Domain,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        var result = await _bus.InvokeAsync<Result<CertificateDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/certificates/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Installs certificate material the customer supplied, replacing what is there.</summary>
    /// <param name="command">The domain and the material.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("custom")]
    [ProducesResponseType(typeof(CertificateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InstallCustomAsync(
        [FromBody] InstallCustomCertificateCommand command,
        CancellationToken cancellationToken)
    {
        // The three coalescings are the ones the removed request type performed. The material is
        // moved, not copied: the command's own ToString still refuses to render it, so the secret is
        // no more printable here than it was one type earlier.
        command = command with
        {
            Domain = command.Domain is null ? string.Empty : command.Domain,
            CertificatePem = command.CertificatePem is null ? string.Empty : command.CertificatePem,
            PrivateKeyPem = command.PrivateKeyPem is null ? string.Empty : command.PrivateKeyPem,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        var result = await _bus.InvokeAsync<Result<CertificateDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/certificates/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Removes a certificate and returns its site to plain HTTP. Another customer's answers 404.</summary>
    /// <param name="id">The certificate to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RemoveCertificateCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
