using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Queries.ListResellers;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListResellers;

/// <summary>Behavioral contract of <see cref="ListResellersQueryValidator"/>.</summary>
public sealed class ListResellersQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListResellersQueryValidator _validator = new();

    /// <summary>The defaults and every known filter and sort are accepted: the inverse control.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_and_sort_are_accepted()
    {
        Assert.True(_validator.Validate(new ListResellersQuery()).IsValid);
        Assert.True(_validator.Validate(new ListResellersQuery(
            Page: 4,
            PageSize: 100,
            Search: SearchTerm.Of("acme"),
            Sort: SortRequest.Parse("-createdAt"),
            Status: ResellerStatus.Suspended)).IsValid);
        Assert.True(_validator.Validate(new ListResellersQuery(Sort: SortRequest.Parse("status"))).IsValid);
    }

    /// <summary>A status that names no member of the enum is refused with the shared state code.</summary>
    [Fact]
    public void A_status_that_names_no_member_is_refused_with_the_shared_state_code()
    {
        var result = _validator.Validate(new ListResellersQuery(Status: (ResellerStatus)7));

        Assert.Equal("AccountStatusUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_list_does_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListResellersQuery(Sort: SortRequest.Parse("contactEmail")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer, and a page below the first, are refused.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_and_a_page_below_the_first_are_refused()
    {
        Assert.Equal(
            "ListPageSizeNotOffered",
            Assert.Single(_validator.Validate(new ListResellersQuery(PageSize: 7)).Errors).ErrorCode);
        Assert.Equal(
            "ListPageOutOfRange",
            Assert.Single(_validator.Validate(new ListResellersQuery(Page: 0)).Errors).ErrorCode);
    }
}
