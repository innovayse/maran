using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;

namespace Maran.Modules.Dns.Queries.GetZoneDnssec;

/// <summary>
/// Handles <see cref="GetZoneDnssecQuery"/>: the panel's row beside what the host is seen to be able to do and,
/// for a zone that is signed, what the name server is seen to do about it.
/// </summary>
/// <remarks>
/// <para>
/// Another tenant's zone is not found rather than forbidden: the context's query filter means the row is not
/// there, and so is its key.
/// </para>
/// <para>
/// An agent that cannot be asked is an answer, not a failure: <c>Available</c> reads false and the observation
/// is absent, so a screen can say "I could not look" without losing the DS record the row states. The private
/// key is never read into an answer.
/// </para>
/// </remarks>
public sealed class GetZoneDnssecQueryHandler
{
    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns the daemon.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Asks whether this host's name server software can sign.</summary>
    private readonly DnssecHostProbe _probe;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="agent">The agent client that reads the zone.</param>
    /// <param name="probe">Asks whether this host can sign.</param>
    public GetZoneDnssecQueryHandler(DnsDbContext dbContext, IAgentDnsClient agent, DnssecHostProbe probe)
    {
        _dbContext = dbContext;
        _agent = agent;
        _probe = probe;
    }

    /// <summary>Returns the zone's DNSSEC situation, or <c>ZoneNotFound</c>.</summary>
    /// <param name="query">Which zone to read.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The situation, or <c>ZoneNotFound</c>.</returns>
    public async Task<Result<ZoneDnssecDto>> HandleAsync(GetZoneDnssecQuery query, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.Zones
            .AsNoTracking()
            .Include(row => row.SigningKeys)
            .SingleOrDefaultAsync(row => row.Id == query.ZoneId, cancellationToken);
        if (zone is null)
        {
            return Result<ZoneDnssecDto>.Fail(Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound));
        }

        var available = await _probe.IsAvailableAsync(cancellationToken);
        DnsSigningObservedDto? observed = null;
        if (available && zone.DnssecState != DnssecState.Off)
        {
            var served = await _agent.GetZoneAsync(zone.Name, cancellationToken);
            observed = served.IsSuccess ? served.Value.Signing : null;
        }

        return Result<ZoneDnssecDto>.Ok(ZoneDnssecMapper.From(zone, available, observed));
    }
}
