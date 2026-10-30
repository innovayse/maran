namespace Maran.SharedKernel.Pagination;

/// <summary>
/// The bounds every paged list of the panel shares: the first page, the page sizes a caller may
/// ask for, and the longest search term. Stated once, so the validator that refuses a request and
/// the pager that serves it cannot disagree.
/// </summary>
/// <remarks>
/// A page size is one of <see cref="AllowedSizes"/> and nothing else — not any number up to
/// <see cref="MaximumSize"/>. The screens offer exactly these four, so a size outside them is a
/// hand-made request; refusing it keeps the set of query shapes the database answers small and
/// known. Nothing is clamped: a size outside the set is refused with its own code.
/// </remarks>
public static class PageDefaults
{
    /// <summary>The first page; pages are numbered from one.</summary>
    public const int FirstPage = 1;

    /// <summary>The page size a list answers when the caller names none.</summary>
    public const int Size = 25;

    /// <summary>The largest page size any list answers.</summary>
    public const int MaximumSize = 100;

    /// <summary>The longest search term, in UTF-16 code units, a list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>
    /// Every page size a caller may ask for, ascending; it includes <see cref="Size"/> and
    /// <see cref="MaximumSize"/>.
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedSizes = Array.AsReadOnly<int>([10, Size, 50, MaximumSize]);
}
