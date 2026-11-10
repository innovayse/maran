using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.ListAccountImports;

/// <summary>Reads one page of the account imports (issue #127).</summary>
/// <remarks>
/// An import names the account it created by id and is the administrator's bookkeeping, so the read is not
/// tenant-scoped and the surface that reaches it is administrator-only. Search, filter and order all run in the
/// database. The list shape carries no package section and no issued secret, only the figures and the failure.
/// </remarks>
public sealed class ListAccountImportsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Names a stored failure in the caller's language.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="failureNames">Names a stored failure in the caller's language.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ListAccountImportsQueryHandler(ServerMigrationDbContext dbContext, AccountImportFailureNames failureNames, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
        _names = names;
    }

    /// <summary>Narrows by search and state, and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<AccountImportDto>>> HandleAsync(
        ListAccountImportsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<AccountImport> imports = _dbContext.AccountImports.AsNoTracking();

        if (!query.State.IsNone)
        {
            imports = StateOf(query.State.Value) is { } state
                ? imports.Where(import => import.State == state)
                : imports.Where(_ => false);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            imports = imports.Where(import =>
                EF.Functions.ILike(import.SourceAccountName, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(import.SourceHostName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await imports.ToPagedAsync(query, ListAccountImportsSorts.Map, cancellationToken);

        return Result<PagedResult<AccountImportDto>>.Ok(page.Map(import =>
        {
            return AccountImportMapper.ToDto(import, _failureNames.Of(import.FailureCode), _names);
        }));
    }

    /// <summary>Reads a state filter spelled as a state's name.</summary>
    /// <param name="spelling">The filter value.</param>
    /// <returns>The state, or <c>null</c> when the spelling is no state's name. A number is not a name.</returns>
    private static AccountImportState? StateOf(string spelling)
    {
        foreach (var state in Enum.GetValues<AccountImportState>())
        {
            if (string.Equals(state.ToString(), spelling, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }
}
