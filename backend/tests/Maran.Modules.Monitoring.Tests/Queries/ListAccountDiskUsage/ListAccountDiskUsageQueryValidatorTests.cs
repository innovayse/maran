using Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Tests.Queries.ListAccountDiskUsage;

/// <summary>Behavioral contract of <see cref="ListAccountDiskUsageQueryValidator"/>.</summary>
public sealed class ListAccountDiskUsageQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListAccountDiskUsageQueryValidator _validator = new();

    /// <summary>
    /// The defaults and every member set within its bounds are accepted — the inverse control for the
    /// refusals below.
    /// </summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListAccountDiskUsageQuery()).IsValid);
        Assert.True(_validator.Validate(new ListAccountDiskUsageQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("alice"),
            Sort: SortRequest.Parse("-quotaBytes"))).IsValid);
    }

    /// <summary>
    /// A sort the list does not expose is refused with the paging contract's code — used bytes above
    /// all, because the agent measures only the page and cannot know an order by usage.
    /// </summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("usedBytes")]
    [InlineData("-usedBytes")]
    [InlineData("Username")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListAccountDiskUsageQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A size the screens do not offer is refused.</summary>
    [Fact]
    public void A_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListAccountDiskUsageQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListAccountDiskUsageQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }
}
