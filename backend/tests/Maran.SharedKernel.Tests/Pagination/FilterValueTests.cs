using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>How <see cref="FilterValue"/> reads a caller's optional filter text, and what it refuses to expose.</summary>
public sealed class FilterValueTests
{
    /// <summary>Absent and empty text are both no filter, and so is the default value.</summary>
    /// <param name="text">The text as it arrived.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Absent_and_empty_text_are_no_filter(string? text)
    {
        Assert.True(FilterValue.From(text).IsNone);
        Assert.Equal(FilterValue.None, default);
    }

    /// <summary>Any other text is the value exactly as written, untrimmed, for the validator to judge.</summary>
    /// <param name="text">The text as it arrived.</param>
    [Theory]
    [InlineData("BackupCreate")]
    [InlineData(" ")]
    [InlineData("8.3\n")]
    public void Other_text_is_the_value_verbatim(string text)
    {
        var filter = FilterValue.From(text);

        Assert.False(filter.IsNone);
        Assert.Equal(text, filter.Value);
    }

    /// <summary>No filter has no value to read, and building a value from empty text is refused.</summary>
    [Fact]
    public void No_filter_has_no_value_and_an_empty_value_cannot_be_built()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            return FilterValue.None.Value;
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return FilterValue.Of(string.Empty);
        });
    }
}
