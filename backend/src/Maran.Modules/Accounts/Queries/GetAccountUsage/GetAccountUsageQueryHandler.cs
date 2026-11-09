using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetAccountUsage;

/// <summary>
/// Handles <see cref="GetAccountUsageQuery"/>: the account's state and plan from its row, the disk it
/// occupies from the agent, and the resource counts from <see cref="AccountResourceSummaryReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The counts are the summary reader's, not this handler's.</b> The reader decides which caller may
/// be shown a count and answers <c>unavailable</c> for the rest; this handler repeats none of that, so a
/// caller shown nothing by the modules that own the resources cannot be handed a zero here.
/// </para>
/// <para>
/// <b>A disk figure that could not be read is a failure, never a number.</b> The agent's own typed error
/// is returned, so a usage of zero cannot stand in for a measurement that did not happen.
/// </para>
/// </remarks>
public sealed class GetAccountUsageQueryHandler
{
    /// <summary>The number of bytes in a megabyte of a plan's disk quota, as the agent's quota calls count it.</summary>
    private const ulong BytesPerMegabyte = 1024UL * 1024UL;

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which measures the disk every account occupies.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Builds the resource counts beside the plan's limits.</summary>
    private readonly AccountResourceSummaryReader _summary;

    /// <summary>Names the account's state and plan in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent, which measures the disk every account occupies.</param>
    /// <param name="summary">Builds the resource counts beside the plan's limits.</param>
    /// <param name="displayNames">Names the account's state and plan in the request's culture.</param>
    public GetAccountUsageQueryHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        AccountResourceSummaryReader summary,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _agent = agent;
        _summary = summary;
        _displayNames = displayNames;
    }

    /// <summary>Returns the account's use, or a typed failure.</summary>
    /// <param name="query">Which account to read.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// The account's use; <c>AccountNotFound</c> — also for an account another reseller owns, which the
    /// context's filter hides; or the agent's typed failure when the disk could not be measured.
    /// </returns>
    public async Task<Result<AccountUsageDto>> HandleAsync(GetAccountUsageQuery query, CancellationToken cancellationToken)
    {
        var found = await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == query.AccountId)
            .Join(_dbContext.Plans.AsNoTracking(), a => a.PlanId, p => p.Id, (a, p) => new { Account = a, Plan = p })
            .SingleOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return Result<AccountUsageDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var summary = await _summary.ReadAsync(query.AccountId, cancellationToken);
        if (!summary.IsSuccess)
        {
            return Result<AccountUsageDto>.Fail(summary.Error!);
        }

        var usage = await _agent.GetUsageAsync(found.Account.Name, cancellationToken);
        if (!usage.IsSuccess)
        {
            return Result<AccountUsageDto>.Fail(usage.Error!);
        }

        return Result<AccountUsageDto>.Ok(new AccountUsageDto(
            found.Account.Id,
            found.Account.Status,
            AccountStatusMapper.NameOf(found.Account.Status, _displayNames),
            found.Plan.Id,
            PlanMapper.DisplayNameOf(found.Plan, _displayNames),
            new AccountDiskUsageDto(usage.Value.UsedBytes, (ulong)found.Plan.DiskQuotaMb * BytesPerMegabyte),
            summary.Value.Resources));
    }
}
