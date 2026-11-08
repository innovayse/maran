using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Queries.GetZone;

/// <summary>Handles <see cref="GetZoneQuery"/> by reading one row within the caller's tenant scope.</summary>
/// <remarks>
/// Another tenant's zone is not found rather than forbidden, and that is not a politeness: 403 confirms the
/// id names a real zone, which turns this endpoint into an oracle for enumerating other customers' names
/// (rules/testing.md item 3). The distinction is not made by this handler at all, because the context's query
/// filter means the row genuinely is not there.
/// </remarks>
public sealed class GetZoneQueryHandler
{
    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names.</param>
    public GetZoneQueryHandler(DnsDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Returns the zone labelled with its owner's name, or <c>ZoneNotFound</c>.</summary>
    /// <param name="query">Which zone to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The zone's view, or <c>ZoneNotFound</c>.</returns>
    public async Task<Result<ZoneDto>> HandleAsync(GetZoneQuery query, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.Zones
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == query.ZoneId, cancellationToken);
        if (zone is null)
        {
            return Result<ZoneDto>.Fail(Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound));
        }

        var recordCount = await _dbContext.ZoneRecords
            .AsNoTracking()
            .CountAsync(record => record.ZoneId == zone.Id, cancellationToken);
        var ownerNames = await _accounts.NamesOfAsync([zone.AccountId], cancellationToken);
        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);

        return Result<ZoneDto>.Ok(ZoneMapper.From(zone, recordCount, ownerNames, settings?.Nameservers ?? Array.Empty<string>()));
    }
}
