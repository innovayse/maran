using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Cron.Queries.ListCronRuns;

/// <summary>Handles <see cref="ListCronRunsQuery"/>: one page of an entry's runs, searched, filtered and sorted by the database.</summary>
/// <remarks>
/// The account is resolved through <see cref="IAccountDirectory"/> first, so another tenant's account
/// answers 404 exactly as the module's other reads do, and the rows are then held to that account by
/// the tenant filter as well as by the handler's own condition. An entry id with no runs is an empty
/// page, not a 404: the history does not ask the agent whether the entry still exists.
/// </remarks>
public sealed class ListCronRunsQueryHandler
{
    /// <summary>Where the runs are recorded.</summary>
    private readonly CronDbContext _dbContext;

    /// <summary>The one window onto the owning account.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">Where the runs are recorded.</param>
    /// <param name="accounts">The owning account.</param>
    public ListCronRunsQueryHandler(CronDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Reads one page.</summary>
    /// <param name="query">The validated query; see <see cref="ListCronRunsQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<PagedResult<CronRunSummaryDto>>> HandleAsync(
        ListCronRunsQuery query,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<PagedResult<CronRunSummaryDto>>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        IQueryable<CronRun> runs = _dbContext.CronRuns
            .AsNoTracking()
            .Where(run => run.AccountId == account.Id && run.EntryId == query.EntryId);

        if (query.Outcome is { } outcome)
        {
            runs = runs.Where(run => run.Outcome == outcome);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so
            // the term matches literally. Both streams, because an error a customer searches for is as
            // likely on one as on the other.
            var pattern = LikePattern.Contains(query.Search.Value);
            runs = runs.Where(run =>
                EF.Functions.ILike(run.Stdout, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(run.Stderr, pattern, LikePattern.EscapeCharacter));
        }

        var page = await runs.ToPagedAsync(query, ListCronRunsSorts.Map, cancellationToken);

        return Result<PagedResult<CronRunSummaryDto>>.Ok(page.Map(CronRunMapper.Summary));
    }
}
