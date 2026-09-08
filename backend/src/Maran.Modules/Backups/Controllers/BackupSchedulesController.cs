using Maran.Modules.Backups.Commands.SaveBackupSchedule;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Queries.GetBackupSchedule;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Backups.Controllers;

/// <summary>
/// HTTP surface for the panel's backup schedules and their retention (spec §11, R10). Thin by
/// design: binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and a signed-in customer is answered 403.</b> Choosing when the server
/// backs an account up and how many copies it keeps is a decision about the machine and its disk,
/// not about one tenant's data — a customer who could set a retention of one would be deleting
/// their own history, and one who could set 365 would be filling the operator's disk. There is no
/// tenant dimension to hide, so 403 is the right refusal here and 404 would be the tenant answer
/// worn in the wrong place; this mirrors <c>FirewallRulesController</c>, <c>MonitoringController</c>
/// and <c>SmtpSettingsController</c>, which is this tree's admin-gating idiom.
/// </para>
/// <para>
/// <b>There is no endpoint that runs a schedule now.</b> A backup on demand is
/// <c>POST /api/v1/backups</c> and it already exists; a second way to ask for the same thing would
/// be a second place the kind is decided, and the kind is what retention reads.
/// </para>
/// <para>
/// <b>There is no DELETE.</b> A schedule is switched off by saving it with <c>enabled: false</c>,
/// which keeps the operator's cadence and retention where they can be read and turned back on.
/// Removing the row would silently discard those settings and leave a screen that cannot say what
/// the server used to do.
/// </para>
/// </remarks>
[Route("api/v1/backup-schedules")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Backups")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class BackupSchedulesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public BackupSchedulesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads one schedule: the host-wide policy, or the override of the account named in the query
    /// string. A server that has never been configured answers 404.
    /// </summary>
    /// <param name="query">Which schedule to read; omitting the account reads the host-wide policy.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] GetBackupScheduleQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<BackupScheduleDto>>(query, cancellationToken));
    }

    /// <summary>Creates or replaces one schedule and answers with what was stored.</summary>
    /// <param name="command">
    /// The schedule to store. The audit fields it carries are stamped here from the connection and
    /// are not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A <c>PUT</c> rather than a <c>POST</c> because there is at most one schedule per account: the
    /// request states what the schedule should be, and sending it twice leaves the same one row.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveBackupScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<BackupScheduleDto>>(command, cancellationToken));
    }
}
