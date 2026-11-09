using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Queries.GetDnsSettings;

/// <summary>Handles <see cref="GetDnsSettingsQuery"/> by reading the one settings row.</summary>
/// <remarks>
/// A server that never saved settings answers the defaults an unsaved row would carry, rather than a
/// not-found, because "nothing stated yet" is the ordinary first state of the screen and not a failure.
/// </remarks>
public sealed class GetDnsSettingsQueryHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The clock the unsaved defaults are stamped with; never written.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="clock">The clock.</param>
    public GetDnsSettingsQueryHandler(DnsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Returns the settings, or the defaults when none were saved.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings view; this operation never fails.</returns>
    public async Task<Result<DnsSettingsDto>> HandleAsync(GetDnsSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);

        return Result<DnsSettingsDto>.Ok(DnsSettingsMapper.From(settings ?? new DnsSettings(_clock.UtcNow)));
    }
}
