using Maran.Modules.Identity.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>
/// The orders the API key list accepts. <see cref="ListApiKeysQueryValidator"/> refuses any other sort
/// and <see cref="ListApiKeysQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListApiKeysSorts
{
    /// <summary>
    /// <see cref="ApiKeySortFields.Name"/>, <see cref="ApiKeySortFields.CreatedAt"/> and
    /// <see cref="ApiKeySortFields.LastUsedAt"/>, newest created first when none is named, with ties
    /// broken by the key id.
    /// </summary>
    public static readonly SortMap<ApiKey> Map = new SortMapBuilder<ApiKey>()
        .By(ApiKeySortFields.Name, key => key.Name)
        .By(ApiKeySortFields.CreatedAt, key => key.CreatedAt)
        .By(ApiKeySortFields.LastUsedAt, key => key.LastUsedAt)
        .DefaultTo(ApiKeySortFields.CreatedAt, SortDirection.Descending)
        .TieBreaker(key => key.Id);
}
