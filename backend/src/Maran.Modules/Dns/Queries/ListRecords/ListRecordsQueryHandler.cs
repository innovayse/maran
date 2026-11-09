using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Queries.ListRecords;

/// <summary>Handles <see cref="ListRecordsQuery"/> by reading one page of a zone's records.</summary>
/// <remarks>
/// The zone is found through the tenant filter FIRST. Without that, a record read narrowed by a foreign zone
/// id would answer an empty page, and an empty page tells the caller the zone exists and is empty, or does
/// not exist, with no way to tell which; the typed "not found" is the same answer for both.
/// </remarks>
public sealed class ListRecordsQueryHandler
{
    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The viewer's-language lookup of the module's display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    public ListRecordsQueryHandler(DnsDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Narrows to the zone, the type and the search, and answers the requested page.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or <c>ZoneNotFound</c>.</returns>
    public async Task<Result<PagedResult<ZoneRecordDto>>> HandleAsync(
        ListRecordsQuery query,
        CancellationToken cancellationToken)
    {
        var visible = await _dbContext.Zones
            .AsNoTracking()
            .AnyAsync(zone => zone.Id == query.ZoneId, cancellationToken);
        if (!visible)
        {
            return Result<PagedResult<ZoneRecordDto>>.Fail(
                Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound));
        }

        IQueryable<ZoneRecord> records = _dbContext.ZoneRecords
            .AsNoTracking()
            .Where(record => record.ZoneId == query.ZoneId);

        if (query.Type is { } type)
        {
            records = records.Where(record => record.Type == type);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            records = records.Where(record =>
                EF.Functions.ILike(record.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(record.Value, pattern, LikePattern.EscapeCharacter));
        }

        var page = await records.ToPagedAsync(query, ListRecordsSorts.Map, cancellationToken);

        return Result<PagedResult<ZoneRecordDto>>.Ok(page.Map(row =>
        {
            return ZoneRecordMapper.From(row, _names);
        }));
    }
}
