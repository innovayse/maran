using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>Behavioural contract of <see cref="SortRequest"/>.</summary>
public sealed class SortRequestTests
{
    /// <summary>Absent and empty text are the default order.</summary>
    /// <param name="text">A spelling of "the default order".</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Absent_and_empty_text_is_the_default_order(string? text)
    {
        Assert.True(SortRequest.TryParse(text, out var request));
        Assert.True(request.IsDefault);
        Assert.Equal(SortRequest.Default, request);
    }

    /// <summary>A well-formed key parses into a request carrying it.</summary>
    [Fact]
    public void A_well_formed_key_parses_into_a_request_carrying_it()
    {
        Assert.True(SortRequest.TryParse("-createdAt", out var request));
        Assert.False(request.IsDefault);
        Assert.Equal(new SortKey("createdAt", SortDirection.Descending), request.Key);
    }

    /// <summary>Malformed text is refused, whitespace included — a sort is a name, not free text.</summary>
    /// <param name="text">Malformed sort text.</param>
    [Theory]
    [InlineData(" ")]
    [InlineData("-")]
    [InlineData("name desc")]
    [InlineData(" name")]
    [InlineData("name;drop")]
    public void Malformed_text_is_refused(string text)
    {
        Assert.False(SortRequest.TryParse(text, out var request));
        Assert.True(request.IsDefault);
    }

    /// <summary>The default order names no key: reading it throws.</summary>
    [Fact]
    public void Reading_the_key_of_the_default_order_throws()
    {
        Assert.Throws<InvalidOperationException>(() => { return SortRequest.Default.Key; });
    }

    /// <summary>The default value of the type is the default order, which is what an absent parameter leaves behind.</summary>
    [Fact]
    public void The_default_value_is_the_default_order()
    {
        Assert.True(default(SortRequest).IsDefault);
    }

    /// <summary>A request writes back its wire form; the default order writes the empty string.</summary>
    [Fact]
    public void A_request_round_trips_to_its_wire_form()
    {
        Assert.Equal("-name", SortRequest.Parse("-name").ToString());
        Assert.Equal(string.Empty, SortRequest.Default.ToString());
    }
}
