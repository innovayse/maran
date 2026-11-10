using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;

/// <summary>
/// Ends a withdrawal from DNSSEC signing: the zone becomes unsigned and its key is deleted. Refused until the
/// minimum wait since the withdrawal began has passed, whoever asks, and unless the caller states that the DS
/// record was removed at the registrar.
/// </summary>
/// <param name="DsRecordRemoved">
/// The caller's statement that the DS record is no longer published at the registrar. Must be true: switching
/// signing off while the record is published makes the domain fail validation everywhere.
/// </param>
/// <param name="ZoneId">Which zone, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record FinishZoneSigningWithdrawalCommand(
    bool DsRecordRemoved,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ZoneId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
