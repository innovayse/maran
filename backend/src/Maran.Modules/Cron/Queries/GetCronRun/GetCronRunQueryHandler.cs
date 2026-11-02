using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Cron.Queries.GetCronRun;

/// <summary>Handles <see cref="GetCronRunQuery"/>: one run and its output, or 404.</summary>
/// <remarks>
/// A run is found only under the account AND the entry it was recorded for, so a run id copied into
/// another entry's address answers 404 rather than showing that entry a stranger's output.
/// </remarks>
public sealed class GetCronRunQueryHandler
{
    /// <summary>Where the runs are recorded.</summary>
    private readonly CronDbContext _dbContext;

    /// <summary>The one window onto the owning account.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">Where the runs are recorded.</param>
    /// <param name="accounts">The owning account.</param>
    public GetCronRunQueryHandler(CronDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Reads the run.</summary>
    /// <param name="query">The run to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or <c>AccountNotFound</c> or <c>CronRunNotFound</c>.</returns>
    public async Task<Result<CronRunDto>> HandleAsync(GetCronRunQuery query, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<CronRunDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var run = await _dbContext.CronRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == query.RunId && row.AccountId == account.Id && row.EntryId == query.EntryId,
                cancellationToken);

        return run is null
            ? Result<CronRunDto>.Fail(Error.Of(nameof(ErrorMessages.CronRunNotFound), ErrorType.NotFound))
            : Result<CronRunDto>.Ok(CronRunMapper.From(run));
    }
}
