using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Controllers.Requests;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for hosting accounts. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
/// </summary>
/// <remarks>
/// No <c>[Authorize]</c> here yet: the panel has no authentication stack (no login, no session, no
/// <see cref="ICurrentUser"/> implementation — see <see cref="BaseApiController"/>'s constructor
/// doc comment). Adding the attribute today would require an authentication handler that does not
/// exist, which fails every request — including anonymous smoke checks — the instant this
/// controller is exercised, rather than degrading gracefully. The rate limit, route, tags and
/// per-action response shapes are otherwise exactly as mandated; only the authorization gate is
/// deferred, and it must be added the moment authentication ships (rules/csharp.md "Controller
/// shape is fixed" — an accepted, reported deviation, not a silent skip).
/// </remarks>
[Route("api/v1/accounts")]
[Tags("Accounts")]
[Produces("application/json")]
[EnableRateLimiting("api")]
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
