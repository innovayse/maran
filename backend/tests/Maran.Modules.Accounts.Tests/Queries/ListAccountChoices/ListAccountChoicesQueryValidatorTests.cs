using Maran.Modules.Accounts.Queries.ListAccountChoices;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccountChoices;

/// <summary>Behavioral contract of <see cref="ListAccountChoicesQueryValidator"/>.</summary>
public sealed class ListAccountChoicesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListAccountChoicesQueryValidator _validator = new();

    /// <summary>A name search at the largest offered size is accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void A_name_search_at_the_largest_offered_size_is_accepted()
    {
        Assert.True(_validator.Validate(new ListAccountChoicesQuery(PageSize: 100, Search: SearchTerm.Of("ol"), Sort: SortRequest.Parse("-name"))).IsValid);
    }

    /// <summary>A page size above the cap is refused, so a picker cannot ask for every account at once.</summary>
    [Fact]
    public void A_page_size_above_the_cap_is_refused()
    {
        var result = _validator.Validate(new ListAccountChoicesQuery(PageSize: 1000));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A sort other than the name is refused.</summary>
    [Fact]
    public void A_sort_other_than_the_name_is_refused()
    {
        var result = _validator.Validate(new ListAccountChoicesQuery(Sort: SortRequest.Parse("createdAt")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }
}
