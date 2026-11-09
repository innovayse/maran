using Maran.Modules.Backups.Commands.SaveMyBackupSchedule;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Queries.GetMyBackupSchedule;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Backups.Controllers;

/// <summary>
/// HTTP surface for the signed-in customer's own backup schedule (issue #147). Thin by design
/// (rules/csharp.md "Controller shape is fixed"): stamps the caller's own account, dispatches through
/// Wolverine and translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The route names no account, because the schedule it serves is the caller's.</b> The account is
/// taken from <c>CurrentUser.Caller</c>, never from the request, so there is no identifier here for
/// another tenant's row to be reached by: the IDOR question is closed by construction, and an
/// administrator, reseller or account-less login — none of whom has an account of their own — is
/// answered 404 by the handlers. The administrator's endpoint (<c>api/v1/backup-schedules</c>) stays
/// administrators-only and is not touched.
/// </para>
/// <para>
/// <b>It writes the same per-account row an administrator's override uses</b>, but never the
/// host-wide policy, and never a destination. Its bounds are <c>CustomerBackupLimits</c>.
/// </para>
/// <para>
/// There is no DELETE and no "run now", for the reasons <c>BackupSchedulesController</c> gives: a
/// schedule is switched off by saving it disabled, and a backup on demand is <c>POST api/v1/backups</c>.
/// </para>
/// </remarks>
[Route("api/v1/me/backup-schedule")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Backups")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Backups)]
public sealed class MyBackupScheduleController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MyBackupScheduleController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the caller's own schedule. An account that has never configured one answers 200 with no
    /// schedule in it, because "nothing is scheduled" is the ordinary first state, not a failure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(BackupScheduleScopeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetMyBackupScheduleQuery(CallerAccountId());

        return ToActionResult(await _bus.InvokeAsync<Result<BackupScheduleScopeDto>>(query, cancellationToken));
    }

    /// <summary>Creates or replaces the caller's own schedule and answers with what was stored.</summary>
    /// <param name="command">
    /// The schedule to store. The account and the audit fields are stamped here from the caller and the
    /// connection and are not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// Refused to an operator viewing as the customer: a schedule is running work that outlives the
    /// look, which is what <see cref="ViewAsAccess.Refused"/> is for.
    /// </remarks>
    [HttpPut]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveMyBackupScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with
        {
            AccountId = CallerAccountId(),
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<BackupScheduleDto>>(command, cancellationToken));
    }

    /// <summary>The caller's own account, or an identifier that names no account when the caller has none.</summary>
    /// <returns>
    /// The customer's account id; <see cref="Guid.Empty"/> for an administrator, a reseller or an
    /// account-less login, which the tenant directory answers as an account that does not exist.
    /// </returns>
    private Guid CallerAccountId()
    {
        return CurrentUser.Caller is CustomerCaller customer ? customer.AccountId : Guid.Empty;
    }
}
