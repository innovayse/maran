using Maran.Modules.Firewall.Queries.ListBans;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Tests.Queries.ListBans;

/// <summary>Behavioral contract of <see cref="ListBansQueryValidator"/>.</summary>
public sealed class ListBansQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListBansQueryValidator _validator = new();

    /// <summary>The defaults and every member set are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_member_set_are_accepted()
    {
        Assert.True(_validator.Validate(new ListBansQuery()).IsValid);
        Assert.True(_validator.Validate(new ListBansQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("203.0"),
            Sort: SortRequest.Parse("-expiresAt"))).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The wire sort.</param>
    [Theory]
    [InlineData("address")]
    [InlineData("-address")]
    [InlineData("bannedAt")]
    [InlineData("-bannedAt")]
    [InlineData("expiresAt")]
    [InlineData("-expiresAt")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListBansQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The wire sort.</param>
    [Theory]
    [InlineData("ipAddress")]
    [InlineData("reason")]
    [InlineData("Address")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListBansQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused with the paging contract's code.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListBansQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListBansQuery(PageSize: 101));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
