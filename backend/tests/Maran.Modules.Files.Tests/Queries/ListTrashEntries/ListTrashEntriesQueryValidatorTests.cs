using Maran.Modules.Files.Queries.ListTrashEntries;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Files.Tests.Queries.ListTrashEntries;

/// <summary>
/// Holds <see cref="ListTrashEntriesQueryValidator"/> to naming an account and to the list contract: a page that
/// exists, a size the panel offers, and a sort key that names a field of the trash sort map.
/// </summary>
public sealed class ListTrashEntriesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ListTrashEntriesQueryValidator Validator = new();

    /// <summary>A query every rule accepts.</summary>
    private static readonly ListTrashEntriesQuery Valid = new(Guid.NewGuid());

    /// <summary>The default query is accepted.</summary>
    [Fact]
    public void The_default_query_is_valid()
    {
        Assert.True(Validator.Validate(Valid).IsValid);
    }

    /// <summary>A query with no account is refused.</summary>
    [Fact]
    public void A_query_with_no_account_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { AccountId = Guid.Empty }).IsValid);
    }

    /// <summary>A page before the first, or a size the panel does not offer, is refused.</summary>
    [Fact]
    public void A_page_before_the_first_or_an_unoffered_size_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { Page = 0 }).IsValid);
        Assert.False(Validator.Validate(Valid with { PageSize = 7 }).IsValid);
    }

    /// <summary>Every field of the trash sort map is accepted, in both directions, and an unknown one is refused.</summary>
    /// <param name="key">A sort key.</param>
    /// <param name="accepted">Whether the validator should accept it.</param>
    [Theory]
    [InlineData("name", true)]
    [InlineData("-size", true)]
    [InlineData("deletedAt", true)]
    [InlineData("-expiresAt", true)]
    [InlineData("modified", false)]
    [InlineData("-kind", false)]
    public void A_sort_key_is_accepted_only_when_it_names_a_trash_field(string key, bool accepted)
    {
        var query = Valid with { Sort = SortRequest.Parse(key) };

        Assert.Equal(accepted, Validator.Validate(query).IsValid);
    }
}
