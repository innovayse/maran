using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Controllers.Requests;
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
    /// <param name="request">The account's name, primary domain, and plan.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateAccountCommand(request.Name, request.PrimaryDomain, request.PlanId);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
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
        var result = await _bus.InvokeAsync<Result<AccountDto>>(new SuspendAccountCommand(id), cancellationToken);
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
        var result = await _bus.InvokeAsync<Result<AccountDto>>(new ReactivateAccountCommand(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Removes an account, its system user and everything under its home directory.</summary>
    /// <param name="id">The account to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ulong), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ulong>>(new DeleteAccountCommand(id), cancellationToken);
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
