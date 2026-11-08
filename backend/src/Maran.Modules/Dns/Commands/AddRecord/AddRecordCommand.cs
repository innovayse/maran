using System.Text.Json.Serialization;
using Maran.Modules.Dns.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.AddRecord;

/// <summary>Adds one record to a zone.</summary>
/// <param name="Record">The record as stated: owner name, type, time to live and the type's fields.</param>
/// <param name="ZoneId">
/// The zone, from the route. Established by the server and stamped by the action; never bound from the
/// body, which would let a request name one zone in the URL and another in the document.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the
/// action.
/// </param>
public sealed record AddRecordCommand(
    RecordDraft Record,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ZoneId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
