using Maran.Modules.ServerMigration.Queries.ListRemoteServers;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Tests.Queries.ListRemoteServers;

/// <summary>The list is held to the panel's paging bounds and to the sorts it offers.</summary>
public sealed class ListRemoteServersQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ListRemoteServersQueryValidator Validator = new();

    /// <summary>Every sort the list offers is accepted, descending too, and so is the default.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("host")]
    [InlineData("state")]
    [InlineData("lastTestedAt")]
    [InlineData("-createdAt")]
    public void An_offered_sort_is_accepted(string sort)
    {
        var result = Validator.Validate(new ListRemoteServersQuery(Sort: SortRequest.Parse(sort)));

        Assert.True(result.IsValid);
        Assert.True(Validator.Validate(new ListRemoteServersQuery()).IsValid);
    }

    /// <summary>A sort the list does not offer, a page below the first and a size that is not offered are each refused with their own code.</summary>
    [Fact]
    public void A_sort_a_page_and_a_size_the_list_does_not_offer_are_refused()
    {
        var sort = Validator.Validate(new ListRemoteServersQuery(Sort: SortRequest.Parse("privateKey")));
        var page = Validator.Validate(new ListRemoteServersQuery(Page: 0));
        var size = Validator.Validate(new ListRemoteServersQuery(PageSize: 37));

        Assert.Contains(sort.Errors, error =>
        {
            return error.ErrorCode == "ListSortUnknown";
        });
        Assert.Contains(page.Errors, error =>
        {
            return error.ErrorCode == "ListPageOutOfRange";
        });
        Assert.Contains(size.Errors, error =>
        {
            return error.ErrorCode == "ListPageSizeNotOffered";
        });
    }
}
