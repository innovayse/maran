using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobs;

/// <summary>Handles <see cref="ListMigrationJobsQuery"/>: the page is a SQL query, not a filter in memory.</summary>
public sealed class ListMigrationJobsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ListMigrationJobsQueryHandler(ServerMigrationDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Reads the page.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page.</returns>
    public async Task<Result<PagedResult<MigrationJobDto>>> HandleAsync(
        ListMigrationJobsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var jobs = _dbContext.MigrationJobs.AsNoTracking();
        if (!query.State.IsNone)
        {
            var state = Enum.GetValues<MigrationJobState>().Cast<MigrationJobState?>()
                .FirstOrDefault(value =>
                {
                    return string.Equals(value.ToString(), query.State.Value, StringComparison.Ordinal);
                });
            jobs = state is null ? jobs.Where(_ => false) : jobs.Where(job => job.State == state);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            jobs = jobs.Where(job => EF.Functions.ILike(job.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await jobs.ToPagedAsync(query, ListMigrationJobsSorts.Map, cancellationToken);
        var counts = new Dictionary<Guid, IReadOnlyDictionary<MigrationAccountState, int>>();
        foreach (var job in page.Items)
        {
            var rows = await _dbContext.MigrationJobAccounts.AsNoTracking()
                .Where(account => account.JobId == job.Id)
                .GroupBy(account => account.State)
                .Select(group => new { State = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            counts[job.Id] = rows.ToDictionary(row =>
            {
                return row.State;
            }, row =>
            {
                return row.Count;
            });
        }

        return Result<PagedResult<MigrationJobDto>>.Ok(page.Map(job => { return MigrationJobMapper.ToDto(job, counts[job.Id], _names); }));
    }
}
