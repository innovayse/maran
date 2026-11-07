using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Queries.ExportPersonalData;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Hands an administrator everything the panel holds about one person or one hosting account, as a
/// downloaded JSON document (issue #146).
/// </summary>
/// <remarks>
/// <para>
/// Administrators only, and refused to a viewed-as session as well: administrator-only actions are
/// exempt from the view-as declaration, but an export of personal data is the one action where the
/// refusal is stated rather than inherited. The routes name the subject and the actions write it into
/// the query themselves, so no body or query string can retarget an export; there is no listing of
/// users to pick from, so a user's id comes from an account export's <c>team</c> section.
/// </para>
/// <para>
/// Shares the <see cref="RateLimitPolicies.AuditExport"/> window with the journal export: both hand the
/// same kind of data to one caller, and two separate windows would double what a stolen session may
/// take. The response is written directly, as <c>SitesController</c>'s log stream is, so the controller
/// returns an empty result once the writer has finished.
/// </para>
/// </remarks>
[Route("api/v1/privacy")]
[Tags("Privacy")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicies.AuditExport)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class PrivacyController : BaseApiController
{
    /// <summary>The message bus the query travels on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Streams the planned export into the response.</summary>
    private readonly PersonalDataExportWriter _writer;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus the query travels on.</param>
    /// <param name="writer">Streams the planned export into the response.</param>
    public PrivacyController(ICurrentUser currentUser, IMessageBus bus, PersonalDataExportWriter writer)
        : base(currentUser)
    {
        _bus = bus;
        _writer = writer;
    }

    /// <summary>Exports what the panel holds about one hosting account: its record, team and journal entries.</summary>
    /// <param name="accountId">The account, from the route.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The JSON document as an attachment, or a problem when the account is unknown or the export too large.</returns>
    [HttpGet("accounts/{accountId:guid}/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExportAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await ExportAsync(PersonalDataSubjectKind.Account, accountId, cancellationToken);
    }

    /// <summary>Exports what the panel holds about one person: their login, seats, sessions and journal entries.</summary>
    /// <param name="userId">The user, from the route.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The JSON document as an attachment, or a problem when the user is unknown or the export too large.</returns>
    [HttpGet("users/{userId:guid}/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExportUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await ExportAsync(PersonalDataSubjectKind.User, userId, cancellationToken);
    }

    /// <summary>Plans the export, then streams it.</summary>
    /// <param name="kind">Which route was taken.</param>
    /// <param name="subjectId">The id that route carried.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    private async Task<IActionResult> ExportAsync(
        PersonalDataSubjectKind kind,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var query = new ExportPersonalDataQuery(kind, subjectId, ClientIpAddress, CallerUserAgent);

        var plan = await _bus.InvokeAsync<Result<PersonalDataExportPlan>>(query, cancellationToken);
        if (!plan.IsSuccess)
        {
            return ToActionResult(plan);
        }

        // Written directly to the response because the body is produced in batches; see the remarks.
        await _writer.WriteAsync(Response, plan.Value, cancellationToken);
        return new EmptyResult();
    }
}
