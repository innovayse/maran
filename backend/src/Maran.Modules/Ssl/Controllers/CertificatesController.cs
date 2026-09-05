using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Commands.RemoveCertificate;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Controllers.Requests;
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
    /// <param name="request">The domain to issue for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CertificateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueAsync(
        [FromBody] IssueCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new IssueCertificateCommand(request.Domain ?? string.Empty, ClientIpAddress, UserAgent());
        var result = await _bus.InvokeAsync<Result<CertificateDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/certificates/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Installs certificate material the customer supplied, replacing what is there.</summary>
    /// <param name="request">The domain and the material.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("custom")]
    [ProducesResponseType(typeof(CertificateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InstallCustomAsync(
        [FromBody] InstallCustomCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new InstallCustomCertificateCommand(
            request.Domain ?? string.Empty,
            request.CertificatePem ?? string.Empty,
            request.PrivateKeyPem ?? string.Empty,
            ClientIpAddress,
            UserAgent());

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
        var command = new RemoveCertificateCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
