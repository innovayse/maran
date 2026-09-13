using Maran.Modules.Backups.Commands.CreateBackup;
using Maran.Modules.Backups.Commands.DeleteBackup;
using Maran.Modules.Backups.Commands.RestoreBackup;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Queries.GetBackup;
using Maran.Modules.Backups.Queries.ListBackups;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Backups.Controllers;

/// <summary>
/// HTTP surface for account backups. Thin by design (rules/csharp.md "Controller shape is fixed"):
/// binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No
/// business logic, no data access.
///
/// Open to any signed-in caller, because a backup belongs to a customer's account and a customer
/// takes their own. What they can SEE is not decided here: every read and every mutation goes
/// through <c>BackupsDbContext</c>, whose global query filter scopes rows to the caller's account,
/// and through the tenant-scoped account directory — so a backup belonging to somebody else answers
/// 404, never 403, which would confirm it exists (spec §8, rules/testing.md item 3).
///
/// There is deliberately no endpoint that returns an archive's bytes. An archive holds the account's
/// files and the contents of its databases, and streaming a root-owned file of that description
/// through the API process is a surface the spec does not ask for; an operator retrieves one from
/// the server, where the file already is.
/// </summary>
[Route("api/v1/backups")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Backups")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class BackupsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public BackupsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the backups the caller may see, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListBackupsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<BackupDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one backup. Another customer's backup answers 404, not 403.</summary>
    /// <param name="id">The backup to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<BackupDto>>(new GetBackupQuery(id), cancellationToken));
    }

    /// <summary>
    /// Takes a backup of an account now and answers with the finished record — completed or failed,
    /// which the row's status says.
    /// </summary>
    /// <param name="command">
    /// The account to back up. The audit fields it carries are stamped here from the connection and
    /// are not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<BackupDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/backups/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Replaces an account from one of its backups: its home directory is swapped for the archive's
    /// and each of its databases is dropped, re-created and reloaded.
    /// </summary>
    /// <param name="id">The backup to restore from. Another customer's answers 404, not 403.</param>
    /// <param name="command">
    /// The typed confirmation; anything but the account's own user name refuses. The backup id and
    /// the audit fields are stamped here from the route and the connection, and no value the body
    /// carries for them survives.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// <para>
    /// <b>This is the operation that can destroy a working account, and it is replace-within-scope
    /// rather than undo.</b> What the archive holds replaces what is there; what it does not hold —
    /// the vhosts, the certificates, the crontab, the firewall rules, the SFTP logins — is untouched.
    /// </para>
    /// <para>
    /// It answers 200 ONLY for a restore that replaced everything it set out to. A partial restore
    /// is a failure carrying its own code and the counts, because the account has been changed and
    /// no caller may read that as routine.
    /// </para>
    /// <para>
    /// Its own rate-limit bucket rather than the general one: a restore is not a screen read, and
    /// the thing worth refusing is a caller repeating it.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/restore")]
    [EnableRateLimiting(RateLimitPolicies.BackupRestore)]
    [ProducesResponseType(typeof(RestoreOutcomeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RestoreAsync(
        Guid id,
        [FromBody] RestoreBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The confirmation is coalesced because a JSON null lands as null in a non-nullable member
        // (nothing sets RespectNullableAnnotations), and the validator refuses an empty string with
        // RestoreConfirmationRequired where a null would throw inside the comparison.
        command = command with
        {
            BackupId = id,
            ConfirmAccountUsername = command.ConfirmAccountUsername ?? string.Empty,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<RestoreOutcomeDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Deletes a backup's archive and its record. The copy is gone and is not recoverable. Another
    /// customer's backup answers 404, not 403.
    /// </summary>
    /// <param name="id">The backup to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteBackupCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
