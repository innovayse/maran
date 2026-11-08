using Maran.Host.Search.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Search;

/// <summary>One provider's part of the answer: its kind, whether it could answer, and its hits.</summary>
/// <param name="Kind">What the hits are.</param>
/// <param name="State">Whether the provider could answer; hits are empty unless it is ready.</param>
/// <param name="Hits">The hits, best first; empty when nothing matched or the group is unavailable.</param>
public sealed record SearchGroupDto(SearchKind Kind, SearchGroupState State, IReadOnlyList<SearchHit> Hits);
