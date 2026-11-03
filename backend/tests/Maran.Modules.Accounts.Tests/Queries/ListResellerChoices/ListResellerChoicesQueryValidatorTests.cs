using Maran.Modules.Accounts.Queries.ListResellerChoices;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListResellerChoices;

/// <summary>Behavioral contract of <see cref="ListResellerChoicesQueryValidator"/>.</summary>
public sealed class ListResellerChoicesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListResellerChoicesQueryValidator _validator = new();

    /// <summary>The defaults and a name sort are accepted: the inverse control for the refusals.</summary>
    [Fact]
    public void The_defaults_and_a_name_sort_are_accepted()
    {
        Assert.True(_validator.Validate(new ListResellerChoicesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListResellerChoicesQuery(
            Page: 2,
            PageSize: 25,
            Search: SearchTerm.Of("ac"),
            Sort: SortRequest.Parse("-name"))).IsValid);
    }

    /// <summary>A sort the choices do not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_choices_do_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListResellerChoicesQuery(Sort: SortRequest.Parse("createdAt")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListResellerChoicesQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
