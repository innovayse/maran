using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.GetMigrationJob;

/// <summary>Handles <see cref="GetMigrationJobQuery"/>.</summary>
public sealed class GetMigrationJobQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public GetMigrationJobQueryHandler(ServerMigrationDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Reads the job.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The job, or <c>MigrationJobNotFound</c>.</returns>
    public async Task<Result<MigrationJobDto>> HandleAsync(GetMigrationJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var job = await _dbContext.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == query.JobId, cancellationToken);

        return job is null
            ? Result<MigrationJobDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound))
            : Result<MigrationJobDto>.Ok(MigrationJobMapper.ToDto(job, await _dbContext.CountStatesAsync(job.Id, cancellationToken), _names));
    }
}
