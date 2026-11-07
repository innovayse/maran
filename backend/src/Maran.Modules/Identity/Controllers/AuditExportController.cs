using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Queries.ExportAuditEvents;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Hands the audit journal out as a file: <c>GET /api/v1/audit/export?format=csv|json</c> with the same
/// filters the journal list takes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one place the whole security record, every address and user agent in it, leaves the
/// panel in a single response, and it is guarded as a disclosure.</b> Administrators only
/// (<see cref="AuthorizationPolicies.AdminOnly"/>; an API key and every customer or reseller login are
/// refused), view-as refused although an administrator-only action is exempt from that declaration, its
/// own rate-limit bucket (<see cref="RateLimitPolicies.AuditExport"/>), the export journalled before the
/// first byte, capped and never truncated. Threat note:
/// docs/superpowers/notes/2026-11-07-audit-retention-erasure-threat-note.md (second review OUTSTANDING).
/// </para>
/// <para>
/// The route is its own controller, not an action of <see cref="AuditController"/>, because it carries a
/// different rate-limit policy and a different content type from the reads beside it.
/// </para>
/// </remarks>
[Route("api/v1/audit/export")]
[Tags("Audit")]
[Produces("application/json", "text/csv")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[ViewAsAccess(ViewAsAccess.Refused)]
[EnableRateLimiting(RateLimitPolicies.AuditExport)]
public sealed class AuditExportController : BaseApiController
{
    /// <summary>The message bus the query travels on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Streams the file into the response.</summary>
    private readonly AuditExportWriter _writer;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus the query travels on.</param>
    /// <param name="writer">Streams the file into the response.</param>
    public AuditExportController(ICurrentUser currentUser, IMessageBus bus, AuditExportWriter writer)
        : base(currentUser)
    {
        _bus = bus;
        _writer = writer;
    }

    /// <summary>
    /// Exports the matching journal entries, oldest first, as a CSV or JSON attachment. The export is
    /// authorised, counted and journalled first; only then does the first byte of the file leave.
    /// </summary>
    /// <param name="query">
    /// <c>format</c> (<c>csv</c> or <c>json</c>, JSON by default) and the <c>action</c>, <c>outcome</c>,
    /// <c>from</c>, <c>to</c>, <c>search</c>, <c>actor</c> and <c>account</c> filters, bound from the
    /// query string; the caller's identity and address are stamped here and never bound.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects, which stops the reads.</param>
    /// <returns>The file, or a 400 naming the bound that was broken or saying the window is too large.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] ExportAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        query = query with
        {
            RequestedByUserId = SignedInCallerPolicy.Require(CurrentUser).UserId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        var plan = await _bus.InvokeAsync<Result<AuditExportPlan>>(query, cancellationToken);

        if (!plan.IsSuccess)
        {
            return ToActionResult(plan);
        }

        // Written directly to the response rather than returned as a value, because the body is
        // produced over time: a materialized value would hold the whole file in memory.
        await _writer.WriteAsync(Response, plan.Value, cancellationToken);
        return new EmptyResult();
    }
}
