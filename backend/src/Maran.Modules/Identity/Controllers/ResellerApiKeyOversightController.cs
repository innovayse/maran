using Maran.Modules.Identity.Commands.RevokeApiKey;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The administrator's oversight of ONE reseller's API keys: list them and revoke any of them while
/// the reseller stays active (issue #435). Before this, the only lever over a reseller's keys was
/// suspending or deleting the reseller.
/// </summary>
/// <remarks>
/// <para>
/// A controller of its own and not an action on <see cref="ApiKeysController"/> or
/// <see cref="ResellerApiKeysController"/>: the administrator's keys and the reseller's keys are two
/// surfaces whose owner is stamped from different places (the token of the signed-in owner there, the
/// route here), and this one is the only place an administrator names a reseller. It reads and revokes
/// only; it creates nothing, so an administrator can never mint a credential that acts for a reseller.
/// </para>
/// <para>
/// The route's reseller id is the owner, stamped by the action into the same <c>OwnerResellerId</c>
/// member the reseller's own surface fills from its token. The handlers are the existing ones, so a
/// key of the administrator or of another reseller answers 404 to a revoke here exactly as a key that
/// never existed does, and an id of a reseller that does not exist lists nothing: this module cannot
/// read the Accounts schema, and a reseller that is gone has no keys left (they go with it).
/// The revoke is journalled by the same entry the owner's own revoke writes, with the administrator
/// as actor.
/// </para>
/// <para>
/// The key is never shown: the list carries the prefix, the scopes and the use, and the plaintext
/// exists only in the response that created the key. An administrator viewing as a customer is
/// refused, as on every credential surface.
/// </para>
/// </remarks>
[Route("api/v1/resellers/{resellerId:guid}/api-keys")]
[Tags("Reseller API Keys (administrator)")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerApiKeyOversightController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ResellerApiKeyOversightController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of one reseller's API keys, optionally searched by name and narrowed to one status.</summary>
    /// <param name="resellerId">The reseller whose keys are listed.</param>
    /// <param name="query">Page, page size, search, sort and the <c>status</c> filter, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total that matched (empty for a reseller with no keys or none such), or a 400 naming the bound that was broken.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApiKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        Guid resellerId,
        [FromQuery] ListApiKeysQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { OwnerResellerId = resellerId };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ApiKeyDto>>>(query, cancellationToken));
    }

    /// <summary>Revokes one of a reseller's keys: it authenticates nothing from the next request on. Revoking a revoked key succeeds.</summary>
    /// <param name="resellerId">The reseller that owns the key.</param>
    /// <param name="id">The key to revoke. A key this reseller does not own answers 404.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid resellerId, Guid id, CancellationToken cancellationToken)
    {
        var command = new RevokeApiKeyCommand(
            id,
            SignedInCallerPolicy.Require(CurrentUser).UserId,
            ClientIpAddress,
            CallerUserAgent,
            resellerId);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
