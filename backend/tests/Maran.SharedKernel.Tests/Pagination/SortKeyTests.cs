using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>Behavioural contract of <see cref="SortKey"/>.</summary>
public sealed class SortKeyTests
{
    /// <summary>A bare field name parses as ascending.</summary>
    [Fact]
    public void A_bare_field_name_parses_as_ascending()
    {
        Assert.True(SortKey.TryParse("createdAt", out var key));
        Assert.Equal(new SortKey("createdAt", SortDirection.Ascending), key);
    }

    /// <summary>A leading minus parses as descending and is not part of the field.</summary>
    [Fact]
    public void A_leading_minus_parses_as_descending()
    {
        Assert.True(SortKey.TryParse("-name", out var key));
        Assert.Equal(new SortKey("name", SortDirection.Descending), key);
    }

    /// <summary>Malformed keys are refused rather than guessed at.</summary>
    /// <param name="text">A malformed key.</param>
    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("--name")]
    [InlineData("+name")]
    [InlineData("1name")]
    [InlineData("na me")]
    [InlineData("name.length")]
    [InlineData("name;drop")]
    [InlineData("náme")]
    public void Malformed_keys_are_refused(string text)
    {
        Assert.False(SortKey.TryParse(text, out var key));
        Assert.Null(key);
    }

    /// <summary>A parsed key writes back the text it was parsed from.</summary>
    /// <param name="text">A well-formed key.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-createdAt")]
    public void A_key_round_trips_to_its_wire_form(string text)
    {
        Assert.True(SortKey.TryParse(text, out var key));
        Assert.Equal(text, key.ToString());
    }
}
