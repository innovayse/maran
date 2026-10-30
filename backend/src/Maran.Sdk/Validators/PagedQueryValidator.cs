using FluentValidation;
using Maran.Sdk.Resources;
using Maran.SharedKernel.Pagination;

namespace Maran.Sdk.Validators;

/// <summary>
/// The paging bounds every list query of the panel is held to. A list's own
/// <c>…QueryValidator</c> derives from this, passes the list's <see cref="SortMap{T}"/>, and adds
/// the rules for its typed filters in its own constructor.
/// </summary>
/// <typeparam name="TQuery">The list query being validated.</typeparam>
/// <typeparam name="TRow">The row type the list's sort map orders.</typeparam>
/// <remarks>
/// <para>
/// Each broken bound answers its own code from <c>Resources/ValidationMessages</c>, carried both as
/// the failure's error code and as its message — the message is what the panel's exception
/// middleware reads as the code of a 400 — so a caller can tell "page 0" from "size 37" from
/// "unknown sort" without parsing a sentence. Two of the list contract's codes are not raised here:
/// a search that is too long or carries a control character, and a sort that is not a well-formed
/// key, never become a query at all — <see cref="SearchTerm"/> and <see cref="SortRequest"/> cannot
/// hold them, and <c>ModelBinding/PagingModelBinderProvider</c> refuses them while binding, with
/// <c>ListSearchTooLong</c>, <c>ListSearchInvalid</c> and <c>ListSortUnknown</c>. What stays here
/// is what only the list knows:
/// </para>
/// <list type="bullet">
/// <item><c>ListPageOutOfRange</c> — <c>Page</c> is below <see cref="PageDefaults.FirstPage"/>.</item>
/// <item><c>ListPageSizeNotOffered</c> — <c>PageSize</c> is not one of <see cref="PageDefaults.AllowedSizes"/>.
/// The rule is membership in that set, not a range up to <see cref="PageDefaults.MaximumSize"/>: the
/// screens offer exactly those sizes, and nothing is clamped.</item>
/// <item><c>ListSortUnknown</c> — <c>Sort</c> is a well-formed key naming no field of the sort map.</item>
/// </list>
/// </remarks>
public abstract class PagedQueryValidator<TQuery, TRow> : AbstractValidator<TQuery>
    where TQuery : IPagedQuery
{
    /// <summary>Declares the paging rules against the list's sort whitelist.</summary>
    /// <param name="sortMap">The list's sort map; its keys are the only sorts accepted.</param>
    protected PagedQueryValidator(SortMap<TRow> sortMap)
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(PageDefaults.FirstPage)
            .WithErrorCode(nameof(ValidationMessages.ListPageOutOfRange))
            .WithMessage(nameof(ValidationMessages.ListPageOutOfRange));

        RuleFor(query => query.PageSize)
            .Must(size => { return PageDefaults.AllowedSizes.Contains(size); })
            .WithErrorCode(nameof(ValidationMessages.ListPageSizeNotOffered))
            .WithMessage(nameof(ValidationMessages.ListPageSizeNotOffered));

        RuleFor(query => query.Sort)
            .Must(sortMap.Accepts)
            .WithErrorCode(nameof(ValidationMessages.ListSortUnknown))
            .WithMessage(nameof(ValidationMessages.ListSortUnknown));
    }
}
