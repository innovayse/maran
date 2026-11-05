using Maran.Modules.Sftp.Queries.ListSftpUserKeys;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sftp.Tests.Queries.ListSftpUserKeys;

/// <summary>Behavioral contract of <see cref="ListSftpUserKeysQueryValidator"/>.</summary>
public sealed class ListSftpUserKeysQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListSftpUserKeysQueryValidator _validator = new();

    /// <summary>The defaults and every member set within its bounds are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListSftpUserKeysQuery()).IsValid);
        Assert.True(_validator.Validate(new ListSftpUserKeysQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("laptop"),
            Sort: SortRequest.Parse("-createdAt"),
            SftpUserId: Guid.NewGuid())).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData(SftpUserKeySortFields.Label)]
    [InlineData("-" + SftpUserKeySortFields.Label)]
    [InlineData(SftpUserKeySortFields.CreatedAt)]
    [InlineData("-" + SftpUserKeySortFields.CreatedAt)]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListSftpUserKeysQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose — the key body and the fingerprint included — is refused.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("fingerprint")]
    [InlineData("publicKey")]
    [InlineData("algorithm")]
    [InlineData("Label")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListSftpUserKeysQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused with the paging contract's code.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListSftpUserKeysQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListSftpUserKeysQuery(PageSize: 37));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
