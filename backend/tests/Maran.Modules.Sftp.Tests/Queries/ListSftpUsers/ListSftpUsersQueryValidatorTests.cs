using Maran.Modules.Sftp.Queries.ListSftpUsers;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sftp.Tests.Queries.ListSftpUsers;

/// <summary>Behavioral contract of <see cref="ListSftpUsersQueryValidator"/>.</summary>
public sealed class ListSftpUsersQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListSftpUsersQueryValidator _validator = new();

    /// <summary>
    /// The defaults and every member set within its bounds are accepted — the inverse control for the
    /// refusals below.
    /// </summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListSftpUsersQuery()).IsValid);
        Assert.True(_validator.Validate(new ListSftpUsersQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("deploy"),
            Sort: SortRequest.Parse("-name"),
            AccountId: Guid.NewGuid())).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListSftpUsersQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("fullName")]
    [InlineData("owner")]
    [InlineData("Name")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListSftpUsersQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused with the paging contract's code.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListSftpUsersQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListSftpUsersQuery(PageSize: 37));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
