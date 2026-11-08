using Maran.Modules.Sites.Commands.ReplaceSiteOptions;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Queries.GetSiteOptions;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sites.Controllers;

/// <summary>
/// HTTP surface for one site's per-site options: redirects, basic authentication, IP rules, upload
/// size and cache headers. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Open to any signed-in caller whose plan sells the Sites feature, like <see cref="SitesController"/>,
/// and scoped the same way: every read and write goes through <c>SitesDbContext</c>, whose query filter
/// hides another customer's site, so it answers 404 and never 403 (rules/security.md item 6). The
/// answer never carries a credential — users are listed by name — and the request's passwords are hashed
/// by the handler and exist nowhere else.
/// </remarks>
[Route("api/v1/sites/{id:guid}/options")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[RequiresAccountFeature(AccountFeature.Sites)]
[Tags("Sites")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SiteOptionsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SiteOptionsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads a site's options with the bounds and choices their screen needs.</summary>
    /// <param name="id">The site whose options to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(SiteOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<SiteOptionsDto>>(new GetSiteOptionsQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Replaces ALL of a site's options with the set in the body: anything not in it is removed.
    /// Idempotent, so a retried save converges to the same state.
    /// </summary>
    /// <param name="id">The site whose options to replace.</param>
    /// <param name="command">
    /// The complete new set. The route id and the audit fields it also carries are stamped here and are
    /// not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(SiteOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceAsync(
        Guid id,
        [FromBody] ReplaceSiteOptionsCommand command,
        CancellationToken cancellationToken)
    {
        // The coalescings are the ones the other body-bound actions in this module perform: a JSON null
        // lands as null in a non-nullable member, and the validator should see an empty list instead.
        command = command with
        {
            SiteId = id,
            Redirects = command.Redirects is null ? [] : command.Redirects,
            BasicAuthUsers = command.BasicAuthUsers is null ? [] : command.BasicAuthUsers,
            IpRules = command.IpRules is null ? [] : command.IpRules,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<SiteOptionsDto>>(command, cancellationToken));
    }
}
