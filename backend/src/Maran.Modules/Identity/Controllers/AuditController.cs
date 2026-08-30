using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Reads the append-only audit journal: who did what, when, and from where (spec §10, "Аудит").
/// </summary>
/// <remarks>
/// Administrators only. The journal names every actor on the server and the address they came
/// from, so it is the one place where a customer reading a page would learn about other tenants —
/// which is why it is behind <see cref="AuthorizationPolicies.AdminOnly"/> rather than the
/// controller-wide authenticated default. It is read-only by construction: entries are written by
/// <c>IAuditWriter</c> from inside the handlers that perform the action, never through HTTP, so
/// there is no route here that could forge or amend one.
/// </remarks>
[Route("api/v1/audit")]
[Tags("Audit")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AuditController : BaseApiController
{
    /// <summary>How many entries a request returns when it does not say.</summary>
    private const int DefaultLimit = 100;

    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus the query travels on.</param>
    public AuditController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the most recent audit entries, newest first.</summary>
    /// <param name="limit">How many entries to return; the validator bounds it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The entries, or a typed failure when the limit is out of range.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<AuditEventDto>>>(
            new ListAuditEventsQuery(limit), cancellationToken);

        return ToActionResult(result);
    }
}
