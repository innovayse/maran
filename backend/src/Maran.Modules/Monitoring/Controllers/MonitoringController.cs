using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Queries.GetHostMetrics;
using Maran.Modules.Monitoring.Queries.GetMetricsChart;
using Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Monitoring.Controllers;

/// <summary>
/// HTTP surface for what the server is doing: a live reading, the services' states, and the charts.
/// Thin by design (rules/csharp.md "Controller shape is fixed"): binds the request, dispatches
/// through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only, one class-level <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c>,
/// mirroring the Firewall module's gating. A signed-in customer is answered 403 and an anonymous
/// caller 401. How much memory the host has, which services are up and how full its disk is are
/// facts about the machine every customer shares — they are not a tenant's data, and there is no
/// tenant dimension to scope them by.
/// </remarks>
[Route("api/v1/monitoring")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Monitoring")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MonitoringController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public MonitoringController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the host's resource use right now, live from the agent.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// Live rather than the newest stored sample: the dashboard's question is what the machine is
    /// doing, and the table lags by up to a whole sampling interval — and holds nothing at all on a
    /// panel whose sampler has not run yet.
    /// </remarks>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(HostMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var query = new GetHostMetricsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<HostMetricsDto>>(query, cancellationToken));
    }

    /// <summary>Reads whether each service the agent watches is up, down, or not known.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A service with no row is one the agent does not watch, and the interface must read that as
    /// "not known" rather than as "not running" — the absence is the answer.
    /// </remarks>
    [HttpGet("services")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetServicesAsync(CancellationToken cancellationToken)
    {
        var query = new ListServiceStatusesQuery();
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<ServiceStatusDto>>>(query, cancellationToken));
    }

    /// <summary>Reads the stored samples for a range, bucketed into points a chart can draw.</summary>
    /// <param name="range">How far back the chart reaches; the bucket width follows from it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// An empty bucket list is a 200, not a 404: a panel installed ten minutes ago simply has no
    /// samples, and the interface draws its empty state.
    /// </remarks>
    [HttpGet("chart")]
    [ProducesResponseType(typeof(MetricsChartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetChartAsync(
        [FromQuery] ChartRange range,
        CancellationToken cancellationToken)
    {
        var query = new GetMetricsChartQuery(range);
        return ToActionResult(await _bus.InvokeAsync<Result<MetricsChartDto>>(query, cancellationToken));
    }

    /// <summary>Reads what every hosting account occupies on disk, beside what its plan allows.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// <para>
    /// Host-wide and administrators only, like every route on this controller — but this one is the
    /// route on which that gate is the ONLY thing standing between a customer and every other
    /// tenant's system user name and plan allowances. The query behind it reaches
    /// <c>IAccountDirectory.ListAsync</c>, which applies no tenant scope by contract, so the
    /// class-level policy is not a formality here.
    /// </para>
    /// <para>
    /// An account with no usage figure is one the agent did not report, and it comes back with a null
    /// rather than a zero: "we have not measured this" and "this account holds nothing" are different
    /// answers and the interface draws them differently.
    /// </para>
    /// </remarks>
    [HttpGet("accounts-disk")]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDiskUsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAccountsDiskAsync(CancellationToken cancellationToken)
    {
        var query = new ListAccountDiskUsageQuery();
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<AccountDiskUsageDto>>>(query, cancellationToken));
    }
}
