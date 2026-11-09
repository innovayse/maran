using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A module's answer to "what do you hold that matches this text", for the global search. Implemented
/// and registered by every module that owns a searchable kind; the Host gathers every registered provider
/// and asks each in turn.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope comes from the module's filtered <c>DbContext</c>, never from the provider.</b> An
/// implementation reads its own tables the ordinary way and does not bypass the tenant query filters
/// (<c>IgnoreQueryFilters</c> is banned in production code). It is given no principal and must not decide
/// scope by hand; the one thing it may decide is relevance, such as answering nothing for a role that has
/// no screen for the kind, or for a feature the caller's plan or grants do not include. A mistake in such
/// a decision can only show a person less than the filter allows, never more.
/// </para>
/// <para>
/// <b>An allow-list, field by field.</b> The projection names each column it reads; a hit carries only
/// fields the caller's role already reads on the object's own page.
/// </para>
/// <para>
/// <b>Bounded.</b> At most <see cref="SearchQuery.HitLimit"/> hits, reading at most
/// <see cref="SearchQuery.CandidateLimit"/> rows, with the term escaped by <c>LikePattern</c>. The Host
/// enforces a time budget around every call and abandons one that exceeds it, so a slow provider costs its
/// own group and nothing else; the provider honours the cancellation token it is given. Providers are
/// called one after another, so one may share a scoped context with a window another provider uses.
/// </para>
/// </remarks>
public interface ISearchProvider
{
    /// <summary>The kind of thing this provider finds; the Host groups the answer by it.</summary>
    SearchKind Kind { get; }

    /// <summary>Finds what the module holds that matches the query, for the current caller.</summary>
    /// <param name="query">The validated term.</param>
    /// <param name="ct">Cancels the read; also fired when the provider's time budget ends.</param>
    /// <returns>At most <see cref="SearchQuery.HitLimit"/> hits, best match first; empty when nothing matches.</returns>
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct);
}
