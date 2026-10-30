using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackupDestinations;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Tests.Queries.ListBackupDestinations;

/// <summary>Behavioral contract of <see cref="ListBackupDestinationsQueryValidator"/>.</summary>
public sealed class ListBackupDestinationsQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListBackupDestinationsQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListBackupDestinationsQuery()).IsValid);
        Assert.True(_validator.Validate(new ListBackupDestinationsQuery(
            Page: 2,
            PageSize: 10,
            Search: SearchTerm.Of("local"),
            Sort: SortRequest.Parse("-createdAt"),
            Kind: BackupDestinationKind.S3)).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("Name")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListBackupDestinationsQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A size the screens do not offer is refused.</summary>
    [Fact]
    public void A_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListBackupDestinationsQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A kind that names no member of the enum is refused with its own code.</summary>
    [Fact]
    public void A_kind_that_names_no_member_is_refused_with_its_own_code()
    {
        var result = _validator.Validate(new ListBackupDestinationsQuery(Kind: (BackupDestinationKind)99));

        Assert.Equal("BackupDestinationKindUnknown", Assert.Single(result.Errors).ErrorCode);
    }
}
