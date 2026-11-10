using Maran.Modules.ServerMigration.Commands.CreateAccountExport;
using Maran.Modules.ServerMigration.Commands.DownloadAccountExport;
using Maran.Modules.ServerMigration.Commands.IssueExportGrant;
using Maran.Modules.ServerMigration.Commands.RevokeExportGrant;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Queries.GetAccountExport;
using Maran.Modules.ServerMigration.Queries.ListAccountExports;
using Maran.Modules.ServerMigration.Queries.ListExportGrants;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.ServerMigration.Controllers;

/// <summary>
/// Exports an account for another Maran server: makes the export, hands its package to the administrator, and
/// issues the single-use grants with which another panel pulls it (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and not a tenant surface.</b> An export is a copy of a customer's data leaving the
/// server, so the policy is <see cref="AuthorizationPolicies.AdminOnly"/> and a customer or reseller token is
/// refused by it. View-as is refused on the whole class: an operator viewing the panel as a login must not
/// export that login's account.
/// </para>
/// <para>
/// The package is plaintext at rest on the administrator's disk and in the agent's backup root until the export
/// expires, and the screen says so. No response carries the signing key; the token of a grant is returned once,
/// by the action that issues it.
/// </para>
/// </remarks>
[Route("api/v1/account-exports")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Account Exports")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class AccountExportsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AccountExportsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Exports one account: takes the backup and gathers what every module hands over.</summary>
    /// <param name="command">The account to export.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(AccountExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateAccountExportCommand command, CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<AccountExportDto>>(command, cancellationToken));
    }

    /// <summary>Reads one export.</summary>
    /// <param name="id">The export.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<AccountExportDto>>(new GetAccountExportQuery(id), cancellationToken));
    }

    /// <summary>Downloads the package of an export, streamed as it is made.</summary>
    /// <param name="id">The export.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/package")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        // Looked up first, so a missing or expired export is a problem document and not the start of a stream.
        var export = await _bus.InvokeAsync<Result<AccountExportDto>>(new GetAccountExportQuery(id), cancellationToken);

        if (!export.IsSuccess)
        {
            return ToActionResult(export);
        }

        Response.ContentType = "application/octet-stream";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{export.Value!.AccountName}.maranpkg\"";
        var written = await _bus.InvokeAsync<Result>(
            new DownloadAccountExportCommand(id, Destination(), ClientIpAddress, CallerUserAgent),
            cancellationToken);

        // Once the body has started the status cannot change; a failure there is a truncated package, which
        // the receiving side refuses.
        return Response.HasStarted ? new EmptyResult() : ToActionResult(written);
    }

    /// <summary>Lists the exports, one page at a time, optionally searched by the exported account's name.</summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AccountExportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListAccountExportsQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<AccountExportDto>>>(query, cancellationToken));
    }

    /// <summary>Lists the pull grants, one page at a time, optionally searched by account name and narrowed to one state. The token is never part of a row.</summary>
    /// <param name="query">Paging, search, sort and state, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("grants")]
    [ProducesResponseType(typeof(PagedResult<ExportGrantSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetGrantsAsync([FromQuery] ListExportGrantsQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ExportGrantSummaryDto>>>(query, cancellationToken));
    }

    /// <summary>Issues a single-use grant with which another panel pulls the package. The token is returned once.</summary>
    /// <param name="id">The export.</param>
    /// <param name="command">The grant's bounds.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/grants")]
    [ProducesResponseType(typeof(ExportGrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueGrantAsync(
        Guid id,
        [FromBody] IssueExportGrantCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { ExportId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<ExportGrantDto>>(command, cancellationToken));
    }

    /// <summary>Revokes a grant before it is used or expires.</summary>
    /// <param name="grantId">The grant.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("grants/{grantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeGrantAsync(Guid grantId, CancellationToken cancellationToken)
    {
        var command = new RevokeExportGrantCommand(grantId, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Where the package of a download goes: the response body, with the size announced before the first byte.</summary>
    /// <returns>The destination; the response then carries a <c>Content-Length</c> and the browser can show progress.</returns>
    private PackageDestination Destination()
    {
        return new PackageDestination(Response.Body, length => { Response.ContentLength = length; });
    }
}
