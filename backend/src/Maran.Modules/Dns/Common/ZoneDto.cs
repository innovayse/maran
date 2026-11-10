using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>One DNS zone as a screen shows it.</summary>
/// <param name="Id">The zone's identity, and the only identifier a request may name for it.</param>
/// <param name="AccountId">The owning account.</param>
/// <param name="AccountName">The owning account's name, or an empty string when the directory could not name it.</param>
/// <param name="Name">The zone's apex name.</param>
/// <param name="Serial">The SOA serial the zone is published under.</param>
/// <param name="RecordCount">How many records the zone holds, not counting the system-managed apex records.</param>
/// <param name="CreatedAt">When the zone was created.</param>
/// <param name="UpdatedAt">When the zone's content last changed.</param>
/// <param name="Nameservers">
/// The name servers the zone names, which the owner delegates the domain to at the registrar. Empty while the
/// administrator has stated none.
/// </param>
/// <param name="SkippedTemplateRecords">
/// The records of the zone's template that were left out because a setting they need is not stated; empty
/// for every zone read after its creation, and for a zone created without a template.
/// </param>
/// <param name="DnssecState">
/// Where the zone is in its DNSSEC life, so a list or a confirmation can warn about a zone whose registrar
/// may hold a DS record for it.
/// </param>
public sealed record ZoneDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string Name,
    long Serial,
    int RecordCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Nameservers,
    IReadOnlyList<SkippedTemplateRecordDto> SkippedTemplateRecords,
    DnssecState DnssecState);
