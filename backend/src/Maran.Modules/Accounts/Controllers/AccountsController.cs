using Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;
using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Commands.TransferAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetAccount;
using Maran.Modules.Accounts.Queries.GetAccountResourceSummary;
using Maran.Modules.Accounts.Queries.ListAccountChoices;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Modules.Accounts.Queries.ListOwnerLanguages;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for hosting accounts. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
///
/// Administrators and resellers. Managing the hosting accounts on a server is a server-owner action
/// (spec §8), and a reseller is the server owner's delegate over the accounts it owns: the policy
/// admits both roles and WHICH accounts a caller sees is decided below the controller, by the
/// context's query filter, so another reseller's account answers 404 exactly as one that does not
/// exist. A customer's own view of the account they own is <see cref="MyAccountController"/>, scoped by
/// the caller's own token rather than by a parameter.
///
/// Every action declares what a view-as token may do here, because an operator viewing as a reseller
/// holds the reseller role and reaches it: reads are allowed, and everything that changes an account is
/// refused while looking.
/// </summary>
[Route("api/v1/accounts")]
[Authorize(Policy = AuthorizationPolicies.AdminOrReseller)]
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

    /// <summary>Lists one page of hosting accounts, searched, filtered and sorted.</summary>
    /// <param name="query">
    /// Paging, search, sort and the plan, status and owning-reseller filters, bound from the query string.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListAccountsQuery query, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<AccountDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Lists one page of account ids and names for a picker or an owner-name lookup, so no screen
    /// has to load the full account list to name an account.
    /// </summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("choices")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<AccountChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetChoicesAsync(
        [FromQuery] ListAccountChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<AccountChoiceDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Lists every language an account owner's invitation can be written in, each named in itself, so
    /// the create-account form offers exactly the set the create endpoint accepts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("owner-languages")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<OwnerLanguageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwnerLanguagesAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<OwnerLanguageDto>>>(
            new ListOwnerLanguagesQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one hosting account.</summary>
    /// <param name="id">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(AccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(new GetAccountQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Reads what one hosting account owns — sites, databases, logins, cron entries, backups — beside
    /// its plan's limits. A kind whose module is not composed has no row.
    /// </summary>
    /// <param name="id">The account to summarise.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/resource-summary")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(AccountResourceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResourceSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<AccountResourceSummaryDto>>(
            new GetAccountResourceSummaryQuery(id), cancellationToken);
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
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
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
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ReactivateAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Moves an account to another plan. Refused with 409 when the account already uses more disk
    /// than the new plan allows; see <c>ChangeAccountPlanCommandHandler</c> for why.
    /// </summary>
    /// <param name="id">The account to move.</param>
    /// <param name="command">The plan to move it to. Its account id, address and agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/plan")]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(AccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePlanAsync(
        Guid id,
        [FromBody] ChangeAccountPlanCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { AccountId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Changes an account's owner address, and with it the address its login's invitation and
    /// password resets are mailed to. Refused with 409 when another login already holds the address.
    /// </summary>
    /// <param name="id">The account to change.</param>
    /// <param name="command">The new address. Its account id, address and agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/owner-email")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(AccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeOwnerEmailAsync(
        Guid id,
        [FromBody] ChangeAccountOwnerEmailCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { AccountId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Hands an account to another reseller, or to the administrator, with a plan of the new owner.
    /// Administrator only: a reseller is refused with 403 before the handler runs, so it can neither
    /// move its own accounts away nor draw another owner's in.
    /// </summary>
    /// <param name="id">The account being moved.</param>
    /// <param name="command">The new owner and the plan of that owner the account is sold from.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account as it now stands, or the typed refusal.</returns>
    [HttpPut("{id:guid}/owner")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(AccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransferAsync(
        Guid id,
        [FromBody] TransferAccountCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { AccountId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Removes an account, its system user and everything under its home directory — after taking
    /// the final backup the panel promises (spec §12).
    /// </summary>
    /// <param name="id">The account to remove.</param>
    /// <param name="skipFinalBackup">
    /// Deletes WITHOUT taking that final backup. Off unless asked for, and only an administrator may ask:
    /// this controller admits resellers too, so the refusal is made by the handler from the caller's own
    /// token (<c>AccountFinalBackupSkipForbidden</c>, 403) and not by the policy; a customer has no route
    /// to this parameter at all. It exists because the alternative to a refusal on a failed backup is an undeletable account:
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
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ulong), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
}
