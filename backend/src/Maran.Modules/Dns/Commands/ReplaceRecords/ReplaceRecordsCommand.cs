using System.Text.Json.Serialization;
using Maran.Modules.Dns.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.ReplaceRecords;

/// <summary>
/// Replaces the customer's whole record set of a zone in one step, which is what an import does. Records
/// another facility manages are kept.
/// </summary>
/// <param name="Records">The complete new set. Every record is weighed against the zone's rules.</param>
/// <param name="ZoneId">The zone, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record ReplaceRecordsCommand(
    IReadOnlyList<RecordDraft> Records,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ZoneId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
