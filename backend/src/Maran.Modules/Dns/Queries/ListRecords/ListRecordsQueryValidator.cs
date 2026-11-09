using Maran.Modules.Dns.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Dns.Queries.ListRecords;

/// <summary>Holds <see cref="ListRecordsQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>The type filter is an enum the binder either parses or refuses; nothing beyond paging is left to check.</remarks>
public sealed class ListRecordsQueryValidator : PagedQueryValidator<ListRecordsQuery, ZoneRecord>
{
    /// <summary>Declares the paging rules against <see cref="ListRecordsSorts.Map"/>.</summary>
    public ListRecordsQueryValidator()
        : base(ListRecordsSorts.Map)
    {
    }
}
