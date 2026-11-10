using Maran.Modules.ServerMigration.Commands.PullExportHead;
using Maran.Modules.ServerMigration.Commands.PullExportPackage;
using Maran.Modules.ServerMigration.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.ServerMigration.Controllers;

/// <summary>
/// The two endpoints another panel reaches with a single-use export grant: the head of a package, and the package
/// (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reached ONLY with the <c>MigrationGrant</c> scheme and the <see cref="MigrationGrantPolicyNames.Pull"/>
/// policy</b>, which admit a live grant and nothing else: no login, role or API key satisfies it, and a grant
/// satisfies no other policy. Which export is served is decided by the grant's own claim, never by a route, query
/// or body value, so a grant for one export cannot be aimed at another.
/// </para>
/// <para>
/// <b>Every refusal is one 404-shaped answer</b> (rules/security.md item 6). A wrong, expired, used, revoked or
/// address-refused grant is turned away earlier by the authentication handler with a bodyless 401 that says
/// nothing about which; what reaches the actions here can fail only as not found.
/// </para>
/// <para>
/// A grant is not a login, so view-as does not apply; the declaration is <see cref="ViewAsAccess.Refused"/> as for
/// every key-reached controller. The caller is anonymous to every other module and sees no tenant row.
/// </para>
/// </remarks>
[Route("api/v1/migration-pull")]
[Authorize(AuthenticationSchemes = MigrationGrantDefaults.Scheme, Policy = MigrationGrantPolicyNames.Pull)]
[Tags("Migration Pull")]
[Produces("application/octet-stream")]
[EnableRateLimiting(RateLimitPolicies.MigrationPull)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class MigrationPullController : BaseApiController
{
    /// <summary>The message bus commands are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, a grant.</param>
    /// <param name="bus">The message bus commands are dispatched through.</param>
    public MigrationPullController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Serves the head of the grant's package: the signed header and manifest, and no artifact byte.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>Counted against the grant, which allows a few of these before the artifact is read.</remarks>
    [HttpGet("head")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> HeadAsync(CancellationToken cancellationToken)
    {
        Response.ContentType = "application/octet-stream";
        Response.Headers.CacheControl = "no-store";

        var served = await _bus.InvokeAsync<Result>(
            new PullExportHeadCommand(GrantId(), Destination(), ClientIpAddress, CallerUserAgent),
            cancellationToken);

        // Once the body has started the status cannot change; a failure there is a truncated head, which the
        // receiving reader refuses.
        return Response.HasStarted ? new EmptyResult() : ToActionResult(served);
    }

    /// <summary>Serves the grant's whole package, streamed as the agent reads it. The grant allows this once.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// An interrupted pull needs a new grant: the single read is taken at the first byte, so a grant half used is
    /// a grant a thief may have half used.
    /// </remarks>
    [HttpGet("package")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PackageAsync(CancellationToken cancellationToken)
    {
        Response.ContentType = "application/octet-stream";
        Response.Headers.CacheControl = "no-store";

        var served = await _bus.InvokeAsync<Result>(
            new PullExportPackageCommand(GrantId(), Destination(), ClientIpAddress, CallerUserAgent),
            cancellationToken);

        return Response.HasStarted ? new EmptyResult() : ToActionResult(served);
    }

    /// <summary>Reads the id of the grant that authenticated the request from the claim only the grant handler writes.</summary>
    /// <returns>The grant's id.</returns>
    /// <remarks>
    /// The policy requires the claim, so an authenticated request always has one; an unreadable value is a
    /// defect and answers <see cref="Guid.Empty"/>, which no grant has, so the handler refuses it as not found.
    /// </remarks>
    private Guid GrantId()
    {
        return Guid.TryParse(User.FindFirst(PanelClaimTypes.MigrationGrantId)?.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>Where a pulled part goes: the response body, with the size announced before the first byte.</summary>
    /// <returns>The destination.</returns>
    private PackageDestination Destination()
    {
        return new PackageDestination(Response.Body, length => { Response.ContentLength = length; });
    }
}
