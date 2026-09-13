using Maran.Modules.Backups.Commands.SaveBackupDestination;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Queries.ListBackupDestinations;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Backups.Controllers;

/// <summary>
/// HTTP surface for the places this server keeps backups (spec §11, §209). Thin by design: binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and a signed-in customer is answered 403.</b> Where the server keeps its
/// archives is a decision about the machine and its disks; there is no tenant dimension to hide, so
/// there is nothing a 403 could confirm, and 404 would be the tenant answer worn in the wrong place
/// (rules/security.md item 6, which scopes 404 to a resource another account owns). This mirrors
/// <see cref="BackupSchedulesController"/> and the seven other admin-only controllers in the tree.
/// </para>
/// <para>
/// <b>There is no DELETE.</b> The only destination that exists is the default one, which every
/// historic backup row's null already points at and which the startup reconciliation would restore —
/// so a delete endpoint could do nothing but refuse, on every server, for ever. An endpoint whose
/// every answer is the same refusal is documentation of code that does not exist.
/// </para>
/// <para>
/// <b>No request on this surface carries a path, and the path a response carries may be absent.</b>
/// The agent refuses to be told a local root and writes under its own constant, so there is nothing
/// for a caller to set: a field the panel accepted and never honoured is how a screen comes to lie
/// about where a customer's data is. The path the list RETURNS is read from the agent's handshake on
/// the request that shows it, and is <c>null</c> when the agent could not be asked — a screen says
/// so rather than filling the gap with a default.
/// </para>
/// </remarks>
[Route("api/v1/backup-destinations")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Backups")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class BackupDestinationsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public BackupDestinationsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists every place this server records backups as living.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDestinationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        return Ok(await _bus.InvokeAsync<IReadOnlyList<BackupDestinationDto>>(
            new ListBackupDestinationsQuery(), cancellationToken));
    }

    /// <summary>Records a new destination, or answers with the reason this build cannot hold one.</summary>
    /// <param name="command">
    /// The destination to record. The audit fields it carries are stamped here from the connection
    /// and are not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// Every answer this action gives today is a refusal, and it is published rather than omitted so
    /// that an operator asking for S3-compatible storage gets a machine-stable code and a sentence in
    /// their own language instead of a 404 on a route that was never written. That is the honest
    /// statement of an open decision (<c>docs/superpowers/notes/2026-09-07-backup-object-store-seam.md</c>),
    /// and it is what a settings screen branches on.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(BackupDestinationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveBackupDestinationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<BackupDestinationDto>>(command, cancellationToken));
    }
}
