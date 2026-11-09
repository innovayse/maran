using System.Text.Json.Serialization;
using Maran.Modules.Dns.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.ChangeRecord;

/// <summary>Replaces what one record of a zone says.</summary>
/// <param name="Record">The record as it should now read.</param>
/// <param name="ZoneId">The zone, from the route. Established by the server; never bound from the body.</param>
/// <param name="RecordId">The record, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record ChangeRecordCommand(
    RecordDraft Record,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ZoneId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid RecordId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
