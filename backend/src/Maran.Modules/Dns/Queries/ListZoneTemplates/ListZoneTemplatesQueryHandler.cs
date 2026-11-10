using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Dns.Queries.ListZoneTemplates;

/// <summary>
/// Handles <see cref="ListZoneTemplatesQuery"/> by reading one page of <c>dns.ZoneTemplates</c>. Reached only
/// behind <c>AdminOnly</c>; the rows belong to the server and no tenant filter applies. Each row's record count
/// is read in one batched query for the whole page, never one per row.
/// </summary>
public sealed class ListZoneTemplatesQueryHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    public ListZoneTemplatesQueryHandler(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Searches the name and answers the requested page.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<ZoneTemplateDto>>> HandleAsync(
        ListZoneTemplatesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<ZoneTemplate> templates = _dbContext.ZoneTemplates.AsNoTracking();

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so the
            // term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            templates = templates.Where(template =>
                EF.Functions.ILike(template.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await templates.ToPagedAsync(query, ListZoneTemplatesSorts.Map, cancellationToken);

        var ids = page.Items
            .Select(row =>
            {
                return row.Id;
            })
            .ToList();
        var counts = await _dbContext.ZoneTemplateRecords
            .AsNoTracking()
            .Where(record => ids.Contains(record.ZoneTemplateId))
            .GroupBy(record => record.ZoneTemplateId)
            .Select(group => new { TemplateId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row =>
            {
                return row.TemplateId;
            }, row =>
            {
                return row.Count;
            }, cancellationToken);

        var defaultId = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.DefaultTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        return Result<PagedResult<ZoneTemplateDto>>.Ok(page.Map(row =>
        {
            return ZoneTemplateMapper.From(row, counts.GetValueOrDefault(row.Id), defaultId);
        }));
    }
}
