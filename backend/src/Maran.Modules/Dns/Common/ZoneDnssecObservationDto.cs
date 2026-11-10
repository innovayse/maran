using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>What the name server was seen to do about signing a zone, read from the daemon and not from the panel's row.</summary>
/// <param name="Mode">The step the daemon's configuration for the zone states; meaningful only when <paramref name="ModeObserved"/> is in force.</param>
/// <param name="ModeObserved">Whether <paramref name="Mode"/> was read from the daemon or could not be.</param>
/// <param name="PublishedKeyTag">The key tag of the key the daemon reports having signed the zone with; zero unless <paramref name="PublishedKeyObserved"/> is in force.</param>
/// <param name="PublishedKeyObserved">Whether the daemon reports a completed signing, reports none, or could not be asked.</param>
public sealed record ZoneDnssecObservationDto(
    DnssecState Mode,
    DnsPolicyState ModeObserved,
    int PublishedKeyTag,
    DnsPolicyState PublishedKeyObserved);
