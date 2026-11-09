using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>Behavioural contract of <see cref="SearchRank"/>.</summary>
public sealed class SearchRankTests
{
    /// <summary>Each degree of match earns its own rank, best first.</summary>
    /// <param name="field">The candidate's field.</param>
    /// <param name="expected">The rank the term earns against it.</param>
    [Theory]
    [InlineData("example.com", SearchRank.Exact)]
    [InlineData("example.com.au", SearchRank.Prefix)]
    [InlineData("my-example.com", SearchRank.Substring)]
    [InlineData("other.org", SearchRank.NoMatch)]
    public void Each_degree_of_match_earns_its_own_rank(string field, int expected)
    {
        Assert.Equal(expected, SearchRank.Of("example.com", field));
    }

    /// <summary>The comparison ignores case in both the term and the field.</summary>
    [Fact]
    public void Case_never_changes_the_rank()
    {
        Assert.Equal(SearchRank.Exact, SearchRank.Of("EXAMPLE.com", "Example.COM"));
        Assert.Equal(SearchRank.Prefix, SearchRank.Of("EXA", "example.com"));
    }

    /// <summary>A candidate ranks by its best field, however the others rank.</summary>
    [Fact]
    public void A_candidate_ranks_by_its_best_field()
    {
        Assert.Equal(SearchRank.Exact, SearchRank.Of("acme", "my-acme-shop", "acme", null));
    }

    /// <summary>Null and empty fields never match, even an empty one against a non-empty term.</summary>
    [Fact]
    public void Null_and_empty_fields_never_match()
    {
        Assert.Equal(SearchRank.NoMatch, SearchRank.Of("acme", null, string.Empty));
    }

    /// <summary>Pattern characters in the term are plain text here, not wildcards.</summary>
    [Fact]
    public void Pattern_characters_are_plain_text()
    {
        Assert.Equal(SearchRank.NoMatch, SearchRank.Of("50%", "500"));
        Assert.Equal(SearchRank.Substring, SearchRank.Of("50%", "save 50% now"));
    }
}
