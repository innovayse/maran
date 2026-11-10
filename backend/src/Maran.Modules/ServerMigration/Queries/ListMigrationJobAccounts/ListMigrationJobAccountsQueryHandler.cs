using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;

/// <summary>Handles <see cref="ListMigrationJobAccountsQuery"/>: search and filter are SQL, so a job of thousands of accounts is a page.</summary>
public sealed class ListMigrationJobAccountsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Resolves a failure code to its sentence.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="failureNames">Resolves a failure code to its sentence.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ListMigrationJobAccountsQueryHandler(ServerMigrationDbContext dbContext, AccountImportFailureNames failureNames, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
        _names = names;
    }

    /// <summary>Reads the page.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or <c>MigrationJobNotFound</c>.</returns>
    public async Task<Result<PagedResult<MigrationJobAccountDto>>> HandleAsync(
        ListMigrationJobAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await _dbContext.MigrationJobs.AsNoTracking().AnyAsync(job => job.Id == query.JobId, cancellationToken))
        {
            return Result<PagedResult<MigrationJobAccountDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var accounts = _dbContext.MigrationJobAccounts.AsNoTracking().Where(account => account.JobId == query.JobId);
        if (!query.State.IsNone)
        {
            var state = Enum.GetValues<MigrationAccountState>().Cast<MigrationAccountState?>()
                .FirstOrDefault(value =>
                {
                    return string.Equals(value.ToString(), query.State.Value, StringComparison.Ordinal);
                });
            accounts = state is null ? accounts.Where(_ => false) : accounts.Where(account => account.State == state);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            accounts = accounts.Where(account =>
                EF.Functions.ILike(account.SourceAccountName, pattern, LikePattern.EscapeCharacter)
                || (account.PrimaryDomain != null && EF.Functions.ILike(account.PrimaryDomain, pattern, LikePattern.EscapeCharacter)));
        }

        var page = await accounts.ToPagedAsync(query, ListMigrationJobAccountsSorts.Map, cancellationToken);

        return Result<PagedResult<MigrationJobAccountDto>>.Ok(page.Map(account =>
        {
            return MigrationJobMapper.ToDto(account, _failureNames.Of(account.FailureCode), _names);
        }));
    }
}
