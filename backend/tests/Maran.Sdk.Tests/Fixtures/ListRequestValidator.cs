using Maran.Sdk.Validators;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Sdk.Tests.Fixtures;

/// <summary>A list's validator as a module writes one: the shared bounds over a one-field sort map.</summary>
public sealed class ListRequestValidator : PagedQueryValidator<ListRequest, ListRow>
{
    /// <summary>The list's sort whitelist: <c>name</c> only.</summary>
    public static readonly SortMap<ListRow> Sorts = new SortMapBuilder<ListRow>()
        .By("name", row => row.Name)
        .DefaultTo("name", SortDirection.Ascending)
        .TieBreaker(row => row.Id);

    /// <summary>Declares the shared rules over <see cref="Sorts"/>.</summary>
    public ListRequestValidator()
        : base(Sorts)
    {
    }
}
