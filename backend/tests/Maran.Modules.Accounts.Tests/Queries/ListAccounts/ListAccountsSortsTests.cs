using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccounts;

/// <summary>Behavioral contract of <see cref="ListAccountsSorts.Map"/>, the accounts list's sort whitelist.</summary>
public sealed class ListAccountsSortsTests
{
    /// <summary>Name, creation time and status are accepted in either direction.</summary>
    /// <param name="sort">The sort requested.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("status")]
    [InlineData("-status")]
    public void Name_creation_time_and_status_are_accepted_in_either_direction(string sort)
    {
        Assert.True(ListAccountsSorts.Map.Accepts(SortRequest.Parse(sort)));
    }

    /// <summary>A well-formed key naming a column the list does not expose is refused, whatever its case.</summary>
    /// <param name="sort">The sort requested.</param>
    [Theory]
    [InlineData("primaryDomain")]
    [InlineData("ownerEmail")]
    [InlineData("Name")]
    public void A_column_the_list_does_not_expose_is_refused(string sort)
    {
        Assert.False(ListAccountsSorts.Map.Accepts(SortRequest.Parse(sort)));
    }

    /// <summary>A malformed key never becomes a sort request at all, so it cannot reach the map.</summary>
    [Fact]
    public void A_malformed_key_never_becomes_a_sort_request()
    {
        Assert.False(SortRequest.TryParse("name;drop", out _));
    }

    /// <summary>No sort named means oldest first.</summary>
    [Fact]
    public void No_sort_named_means_oldest_first()
    {
        var key = ListAccountsSorts.Map.Resolve(SortRequest.Default);

        Assert.Equal(AccountSortFields.CreatedAt, key.Field);
        Assert.Equal(SortDirection.Ascending, key.Direction);
    }
}
