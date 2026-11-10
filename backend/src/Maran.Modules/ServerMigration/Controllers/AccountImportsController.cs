using Maran.Modules.ServerMigration.Commands.ClaimAccountImportSecrets;
using Maran.Modules.ServerMigration.Commands.DiscardAccountImport;
using Maran.Modules.ServerMigration.Commands.PreviewAccountPull;
using Maran.Modules.ServerMigration.Commands.PullAccountImport;
using Maran.Modules.ServerMigration.Commands.StageAccountImport;
using Maran.Modules.ServerMigration.Commands.StartAccountImport;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Queries.GetAccountImport;
using Maran.Modules.ServerMigration.Queries.GetAccountImportReport;
using Maran.Modules.ServerMigration.Queries.ListAccountImports;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.ServerMigration.Controllers;

/// <summary>
/// Imports an account from a package made by another Maran server: accepts the upload, shows the dry run,
/// starts the job and hands over the credentials it generated once (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and not a tenant surface.</b> An import creates an account, its logins and its
/// databases, so a customer or reseller token is refused by <see cref="AuthorizationPolicies.AdminOnly"/>, and
/// view-as is refused on the whole class. A package names its account but can never name an existing one as its
/// destination: the target account's id is made here.
/// </para>
/// <para>
/// <b>The upload is read as a stream and never buffered.</b> Its length is bounded by what the signed manifest
/// declares and by the agent's own ceiling, not by the framework's request-body limit, which is lifted for this
/// one action. The signature is verified before one byte of the artifact is forwarded.
/// </para>
/// </remarks>
[Route("api/v1/account-imports")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Account Imports")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class AccountImportsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AccountImportsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Accepts an uploaded package after verifying it against the fingerprint the administrator confirmed.</summary>
    /// <param name="fingerprint">The fingerprint of the sending server's key, as read on that server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Consumes("application/octet-stream")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(AccountImportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StageAsync([FromQuery] string fingerprint, CancellationToken cancellationToken)
    {
        var command = new StageAccountImportCommand(fingerprint, Request.Body, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportDto>>(command, cancellationToken));
    }

    /// <summary>Lists the imports, one page at a time, optionally searched and narrowed to one state.</summary>
    /// <param name="query">Paging, search, sort and state, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AccountImportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListAccountImportsQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<AccountImportDto>>>(query, cancellationToken));
    }

    /// <summary>Reads the head of another panel's package with its grant and verifies it, spending nothing but a counted preview.</summary>
    /// <param name="command">The other panel's address, the grant and the fingerprint read on that panel.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("pull-preview")]
    [ProducesResponseType(typeof(AccountPullPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PreviewPullAsync([FromBody] PreviewAccountPullCommand command, CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<AccountPullPreviewDto>>(command, cancellationToken));
    }

    /// <summary>Pulls a package from another panel with its grant and stages it as an upload is. The grant allows this once.</summary>
    /// <param name="command">The other panel's address, the grant and the fingerprint read on that panel.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("pull")]
    [ProducesResponseType(typeof(AccountImportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PullAsync([FromBody] PullAccountImportCommand command, CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportDto>>(command, cancellationToken));
    }

    /// <summary>Reads one import.</summary>
    /// <param name="id">The import.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountImportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportDto>>(new GetAccountImportQuery(id), cancellationToken));
    }

    /// <summary>Judges a staged import against this server and changes nothing.</summary>
    /// <param name="id">The import.</param>
    /// <param name="query">The plan the account would be created against.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/report")]
    [ProducesResponseType(typeof(AccountImportReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportAsync(
        Guid id,
        [FromQuery] GetAccountImportReportQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { ImportId = id };

        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportReportDto>>(query, cancellationToken));
    }

    /// <summary>Starts the job that creates the account. The administrator confirms the name and the key fingerprint.</summary>
    /// <param name="id">The import.</param>
    /// <param name="command">The plan, the optional reseller and the two confirmations.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(AccountImportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartAsync(
        Guid id,
        [FromBody] StartAccountImportCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { ImportId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportDto>>(command, cancellationToken));
    }

    /// <summary>Discards a staged import without starting it.</summary>
    /// <param name="id">The import.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(AccountImportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DiscardAccountImportCommand(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<AccountImportDto>>(command, cancellationToken));
    }

    /// <summary>Returns once the credentials the import generated for the databases and logins it created.</summary>
    /// <param name="id">The import.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/secrets")]
    [ProducesResponseType(typeof(IReadOnlyList<IssuedSecretDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClaimSecretsAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ClaimAccountImportSecretsCommand(id);

        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<IssuedSecretDto>>>(command, cancellationToken));
    }
}
