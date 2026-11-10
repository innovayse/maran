using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Queries.GetZoneTemplate;

/// <summary>Handles <see cref="GetZoneTemplateQuery"/>: one template with its records, or not found.</summary>
public sealed class GetZoneTemplateQueryHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    public GetZoneTemplateQueryHandler(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the template.</summary>
    /// <param name="query">Which template.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The template with its records, or <c>ZoneTemplateNotFound</c>.</returns>
    public async Task<Result<ZoneTemplateDetailDto>> HandleAsync(
        GetZoneTemplateQuery query,
        CancellationToken cancellationToken)
    {
        var template = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .Include(row => row.Records)
            .SingleOrDefaultAsync(row => row.Id == query.TemplateId, cancellationToken);
        if (template is null)
        {
            return Result<ZoneTemplateDetailDto>.Fail(
                Error.Of(nameof(ErrorMessages.ZoneTemplateNotFound), ErrorType.NotFound));
        }

        var defaultId = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.DefaultTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        return Result<ZoneTemplateDetailDto>.Ok(ZoneTemplateMapper.DetailFrom(template, defaultId));
    }
}
