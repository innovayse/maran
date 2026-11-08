using Maran.Modules.Identity.Commands.CreateApiKey;
using Maran.Modules.Identity.Commands.RevokeApiKey;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.Modules.Identity.Queries.ListApiKeyScopes;
using Maran.Modules.Identity.Queries.ListApiKeyStatuses;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Manages the API keys a reseller owns: the credentials its integrations present in the
/// <c>X-Api-Key</c> header to read the reseller's own plans and accounts (issue #368).
/// </summary>
/// <remarks>
/// <para>
/// Resellers only, through a signed-in session, and always for the reseller the access token names:
/// there is no parameter that names another. The owner is stamped onto each command by the action from
/// the caller, so a reseller cannot list, create or revoke a key for anyone else, and a key of another
/// owner answers 404 to a revoke exactly as a key that never existed does.
/// </para>
/// <para>
/// No policy that admits an API key guards any action here, so a key can never mint a successor or
/// revoke another key, and a view-as token is refused on every action: a key is a standing credential
/// that outlives the look.
/// </para>
/// </remarks>
[Route("api/v1/reseller/api-keys")]
[Tags("Reseller API Keys")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerApiKeysController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The reseller making the request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ResellerApiKeysController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the calling reseller's API keys, optionally searched by name and narrowed to one status.</summary>
    /// <param name="query">Page, page size, search, sort and the <c>status</c> filter, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total that matched, or a 400 naming the bound that was broken.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApiKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListApiKeysQuery query, CancellationToken cancellationToken)
    {
        if (CurrentUser.Caller is not ResellerCaller reseller)
        {
            return Unauthorized();
        }

        query = query with { OwnerResellerId = reseller.ResellerId };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ApiKeyDto>>>(query, cancellationToken));
    }

    /// <summary>Lists the scopes a reseller's key can be granted, named for the caller's language, for the create dialog.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The reseller-grade choices, in the order the vocabulary defines.</returns>
    [HttpGet("scopes")]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetScopesAsync(CancellationToken cancellationToken)
    {
        if (CurrentUser.Caller is not ResellerCaller reseller)
        {
            return Unauthorized();
        }

        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<ApiKeyChoiceDto>>>(
            new ListApiKeyScopesQuery(reseller.ResellerId), cancellationToken));
    }

    /// <summary>Lists the statuses a key can be in, named for the caller's language, for the list's status filter.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The choices: active, revoked, expired, in that order.</returns>
    [HttpGet("statuses")]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusesAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<ApiKeyChoiceDto>>>(
            new ListApiKeyStatusesQuery(), cancellationToken));
    }

    /// <summary>Creates an API key owned by the calling reseller and returns the plaintext key, once.</summary>
    /// <remarks>
    /// The plaintext is in this one response and nowhere else: the panel stores only a digest, so a
    /// lost key is replaced by creating another, never recovered. Not idempotent on purpose: a retry
    /// creates a second live key. A reseller holds at most
    /// <see cref="Domain.Policies.ApiKeyLimits.MaxKeysPerReseller"/> working keys.
    /// </remarks>
    /// <param name="command">
    /// The key's name, scopes, optional address ranges and optional expiry, bound from the JSON body.
    /// The creator, the owner, the address and the user agent are written here from the caller's token and connection.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>201 with the key and its plaintext, or a 400, or a 409 when the reseller holds its maximum of working keys.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiKeyCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.Caller is not ResellerCaller reseller)
        {
            return Unauthorized();
        }

        command = command with
        {
            CreatedByUserId = reseller.UserId,
            OwnerResellerId = reseller.ResellerId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToCreatedActionResult(
            await _bus.InvokeAsync<Result<ApiKeyCreatedDto>>(command, cancellationToken),
            "/api/v1/reseller/api-keys");
    }

    /// <summary>Revokes one of the calling reseller's keys: it authenticates nothing from the next request on. Revoking a revoked key succeeds.</summary>
    /// <param name="id">The key to revoke. A key another owner holds answers 404.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUser.Caller is not ResellerCaller reseller)
        {
            return Unauthorized();
        }

        var command = new RevokeApiKeyCommand(
            id,
            reseller.UserId,
            ClientIpAddress,
            CallerUserAgent,
            reseller.ResellerId);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
