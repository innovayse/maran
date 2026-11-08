using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers.External;

/// <summary>
/// The recoverable account operations a reseller's API key may perform (issue #7): create, suspend,
/// reactivate and change plan, for a billing integration that provisions on the reseller's behalf.
/// </summary>
/// <remarks>
/// <para>
/// Reached ONLY with the <c>ApiKey</c> scheme and the <see cref="ApiKeyPolicyNames.AccountsWrite"/> policy,
/// which admits a key a reseller owns and holds that scope. Every action dispatches the command the
/// panel's own screens use (<see cref="AccountsController"/>), so no business rule is repeated here: the
/// reseller's envelope, plan ownership, name claims and a suspended reseller's refusal are the
/// handlers'. Which accounts the key reaches is decided below this controller, by the context's filter
/// and by <c>ResellerScope</c>, which read the key's reseller as they read a reseller login's: another
/// reseller's account answers 404 exactly as one that does not exist, and an account created here is
/// owned by the key's reseller, whatever the body names.
/// </para>
/// <para>
/// <b>Every mutation REQUIRES an <c>Idempotency-Key</c> header</b> (400 <c>IdempotencyKeyRequired</c>
/// without one): a billing system retries a timed-out create, and without the key the retry would create
/// a second account. Deletion is not here: it is not recoverable, so it has its own scope and controller
/// (<see cref="ResellerKeyAccountTerminationController"/>).
/// </para>
/// <para>
/// Plan change answers the account row (<see cref="AccountDto"/>), not the single-resource view the
/// panel's screen gets: that view carries the owner's e-mail address, which a key has no business
/// holding. View-as is refused throughout; a key cannot start one.
/// </para>
/// </remarks>
[Route("api/v1/reseller-api/accounts")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.AccountsWrite)]
[Tags("Reseller API")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Provisioning)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerKeyAccountWritesController : BaseApiController
{
    /// <summary>The message bus the commands are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the commands are dispatched through.</param>
    public ResellerKeyAccountWritesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Creates a hosting account owned by the key's reseller and invites its owner.</summary>
    /// <param name="command">
    /// The account's name, primary domain, plan and owner address, bound straight from the body; the two
    /// audit fields it also carries are unbindable by construction and stamped below.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Idempotent(Required = true)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDto>>(stamped, cancellationToken);
        return ToCreatedActionResult(
            result,
            $"/api/v1/reseller-api/accounts/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Suspends an account: its sites and services stop while its data stays. Repeating it changes nothing.</summary>
    /// <param name="id">The account to suspend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/suspend")]
    [Idempotent(Required = true)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new SuspendAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Lifts a suspension. Repeating it changes nothing.</summary>
    /// <param name="id">The account to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/reactivate")]
    [Idempotent(Required = true)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ReactivateAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AccountDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Moves an account to another plan of the key's reseller. Refused with 409 when the account already
    /// uses more disk than the new plan allows. Answers the account row, without the owner's address.
    /// </summary>
    /// <param name="id">The account to move.</param>
    /// <param name="command">The plan to move it to. Its account id, address and agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/plan")]
    [Idempotent(Required = true)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePlanAsync(
        Guid id,
        [FromBody] ChangeAccountPlanCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { AccountId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(stamped, cancellationToken);
        return ToActionResult(AccountMapper.ToDto(result));
    }
}
