using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>
/// A zone's DNSSEC situation as a screen shows it. It never carries the private key, in any member, in any
/// state.
/// </summary>
/// <param name="State">Where the zone is: off, signing, or being withdrawn.</param>
/// <param name="Available">
/// Whether this server's name server software was seen to be able to sign. False when it cannot and when the
/// agent could not be asked; a screen offers to switch DNSSEC on only when it is true.
/// </param>
/// <param name="Ds">The DS record to publish at the registrar, or null while the zone is off.</param>
/// <param name="Dnskey">The public DNSKEY record, or null while the zone is off.</param>
/// <param name="Observed">What the name server was seen to do, or null when it was not asked or could not be.</param>
/// <param name="WithdrawalStartedAt">When the current withdrawal began, or null while the zone is not being withdrawn.</param>
/// <param name="WithdrawalEarliestFinishAt">The earliest instant the withdrawal may end, or null while the zone is not being withdrawn.</param>
/// <param name="RolloverAutomatic">Whether the key is replaced automatically. Always false in this release.</param>
public sealed record ZoneDnssecDto(
    DnssecState State,
    bool Available,
    DsRecordDto? Ds,
    DnskeyRecordDto? Dnskey,
    ZoneDnssecObservationDto? Observed,
    DateTimeOffset? WithdrawalStartedAt,
    DateTimeOffset? WithdrawalEarliestFinishAt,
    bool RolloverAutomatic);
