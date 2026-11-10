using Maran.Modules.ServerMigration.Commands.AddRemoteServer;
using Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;
using Maran.Modules.ServerMigration.Commands.DiscoverRemoteServer;
using Maran.Modules.ServerMigration.Commands.EditRemoteServer;
using Maran.Modules.ServerMigration.Commands.RemoveRemoteServer;
using Maran.Modules.ServerMigration.Commands.RescanRemoteServerHostKeys;
using Maran.Modules.ServerMigration.Commands.TestRemoteServer;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Queries.GetDiscoveredAccount;
using Maran.Modules.ServerMigration.Queries.GetRemoteServer;
using Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;
using Maran.Modules.ServerMigration.Queries.ListRemoteServers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.ServerMigration.Controllers;

/// <summary>
/// HTTP surface for the remote servers an administrator registers as the source of a migration: registering one,
/// confirming its host key, trying the connection, and reading what is on it. Thin by design: binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and a signed-in customer or reseller is answered 403.</b> A source server is the
/// operator's own machine room; it has no tenant dimension to hide, so there is nothing a 403 could confirm, and a
/// 404 would be the tenant answer worn in the wrong place (rules/security.md item 6). View-as is refused on the
/// whole class: no customer token can reach it, and an operator viewing the panel as a customer must not drive an
/// outbound connection.
/// </para>
/// <para>
/// <b>No response carries the private key.</b> The panel's key for a server is made by the agent, stored encrypted
/// and sent back to the agent only for a call that connects; the detail carries the PUBLIC key and the line to
/// install. A request carries no credential either: the administrator installs the line themselves.
/// </para>
/// </remarks>
[Route("api/v1/remote-servers")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Remote Servers")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class RemoteServersController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public RemoteServersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the registered remote servers.</summary>
    /// <param name="query">Paging, search, sort and the optional state filter, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RemoteServerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListRemoteServersQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<RemoteServerDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one registered remote server in full.</summary>
    /// <param name="id">The server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(
            new GetRemoteServerQuery(id), cancellationToken));
    }

    /// <summary>
    /// Registers a remote server: the agent makes the panel's key for it and reads the host keys it offers, and the
    /// server is stored awaiting the administrator's confirmation of one of them.
    /// </summary>
    /// <param name="command">
    /// Where the server is and the login to use. The audit fields it carries are stamped here from the connection and
    /// are not part of the request contract.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] AddRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/remote-servers/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Changes a server's label or where the panel connects to it. A new host, port or login withdraws the confirmed
    /// host key.
    /// </summary>
    /// <param name="id">The server.</param>
    /// <param name="command">The new label, address, port, login and key restriction.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceAsync(
        Guid id,
        [FromBody] EditRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { ServerId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(command, cancellationToken));
    }

    /// <summary>Reads the host keys the server offers now and replaces the candidates. Never pins one.</summary>
    /// <param name="id">The server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/host-keys/scan")]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RescanHostKeysAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RescanRemoteServerHostKeysCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(command, cancellationToken));
    }

    /// <summary>Pins the host key whose fingerprint the administrator confirmed.</summary>
    /// <param name="id">The server.</param>
    /// <param name="command">The fingerprint to pin, as the administrator typed it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/host-key")]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmHostKeyAsync(
        Guid id,
        [FromBody] ConfirmRemoteServerHostKeyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { ServerId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(command, cancellationToken));
    }

    /// <summary>Tries the connection with the pinned host key and records what the probes found, or why it refused.</summary>
    /// <param name="id">The server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(typeof(RemoteServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new TestRemoteServerCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<RemoteServerDetailDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Removes a server with everything stored about it, and answers with the panel's public key that is still
    /// installed on it, for the administrator to take out.
    /// </summary>
    /// <param name="id">The server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(RemovedRemoteServerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RemoveRemoteServerCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<RemovedRemoteServerDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Reads the server's accounts, sites, databases and scheduled jobs and replaces what the panel knew of it.
    /// Read-only on the server.
    /// </summary>
    /// <param name="id">The server.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/discover")]
    [ProducesResponseType(typeof(DiscoveryResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DiscoverAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DiscoverRemoteServerCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<DiscoveryResultDto>>(command, cancellationToken));
    }

    /// <summary>Lists one page of the accounts a discovery found on a server.</summary>
    /// <param name="id">The server.</param>
    /// <param name="query">Paging, search, sort and the optional <c>has</c> filter, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/accounts")]
    [ProducesResponseType(typeof(PagedResult<DiscoveredAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountsAsync(
        Guid id,
        [FromQuery] ListDiscoveredAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        query = query with { ServerId = id };

        return ToActionResult(
            await _bus.InvokeAsync<Result<PagedResult<DiscoveredAccountDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one discovered account with its sites and databases.</summary>
    /// <param name="id">The server.</param>
    /// <param name="accountId">The discovered account.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/accounts/{accountId:guid}")]
    [ProducesResponseType(typeof(DiscoveredAccountDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountAsync(Guid id, Guid accountId, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<DiscoveredAccountDetailDto>>(
            new GetDiscoveredAccountQuery(id, accountId), cancellationToken));
    }
}
