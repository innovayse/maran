using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.GetAccountImport;

/// <summary>Handles <see cref="GetAccountImportQuery"/>.</summary>
public sealed class GetAccountImportQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Names a stored failure in the caller's language.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="failureNames">Names a stored failure in the caller's language.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public GetAccountImportQueryHandler(ServerMigrationDbContext dbContext, AccountImportFailureNames failureNames, IStringLocalizer<DisplayNames> names)
    {
        _failureNames = failureNames;
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Reads the import.</summary>
    /// <param name="query">The import.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The import, or not found.</returns>
    public async Task<Result<AccountImportDto>> HandleAsync(GetAccountImportQuery query, CancellationToken cancellationToken)
    {
        var import = await _dbContext.AccountImports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == query.ImportId, cancellationToken);

        return import is null
            ? Result<AccountImportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound))
            : Result<AccountImportDto>.Ok(AccountImportMapper.ToDto(import, _failureNames.Of(import.FailureCode), _names));
    }
}
