using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlans;

/// <summary>Behavioral contract of <see cref="ListPlansQueryValidator"/>.</summary>
public sealed class ListPlansQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListPlansQueryValidator _validator = new();

    /// <summary>The defaults and every exposed sort are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_exposed_sort_are_accepted()
    {
        Assert.True(_validator.Validate(new ListPlansQuery()).IsValid);
        foreach (var sort in new[] { "name", "-diskQuota", "accounts" })
        {
            Assert.True(_validator.Validate(new ListPlansQuery(
                Page: 2,
                PageSize: 10,
                Search: SearchTerm.Of("agency"),
                Sort: SortRequest.Parse(sort))).IsValid);
        }
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_list_does_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListPlansQuery(Sort: SortRequest.Parse("maxSites")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused with the paging contract's code.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListPlansQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListPlansQuery(PageSize: 1_000));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
