using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetAccount;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for hosting accounts. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
///
/// Administrators only. Managing the hosting accounts on a server is a server-owner action
/// (spec §8); a customer's own view of the account they own arrives with the accounts lifecycle,
/// scoped by the caller's own token rather than by a parameter.
/// </summary>
[Route("api/v1/accounts")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Accounts")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AccountsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AccountsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists every hosting account.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<AccountDto>>>(new ListAccountsQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one hosting account.</summary>
    /// <param name="id">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(new GetAccountQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Creates a new hosting account row.</summary>
    /// <param name="command">
    /// The account's name, primary domain, and plan. The command is bound straight from the body —
    /// there is no request type in between — and the two audit fields it also carries are unbindable
    /// by construction (see <see cref="CreateAccountCommand"/>), so the stamp below is the only
    /// thing that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDto>>(stamped, cancellationToken);
        return ToCreatedActionResult(result, $"/api/v1/accounts/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Suspends an account: its sites and services stop while its data stays (spec §8). Idempotent,
    /// so a billing system may call it on every overdue invoice without tracking what it already did.
    /// </summary>
    /// <param name="id">The account to suspend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new SuspendAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Lifts a suspension. Idempotent, for the same reason suspension is.</summary>
    /// <param name="id">The account to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ReactivateAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Removes an account, its system user and everything under its home directory — after taking
    /// the final backup the panel promises (spec §12).
    /// </summary>
    /// <param name="id">The account to remove.</param>
    /// <param name="skipFinalBackup">
    /// Deletes WITHOUT taking that final backup. Off unless asked for, and the whole of what makes
    /// it safe is that this controller is <c>AdminOnly</c> at class level, so a customer has no
    /// route to this parameter at all — the guarantee is the policy, not a check in the handler.
    /// It exists because the alternative to a refusal on a failed backup is an undeletable account:
    /// an operator whose customer's data is already gone, or already copied, has to be able to say
    /// so. Whoever sets it is named in the audit journal.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A failed final backup REFUSES the deletion and leaves the account exactly as it was, which is
    /// the recoverable state; the answer carries the failure's code and the panel task carries the
    /// same one. It is not a 500 and it is not a partial deletion.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ulong), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        [FromQuery] bool skipFinalBackup,
        CancellationToken cancellationToken)
    {
        var command = new DeleteAccountCommand(id, ClientIpAddress, CallerUserAgent, skipFinalBackup);
        var result = await _bus.InvokeAsync<Result<ulong>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Lists every plan an account can be created against — the reference data the account-creation
    /// form needs, so the caller never has to know or type a plan id (rules/architecture.md "The
    /// backend owns the data, the SPA renders it"). Kept on this controller rather than a separate
    /// <c>PlansController</c>: a plan has no lifecycle of its own in this pass (no create/update/
    /// delete endpoint — plans are seeded reference data), it exists only to be selected while
    /// creating an account, and every other list this module exposes lives beside the resource it
    /// describes the same way.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlansAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<PlanDto>>>(new ListPlansQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
