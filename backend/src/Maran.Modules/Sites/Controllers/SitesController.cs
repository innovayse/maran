using Maran.Modules.Sites.Commands.ChangeSitePhpVersion;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Commands.DeleteSite;
using Maran.Modules.Sites.Commands.DisableSite;
using Maran.Modules.Sites.Commands.EnableSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Controllers.Requests;
using Maran.Modules.Sites.Queries.GetSite;
using Maran.Modules.Sites.Queries.ListPhpVersions;
using Maran.Modules.Sites.Queries.ListSites;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sites.Controllers;

/// <summary>
/// HTTP surface for websites. Thin by design (rules/csharp.md "Controller shape is fixed"): binds
/// the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No business
/// logic, no data access.
///
/// Open to any signed-in caller, because a site belongs to a customer and a customer manages their
/// own. What they can SEE is not decided here: every read and every mutation goes through
/// <c>SitesDbContext</c>, whose global query filter scopes rows to the caller's account, so a site
/// belonging to somebody else answers 404 — never 403, which would confirm it exists (spec §8,
/// rules/testing.md item 3).
/// </summary>
[Route("api/v1/sites")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Sites")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SitesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Resolves a log-tail request and produces its frames.</summary>
    private readonly SiteLogTailService _logTail;

    /// <summary>Writes those frames to the caller as server-sent events.</summary>
    private readonly SiteLogStreamWriter _logStreamWriter;

    /// <summary>Creates the controller with the caller identity, the message bus and the log tail.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    /// <param name="logTail">Resolves a log-tail request and produces its frames.</param>
    /// <param name="logStreamWriter">Writes those frames to the caller as server-sent events.</param>
    public SitesController(
        ICurrentUser currentUser,
        IMessageBus bus,
        SiteLogTailService logTail,
        SiteLogStreamWriter logStreamWriter)
        : base(currentUser)
    {
        _bus = bus;
        _logTail = logTail;
        _logStreamWriter = logStreamWriter;
    }

    /// <summary>Lists the sites the caller may see.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SiteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<SiteDto>>>(new ListSitesQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one site. Another customer's site answers 404, not 403.</summary>
    /// <param name="id">The site to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SiteDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<SiteDetailDto>>(new GetSiteQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Creates a site: its document root, vhost and pool on the host, then the row.</summary>
    /// <param name="request">The site's account, domain, aliases and backend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateSiteCommand(
            request.AccountId,
            request.Domain,
            request.Aliases ?? [],
            request.BackendType,
            request.PhpVersion ?? string.Empty,
            request.ProxyUpstream ?? string.Empty,
            ClientIpAddress,
            UserAgent());

        var result = await _bus.InvokeAsync<Result<SiteDto>>(command, cancellationToken);
        return ToCreatedActionResult(result, $"/api/v1/sites/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Rebinds a site to a different installed PHP version.</summary>
    /// <param name="id">The site to rebind.</param>
    /// <param name="request">The version to switch to.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/php-version")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePhpVersionAsync(
        Guid id,
        [FromBody] ChangeSitePhpVersionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ChangeSitePhpVersionCommand(id, request.PhpVersion, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<SiteDto>>(command, cancellationToken));
    }

    /// <summary>Returns a disabled site to serving its own content. Idempotent.</summary>
    /// <param name="id">The site to enable.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnableAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new EnableSiteCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<SiteDto>>(command, cancellationToken));
    }

    /// <summary>Makes a site serve a suspension response instead of its content. Idempotent.</summary>
    /// <param name="id">The site to disable.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DisableSiteCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<SiteDto>>(command, cancellationToken));
    }

    /// <summary>Removes a site's vhost and its row. The customer's files are left alone.</summary>
    /// <param name="id">The site to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteSiteCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Lists the PHP versions installed on this server — the reference data the site form selects
    /// from, so a caller never types a version the host does not have (rules/architecture.md "The
    /// backend owns the data, the SPA renders it"). Host-level, not per-account: a version is
    /// installed once and bound to any number of sites.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("php-versions")]
    [ProducesResponseType(typeof(IReadOnlyList<PhpVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPhpVersionsAsync(CancellationToken cancellationToken)
    {
        var query = new ListPhpVersionsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<PhpVersionDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Tails one of a site's logs as server-sent events: <c>line</c> events, then exactly one
    /// <c>end</c> event naming why the stream stopped. Another customer's site answers 404, not 403.
    /// </summary>
    /// <remarks>
    /// The stream is bound to this request. When the caller disconnects, the request's cancellation
    /// token stops the agent's tail with it, so an abandoned pane leaves neither an open connection
    /// nor a reader running behind it.
    ///
    /// The ending is never omitted and never softened. A dropped or idle stream reaches the operator
    /// under its own name because the alternative — a pane that silently stops updating — reads
    /// exactly like a log with nothing more to say.
    ///
    /// It carries its OWN rate-limit policy, which replaces the controller's <c>api</c> one for this
    /// action: the question a tail raises is not how fast a customer opens streams but how many they
    /// hold open, because each one pins a blocking thread in the root daemon out of a pool shared by
    /// every other tenant's operations. A fixed window cannot express that and a concurrency limiter
    /// can (see <c>SiteLogStreamRateLimitPolicy</c>). Over the limit is 429, not a queued connection.
    /// </remarks>
    /// <param name="id">The site whose log to read.</param>
    /// <param name="request">Which log, and how much of its history to replay.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    [HttpGet("{id:guid}/logs")]
    [EnableRateLimiting(RateLimitPolicies.SiteLogs)]
    [Produces(SiteLogStreamWriter.EventStreamContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetLogsAsync(
        Guid id,
        [FromQuery] TailSiteLogRequest request,
        CancellationToken cancellationToken)
    {
        var target = await _logTail.ResolveAsync(
            id, request.Source, request.HistoryLines, ClientIpAddress, UserAgent(), cancellationToken);
        if (!target.IsSuccess)
        {
            return ToActionResult(target);
        }

        // Written directly to the response rather than returned as a value, because the body is
        // produced over time: an IActionResult carrying a materialized value would have to wait for
        // a stream that only ends when the operator stops watching.
        await _logStreamWriter.WriteAsync(Response, _logTail.ReadAsync(target.Value, cancellationToken), cancellationToken);
        return new EmptyResult();
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
