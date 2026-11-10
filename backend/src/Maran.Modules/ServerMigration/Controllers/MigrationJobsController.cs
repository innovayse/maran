using Maran.Modules.ServerMigration.Commands.ClaimMigrationAccountSecrets;
using Maran.Modules.ServerMigration.Commands.ControlMigrationAccount;
using Maran.Modules.ServerMigration.Commands.ControlMigrationJob;
using Maran.Modules.ServerMigration.Commands.CreateMigrationJob;
using Maran.Modules.ServerMigration.Commands.DryRunMigrationJob;
using Maran.Modules.ServerMigration.Commands.SaveMigrationMapping;
using Maran.Modules.ServerMigration.Commands.SetMigrationLimits;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Queries.ExportMigrationSummary;
using Maran.Modules.ServerMigration.Queries.GetMigrationAccountReport;
using Maran.Modules.ServerMigration.Queries.GetMigrationDnsAdvice;
using Maran.Modules.ServerMigration.Queries.GetMigrationJob;
using Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;
using Maran.Modules.ServerMigration.Queries.ListMigrationJobs;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.ServerMigration.Controllers;

/// <summary>
/// HTTP surface for whole-server migration jobs (issue #197, phase 2): choosing a registered server's accounts,
/// mapping them, the dry run, the parallel two-phase transfer, the cut-over, the verification report, the DNS helper
/// and the summary. Thin by design: binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only, for the reasons <see cref="RemoteServersController"/> states, and view-as is refused on the
/// whole class. No response carries the source's key, a pull grant or a generated password; the passwords are claimed
/// once through their own endpoint.
/// </remarks>
[Route("api/v1/migration-jobs")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Migration Jobs")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class MigrationJobsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <param name="bus">The message bus.</param>
    public MigrationJobsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the migration jobs.</summary>
    /// <param name="query">Paging, search, sort and state filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MigrationJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListMigrationJobsQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<MigrationJobDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one job.</summary>
    /// <param name="id">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MigrationJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<MigrationJobDto>>(new GetMigrationJobQuery(id), cancellationToken));
    }

    /// <summary>Creates a draft job from accounts a registered server's discovery found.</summary>
    /// <param name="command">The accounts and settings; the audit fields are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(MigrationJobDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateMigrationJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<MigrationJobDto>>(command, cancellationToken);

        return ToCreatedActionResult(result, $"/api/v1/migration-jobs/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Lists one page of a job's accounts.</summary>
    /// <param name="id">The job.</param>
    /// <param name="query">Paging, search, sort and state filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}/accounts")]
    [ProducesResponseType(typeof(PagedResult<MigrationJobAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountsAsync(
        Guid id,
        [FromQuery] ListMigrationJobAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        query = query with { JobId = id };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<MigrationJobAccountDto>>>(query, cancellationToken));
    }

    /// <summary>Maps accounts of a draft job to a plan and an owner.</summary>
    /// <param name="id">The job.</param>
    /// <param name="command">The mapping; the audit fields are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:guid}/mapping")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MapAsync(Guid id, [FromBody] SaveMigrationMappingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { JobId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<int>>(command, cancellationToken));
    }

    /// <summary>The dry run: every conflict and warning, changing nothing.</summary>
    /// <param name="id">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/dry-run")]
    [ProducesResponseType(typeof(MigrationJobReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DryRunAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DryRunMigrationJobCommand(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<MigrationJobReportDto>>(command, cancellationToken));
    }

    /// <summary>Starts, pauses, resumes or cancels the job.</summary>
    /// <param name="id">The job.</param>
    /// <param name="command">The action; the audit fields are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/control")]
    [ProducesResponseType(typeof(MigrationJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ControlAsync(Guid id, [FromBody] ControlMigrationJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { JobId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MigrationJobDto>>(command, cancellationToken));
    }

    /// <summary>Changes the concurrency and the bandwidth limit while the job runs.</summary>
    /// <param name="id">The job.</param>
    /// <param name="command">The limits; the audit fields are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:guid}/limits")]
    [ProducesResponseType(typeof(MigrationJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetLimitsAsync(Guid id, [FromBody] SetMigrationLimitsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { JobId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MigrationJobDto>>(command, cancellationToken));
    }

    /// <summary>Retries, cancels or cuts over one account of the job.</summary>
    /// <param name="id">The job.</param>
    /// <param name="accountId">The account row.</param>
    /// <param name="command">The action; the audit fields are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/accounts/{accountId:guid}/control")]
    [ProducesResponseType(typeof(MigrationJobAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ControlAccountAsync(
        Guid id,
        Guid accountId,
        [FromBody] ControlMigrationAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = command with { JobId = id, AccountId = accountId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MigrationJobAccountDto>>(command, cancellationToken));
    }

    /// <summary>The comparison report of one account's cut-over.</summary>
    /// <param name="id">The job.</param>
    /// <param name="accountId">The account row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}/accounts/{accountId:guid}/report")]
    [ProducesResponseType(typeof(MigrationAccountReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportAsync(Guid id, Guid accountId, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<MigrationAccountReportDto>>(
            new GetMigrationAccountReportQuery(id, accountId), cancellationToken));
    }

    /// <summary>Claims, once, the credentials the migration generated for one account.</summary>
    /// <param name="id">The job.</param>
    /// <param name="accountId">The account row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/accounts/{accountId:guid}/secrets")]
    [ProducesResponseType(typeof(IReadOnlyList<IssuedSecretDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClaimSecretsAsync(Guid id, Guid accountId, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<IssuedSecretDto>>>(
            new ClaimMigrationAccountSecretsCommand(id, accountId), cancellationToken));
    }

    /// <summary>Where the job's domains resolve now, from this server.</summary>
    /// <param name="id">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}/dns")]
    [ProducesResponseType(typeof(MigrationDnsAdviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDnsAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<MigrationDnsAdviceDto>>(new GetMigrationDnsAdviceQuery(id), cancellationToken));
    }

    /// <summary>The summary of the job as a CSV or JSON file.</summary>
    /// <param name="id">The job.</param>
    /// <param name="json">Whether the file is JSON instead of CSV.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummaryAsync(Guid id, [FromQuery] bool json, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<MigrationSummaryFileDto>>(new ExportMigrationSummaryQuery(id, json), cancellationToken);

        return result.IsSuccess
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : ToActionResult(result);
    }
}
