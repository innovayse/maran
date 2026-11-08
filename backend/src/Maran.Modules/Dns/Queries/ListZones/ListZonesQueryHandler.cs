using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Dns.Queries.ListZones;

/// <summary>
/// Handles <see cref="ListZonesQuery"/> by reading one page of <c>dns.Zones</c> within the caller's tenant
/// scope.
/// </summary>
/// <remarks>
/// The tenant boundary is not the <c>Where</c> clause on the account: the context's global query filter
/// supplies it, so this handler could not leak another tenant's rows even if it were rewritten carelessly.
/// The filter applies before the count as well as before the page, so a customer's total never counts
/// another tenant's zones. Each row's record count and owner name are read in one batched query each for the
/// whole page, never one per row.
/// </remarks>
public sealed class ListZonesQueryHandler
{
    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names.</param>
    public ListZonesQueryHandler(DnsDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Narrows to the account, searches the name, and answers the requested page.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<ZoneDto>>> HandleAsync(
        ListZonesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Zone> zones = _dbContext.Zones.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            zones = zones.Where(zone => zone.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so the
            // term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            zones = zones.Where(zone => EF.Functions.ILike(zone.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await zones.ToPagedAsync(query, ListZonesSorts.Map, cancellationToken);

        var zoneIds = page.Items
            .Select(row =>
            {
                return row.Id;
            })
            .ToList();
        var counts = await _dbContext.ZoneRecords
            .AsNoTracking()
            .Where(record => zoneIds.Contains(record.ZoneId))
            .GroupBy(record => record.ZoneId)
            .Select(group => new { ZoneId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row =>
            {
                return row.ZoneId;
            }, row =>
            {
                return row.Count;
            }, cancellationToken);

        var ownerIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        IReadOnlyList<string> nameservers = settings?.Nameservers ?? Array.Empty<string>();

        return Result<PagedResult<ZoneDto>>.Ok(page.Map(row =>
        {
            return ZoneMapper.From(row, counts.GetValueOrDefault(row.Id), ownerNames, nameservers);
        }));
    }
}
