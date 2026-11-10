using Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Tests.Queries.ListDiscoveredAccounts;

/// <summary>The list is held to the panel's paging bounds and to the sorts it offers.</summary>
public sealed class ListDiscoveredAccountsQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ListDiscoveredAccountsQueryValidator Validator = new();

    /// <summary>Every sort the list offers is accepted; one it does not offer is refused with its code.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-disk")]
    [InlineData("sites")]
    [InlineData("-databases")]
    public void An_offered_sort_is_accepted(string sort)
    {
        var accepted = Validator.Validate(new ListDiscoveredAccountsQuery(Sort: SortRequest.Parse(sort)));
        var refused = Validator.Validate(new ListDiscoveredAccountsQuery(Sort: SortRequest.Parse("mailboxes")));

        Assert.True(accepted.IsValid);
        Assert.Contains(refused.Errors, error =>
        {
            return error.ErrorCode == "ListSortUnknown";
        });
    }

    /// <summary>A size that is not offered is refused; the default size is accepted.</summary>
    [Fact]
    public void A_size_that_is_not_offered_is_refused()
    {
        var refused = Validator.Validate(new ListDiscoveredAccountsQuery(PageSize: 7));

        Assert.Contains(refused.Errors, error =>
        {
            return error.ErrorCode == "ListPageSizeNotOffered";
        });
        Assert.True(Validator.Validate(new ListDiscoveredAccountsQuery()).IsValid);
    }
}
