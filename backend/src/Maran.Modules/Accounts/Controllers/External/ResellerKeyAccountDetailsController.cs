using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Queries.GetAccount;
using Maran.Modules.Accounts.Queries.GetAccountUsage;
using Maran.Modules.Accounts.Queries.GetProvisioningInfo;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers.External;

/// <summary>
/// What a reseller's API key may read beyond the account list (issue #7): one account, its use, and the
/// provisioning info, for an integration that provisions on the reseller's behalf.
/// </summary>
/// <remarks>
/// <para>
/// Reached ONLY with the <c>ApiKey</c> scheme and the <see cref="ApiKeyPolicyNames.AccountsRead"/> policy,
/// the one the list uses. It is a controller of its own so that the list controller stays as it was
/// shipped. Which accounts the key sees is decided below this controller, by the context's filter: an
/// account another reseller owns answers 404 exactly as one that does not exist, on every route here.
/// </para>
/// <para>
/// One account is answered as the account row (<see cref="AccountDto"/>), not as the single-resource view
/// the panel's screen gets, because that view carries the owner's e-mail address, which a key has no
/// business holding. The info route answers the revision of this API and the reseller's envelope beside
/// its use; it carries no panel version and no figure of the host. Read-only: no action dispatches a
/// command. These routes are limited by the general <c>api</c> bucket, not the provisioning one, which
/// bounds writes.
/// </para>
/// </remarks>
[Route("api/v1/reseller-api")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.AccountsRead)]
[Tags("Reseller API")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerKeyAccountDetailsController : BaseApiController
{
    /// <summary>The message bus the queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the queries are dispatched through.</param>
    public ResellerKeyAccountDetailsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads one of the key's reseller's accounts.</summary>
    /// <param name="id">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("accounts/{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<AccountDetailDto>>(new GetAccountQuery(id), cancellationToken);
        return ToActionResult(AccountMapper.ToDto(result));
    }

    /// <summary>
    /// Reads one account's use: its state and plan, the disk it occupies against its plan's quota, and its
    /// resource counts, each of which is stated as unavailable rather than read as zero.
    /// </summary>
    /// <param name="id">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("accounts/{id:guid}/usage")]
    [ProducesResponseType(typeof(AccountUsageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetUsageAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<AccountUsageDto>>(new GetAccountUsageQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads what the key's reseller may provision: the API revision and the reseller's envelope beside its use.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("info")]
    [ProducesResponseType(typeof(ProvisioningInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInfoAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ProvisioningInfoDto>>(new GetProvisioningInfoQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
