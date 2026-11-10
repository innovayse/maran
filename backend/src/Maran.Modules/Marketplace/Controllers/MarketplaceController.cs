using Maran.Modules.Marketplace.Commands.InstallModule;
using Maran.Modules.Marketplace.Commands.RemoveModule;
using Maran.Modules.Marketplace.Commands.RestartForModule;
using Maran.Modules.Marketplace.Commands.RollbackModule;
using Maran.Modules.Marketplace.Commands.SetMarketplaceEnabled;
using Maran.Modules.Marketplace.Commands.UpdateModule;
using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Queries.CheckUpdates;
using Maran.Modules.Marketplace.Queries.GetMarketplaceStatus;
using Maran.Modules.Marketplace.Queries.GetModuleDetails;
using Maran.Modules.Marketplace.Queries.ListCatalogue;
using Maran.Modules.Marketplace.Queries.ListInstalled;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Marketplace.Controllers;

/// <summary>
/// The administrator's door to the module marketplace: the switch, the catalogue, the installed modules,
/// the update check, and install, update, remove, rollback and restart. Administrator only: no customer,
/// reseller or API key reaches any route, and the module is not offered to them in the module catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose. Every decision, from whether a version can be installed to whether a password is
/// right, is made by the command or query it dispatches. Install, update, remove and rollback answer 202
/// with the task that carries the work; nothing is applied before the answer, and a staged change takes
/// effect at the next panel restart.
/// </para>
/// <para>
/// The administrator-only policy exempts the routes from the view-as allow-list: an operator viewing as a
/// login holds a customer token, which this policy refuses, so no <c>ViewAsAccess</c> declaration applies.
/// </para>
/// </remarks>
[Route("api/v1/marketplace")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Marketplace")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MarketplaceController : BaseApiController
{
    /// <summary>The panel's message bus.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The signed-in administrator's id, or empty when the caller is not one.</summary>
    private Guid AdministratorId
    {
        get
        {
            return CurrentUser.Caller is AdministratorCaller administrator ? administrator.UserId : Guid.Empty;
        }
    }

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <param name="bus">The panel's message bus.</param>
    public MarketplaceController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the switch, the configured host and the modules waiting for a restart.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The marketplace status. Makes no marketplace call.</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(MarketplaceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<MarketplaceStatusDto>>(new GetMarketplaceStatusQuery(), cancellationToken));
    }

    /// <summary>Switches the marketplace on or off; the administrator proves their password again.</summary>
    /// <param name="command">The switch and the password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status after the change.</returns>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(MarketplaceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetEnabledAsync(
        [FromBody] SetMarketplaceEnabledCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { UserId = AdministratorId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MarketplaceStatusDto>>(stamped, cancellationToken));
    }

    /// <summary>Lists one page of the marketplace catalogue.</summary>
    /// <param name="query">The channel, search term and cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page; a catalogue that could not be verified is a 200 with <c>verified: false</c>.</returns>
    [HttpGet("catalogue")]
    [ProducesResponseType(typeof(MarketplaceCatalogueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCatalogueAsync(
        [FromQuery] ListCatalogueQuery query,
        CancellationToken cancellationToken)
    {
        var stamped = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MarketplaceCatalogueDto>>(stamped, cancellationToken));
    }

    /// <summary>Reads one module's details with its versions and the signed manifest summary.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The module's details.</returns>
    [HttpGet("modules/{id}")]
    [ProducesResponseType(typeof(MarketplaceModuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetModuleAsync(string id, CancellationToken cancellationToken)
    {
        var query = new GetModuleDetailsQuery(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<MarketplaceModuleDetailDto>>(query, cancellationToken));
    }

    /// <summary>Lists the modules installed from packages. Makes no marketplace call.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed modules.</returns>
    [HttpGet("installed")]
    [ProducesResponseType(typeof(IReadOnlyList<InstalledModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInstalledAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<InstalledModuleDto>>>(new ListInstalledQuery(), cancellationToken));
    }

    /// <summary>Checks the marketplace for newer versions and withdrawals; the one "check for updates" call.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the check found; an answer that could not be verified is a 200 with <c>verified: false</c>.</returns>
    [HttpGet("updates")]
    [ProducesResponseType(typeof(ModuleUpdatesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CheckUpdatesAsync(CancellationToken cancellationToken)
    {
        var query = new CheckUpdatesQuery(ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<ModuleUpdatesDto>>(query, cancellationToken));
    }

    /// <summary>Starts installing one version of a module as a task.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="command">The version, the acknowledgement and the password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; an identical install already running answers with its task.</returns>
    [HttpPost("modules/{id}/install")]
    [ProducesResponseType(typeof(ModuleOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> InstallAsync(
        string id,
        [FromBody] InstallModuleCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with
        {
            Id = id,
            UserId = AdministratorId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return AcceptedOrFailure(
            await _bus.InvokeAsync<Result<ModuleOperationAcceptedDto>>(stamped, cancellationToken));
    }

    /// <summary>Starts updating an installed module to one version as a task.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="command">The version, the acknowledgement and the password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; an identical update already running answers with its task.</returns>
    [HttpPost("modules/{id}/update")]
    [ProducesResponseType(typeof(ModuleOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateAsync(
        string id,
        [FromBody] UpdateModuleCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with
        {
            Id = id,
            UserId = AdministratorId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return AcceptedOrFailure(
            await _bus.InvokeAsync<Result<ModuleOperationAcceptedDto>>(stamped, cancellationToken));
    }

    /// <summary>Starts removing an installed module as a task. The module's data is kept.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="command">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; a module that is not installed is 404.</returns>
    [HttpPost("modules/{id}/remove")]
    [ProducesResponseType(typeof(ModuleOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAsync(
        string id,
        [FromBody] RemoveModuleCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with
        {
            Id = id,
            UserId = AdministratorId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return AcceptedOrFailure(
            await _bus.InvokeAsync<Result<ModuleOperationAcceptedDto>>(stamped, cancellationToken));
    }

    /// <summary>Starts going back to an installed module's previous version as a task.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="command">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; a module with no previous version is 409.</returns>
    [HttpPost("modules/{id}/rollback")]
    [ProducesResponseType(typeof(ModuleOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RollbackAsync(
        string id,
        [FromBody] RollbackModuleCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with
        {
            Id = id,
            UserId = AdministratorId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return AcceptedOrFailure(
            await _bus.InvokeAsync<Result<ModuleOperationAcceptedDto>>(stamped, cancellationToken));
    }

    /// <summary>Restarts the panel so a staged module takes effect.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="command">An empty body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with no body once the restart is requested; a module that is not installed is 404.</returns>
    [HttpPost("modules/{id}/restart")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestartAsync(
        string id,
        [FromBody] RestartForModuleCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { Id = id };
        var result = await _bus.InvokeAsync<Result>(stamped, cancellationToken);

        return result.IsSuccess ? Accepted() : ToActionResult(result);
    }

    /// <summary>Answers 202 with the accepted task, or the failure's problem.</summary>
    /// <param name="result">The handler's answer.</param>
    /// <returns>The response.</returns>
    private IActionResult AcceptedOrFailure(Result<ModuleOperationAcceptedDto> result)
    {
        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return new ObjectResult(result.Value)
        {
            StatusCode = StatusCodes.Status202Accepted,
            DeclaredType = typeof(ModuleOperationAcceptedDto),
        };
    }
}
