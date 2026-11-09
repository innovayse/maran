namespace Maran.Modules.Dns.Common;

/// <summary>
/// The reference data a record form is drawn from: the types and the bounds, so the interface keeps no
/// DNS constant of its own (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <param name="Types">Every type a record may have.</param>
/// <param name="MinTtl">The shortest time to live accepted, in seconds.</param>
/// <param name="MaxTtl">The longest time to live accepted, in seconds.</param>
/// <param name="DefaultTtl">The time to live a new record starts with, in seconds.</param>
/// <param name="MaxValueLength">The longest record value, in characters.</param>
/// <param name="MaxRecordsPerZone">The most records one zone may hold on this server.</param>
/// <param name="CaaTags">The CAA tags a CAA record may use.</param>
public sealed record RecordTypeCatalogDto(
    IReadOnlyList<RecordTypeDto> Types,
    int MinTtl,
    int MaxTtl,
    int DefaultTtl,
    int MaxValueLength,
    int MaxRecordsPerZone,
    IReadOnlyList<string> CaaTags);
