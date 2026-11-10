using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.ListExportGrants;

/// <summary>Reads one page of the pull grants (issue #127).</summary>
/// <remarks>
/// <para>
/// A grant is the administrator's bookkeeping, so the read is not tenant-scoped and the surface that reaches it is
/// administrator-only. Search, filter and order run in the database, so the total counts the grants that matched.
/// The row holds the digest of a token and this list never reads it: the shape has no member that could carry it.
/// </para>
/// <para>
/// The state filter restates <see cref="Domain.Entities.ExportGrant.StateAt"/> as a query (revoked before used before
/// expired), because the state of a row cannot be asked of the entity inside a database filter; the test of this
/// handler holds the two to one answer.
/// </para>
/// </remarks>
public sealed class ListExportGrantsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ListExportGrantsQueryHandler(ServerMigrationDbContext dbContext, IClock clock, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _clock = clock;
        _names = names;
    }

    /// <summary>Narrows by search and state, and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<ExportGrantSummaryDto>>> HandleAsync(
        ListExportGrantsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = _clock.UtcNow;
        var rows = _dbContext.ExportGrants
            .AsNoTracking()
            .Join(
                _dbContext.AccountExports.AsNoTracking(),
                grant => grant.ExportId,
                export => export.Id,
                (grant, export) => new ExportGrantRow
                {
                    Id = grant.Id,
                    ExportId = grant.ExportId,
                    AccountName = export.SourceAccountName,
                    AllowedFrom = grant.AllowedFrom,
                    ManifestReads = grant.ManifestReads,
                    CreatedAt = grant.CreatedAt,
                    ExpiresAt = grant.ExpiresAt,
                    ArtifactConsumedAt = grant.ArtifactConsumedAt,
                    RevokedAt = grant.RevokedAt,
                });

        if (!query.State.IsNone)
        {
            rows = StateOf(query.State.Value) is { } state ? Narrow(rows, state, now) : rows.Where(_ => false);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            rows = rows.Where(row => EF.Functions.ILike(row.AccountName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await rows.ToPagedAsync(query, ListExportGrantsSorts.Map, cancellationToken);

        return Result<PagedResult<ExportGrantSummaryDto>>.Ok(page.Map(row =>
        {
            return new ExportGrantSummaryDto(
                row.Id,
                row.ExportId,
                row.AccountName,
                StateOfRow(row, now).ToString(),
                MigrationEnumNames.Of(_names, StateOfRow(row, now)),
                row.AllowedFrom,
                row.ManifestReads,
                row.CreatedAt,
                row.ExpiresAt,
                row.ArtifactConsumedAt,
                row.RevokedAt);
        }));
    }

    /// <summary>Keeps the rows in a state, by the same order the entity decides in.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="state">The state to keep.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The narrowed rows.</returns>
    private static IQueryable<ExportGrantRow> Narrow(IQueryable<ExportGrantRow> rows, ExportGrantState state, DateTimeOffset now)
    {
        return state switch
        {
            ExportGrantState.Revoked => rows.Where(row => row.RevokedAt != null),
            ExportGrantState.Used => rows.Where(row => row.RevokedAt == null && row.ArtifactConsumedAt != null),
            ExportGrantState.Live => rows.Where(row => row.RevokedAt == null && row.ArtifactConsumedAt == null && row.ExpiresAt > now),
            _ => rows.Where(row => row.RevokedAt == null && row.ArtifactConsumedAt == null && row.ExpiresAt <= now),
        };
    }

    /// <summary>The state of a row, in the order the entity decides in.</summary>
    /// <param name="row">The row.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The state.</returns>
    private static ExportGrantState StateOfRow(ExportGrantRow row, DateTimeOffset now)
    {
        if (row.RevokedAt is not null)
        {
            return ExportGrantState.Revoked;
        }

        if (row.ArtifactConsumedAt is not null)
        {
            return ExportGrantState.Used;
        }

        return now < row.ExpiresAt ? ExportGrantState.Live : ExportGrantState.Expired;
    }

    /// <summary>Reads a state filter spelled as a state's name.</summary>
    /// <param name="spelling">The filter value.</param>
    /// <returns>The state, or <c>null</c> when the spelling is no state's name. A number is not a name.</returns>
    private static ExportGrantState? StateOf(string spelling)
    {
        foreach (var state in Enum.GetValues<ExportGrantState>())
        {
            if (string.Equals(state.ToString(), spelling, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }
}
