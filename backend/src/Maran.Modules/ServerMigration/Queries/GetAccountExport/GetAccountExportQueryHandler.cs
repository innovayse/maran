using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Queries.GetAccountExport;

/// <summary>Handles <see cref="GetAccountExportQuery"/>.</summary>
public sealed class GetAccountExportQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This server's signing key.</summary>
    private readonly MigrationSigningKeys _keys;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="keys">This server's signing key.</param>
    public GetAccountExportQueryHandler(ServerMigrationDbContext dbContext, MigrationSigningKeys keys)
    {
        _dbContext = dbContext;
        _keys = keys;
    }

    /// <summary>Reads the export.</summary>
    /// <param name="query">The export.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or not found.</returns>
    public async Task<Result<AccountExportDto>> HandleAsync(GetAccountExportQuery query, CancellationToken cancellationToken)
    {
        var export = await _dbContext.AccountExports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == query.ExportId, cancellationToken);

        if (export is null)
        {
            return Result<AccountExportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationExportNotFound), ErrorType.NotFound));
        }

        var key = await _keys.GetOrCreateAsync(cancellationToken);

        return Result<AccountExportDto>.Ok(AccountImportMapper.ToDto(export, key.Fingerprint));
    }
}
