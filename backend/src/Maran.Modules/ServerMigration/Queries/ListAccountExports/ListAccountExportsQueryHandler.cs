using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Queries.ListAccountExports;

/// <summary>Reads one page of the account exports (issue #127).</summary>
/// <remarks>
/// An export names an account by id and is the administrator's bookkeeping, so the read is not tenant-scoped and the
/// surface that reaches it is administrator-only. Search and order run in the database, so the total counts the
/// exports that matched and not the table. An export past its expiry is listed until the hourly pass removes it, and
/// its expiry says so; the list holds no key material and no token.
/// </remarks>
public sealed class ListAccountExportsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This server's signing key, whose fingerprint every export row states.</summary>
    private readonly MigrationSigningKeys _keys;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="keys">This server's signing key.</param>
    public ListAccountExportsQueryHandler(ServerMigrationDbContext dbContext, MigrationSigningKeys keys)
    {
        _dbContext = dbContext;
        _keys = keys;
    }

    /// <summary>Narrows by search and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<AccountExportDto>>> HandleAsync(
        ListAccountExportsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var exports = _dbContext.AccountExports.AsNoTracking();

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards.
            var pattern = LikePattern.Contains(query.Search.Value);
            exports = exports.Where(export => EF.Functions.ILike(export.SourceAccountName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await exports.ToPagedAsync(query, ListAccountExportsSorts.Map, cancellationToken);
        var key = await _keys.GetOrCreateAsync(cancellationToken);

        return Result<PagedResult<AccountExportDto>>.Ok(page.Map(export =>
        {
            return AccountImportMapper.ToDto(export, key.Fingerprint);
        }));
    }
}
