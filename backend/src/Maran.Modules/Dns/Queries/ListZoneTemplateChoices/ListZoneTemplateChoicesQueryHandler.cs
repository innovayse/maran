using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Queries.ListZoneTemplateChoices;

/// <summary>
/// Handles <see cref="ListZoneTemplateChoicesQuery"/>. The answer carries the three fields a creation form
/// needs and nothing of a template's records, which is the whole of what a customer may learn about a template:
/// the projection selects only the id and the name, so the records are never even read.
/// </summary>
public sealed class ListZoneTemplateChoicesQueryHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    public ListZoneTemplateChoicesQueryHandler(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Lists the choices, the default first and the rest by name.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The choices; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<ZoneTemplateChoiceDto>>> HandleAsync(
        ListZoneTemplateChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var defaultId = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.DefaultTemplateId)
            .SingleOrDefaultAsync(cancellationToken);
        var rows = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .OrderBy(template => template.Name)
            .ThenBy(template => template.Id)
            .Select(template => new { template.Id, template.Name })
            .ToListAsync(cancellationToken);

        IReadOnlyList<ZoneTemplateChoiceDto> choices = rows
            .Select(row =>
            {
                return ZoneTemplateMapper.ChoiceFrom(row.Id, row.Name, defaultId);
            })
            .OrderByDescending(choice =>
            {
                return choice.IsDefault;
            })
            .ToList();

        return Result<IReadOnlyList<ZoneTemplateChoiceDto>>.Ok(choices);
    }
}
