using Maran.Modules.Monitoring.Queries.ListPendingUpdates;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Tests.Queries.ListPendingUpdates;

/// <summary>Behavioral contract of <see cref="ListPendingUpdatesQueryValidator"/>.</summary>
public sealed class ListPendingUpdatesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListPendingUpdatesQueryValidator _validator = new();

    /// <summary>
    /// The defaults and every member set within its bounds are accepted — the inverse control for the
    /// refusals below.
    /// </summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListPendingUpdatesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListPendingUpdatesQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("ssl"),
            Sort: SortRequest.Parse("-source"))).IsValid);
    }

    /// <summary>Every word of the classification vocabulary and the security sort are accepted.</summary>
    /// <param name="word">The filter value.</param>
    [Theory]
    [InlineData("security")]
    [InlineData("ordinary")]
    [InlineData("unmeasured")]
    public void Every_classification_word_and_the_security_sort_are_accepted(string word)
    {
        var result = _validator.Validate(new ListPendingUpdatesQuery(
            Sort: SortRequest.Parse("security"),
            Classification: FilterValue.Of(word)));

        Assert.True(result.IsValid);
    }

    /// <summary>A classification outside the three words is refused with its own code.</summary>
    /// <param name="word">The filter value.</param>
    [Theory]
    [InlineData("critical")]
    [InlineData("Security")]
    [InlineData("1")]
    public void A_classification_outside_the_three_words_is_refused(string word)
    {
        var result = _validator.Validate(new ListPendingUpdatesQuery(Classification: FilterValue.Of(word)));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("PendingUpdateClassificationUnknown", failure.ErrorCode);
        Assert.Equal("Classification", failure.PropertyName);
    }

    /// <summary>
    /// A sort the list does not expose is refused with the paging contract's code — the versions above
    /// all, because the panel cannot order them the way the package manager does.
    /// </summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("availableVersion")]
    [InlineData("-installedVersion")]
    [InlineData("PackageName")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListPendingUpdatesQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A size the screens do not offer is refused.</summary>
    [Fact]
    public void A_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListPendingUpdatesQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListPendingUpdatesQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }
}
