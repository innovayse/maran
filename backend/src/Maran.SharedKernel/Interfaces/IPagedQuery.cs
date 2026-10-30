using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// The paging contract every list query of the panel implements: which page, how many rows, what
/// to search for and how to order. A list query is a record that implements this interface with
/// positional members defaulted to <c>Page = 1</c>, <c>PageSize = PageDefaults.Size</c>,
/// <c>Search = default</c> (<see cref="SearchTerm.None"/>) and <c>Sort = default</c>
/// (<see cref="SortRequest.Default"/>), and adds its own typed filters beside them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One field, one meaning.</b> <see cref="Search"/> and <see cref="Sort"/> are typed values with
/// two explicit states each — no search or a valid term, the default order or a well-formed key —
/// and the HTTP model binder produces them, so absent, empty and (for a search) whitespace-only
/// parameters all mean the same thing and a malformed one is refused before any handler runs. A
/// caller may send an empty parameter or omit it; the answer is the same. <see cref="Page"/> is
/// the page the caller asked for and is never clamped: a page past the end answers an empty page
/// with the true total, and the caller decides where to go.
/// </para>
/// <para>
/// Paging itself lives in exactly one place for each source:
/// <c>QueryablePagingExtensions.ToPagedAsync</c> for a database query and
/// <c>EnumerablePagingExtensions.ToPaged</c> for a list the agent returned whole.
/// </para>
/// </remarks>
public interface IPagedQuery
{
    /// <summary>The 1-based page requested.</summary>
    int Page { get; }

    /// <summary>Rows per page; one of <c>PageDefaults.AllowedSizes</c>.</summary>
    int PageSize { get; }

    /// <summary>The term to match, or <see cref="SearchTerm.None"/> for no search.</summary>
    SearchTerm Search { get; }

    /// <summary>
    /// A sort key such as <c>name</c> or <c>-createdAt</c>, or <see cref="SortRequest.Default"/> for the
    /// list's default order. Whether the key names a field of the list is the list's validator's question.
    /// </summary>
    SortRequest Sort { get; }
}
