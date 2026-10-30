using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>Behavioural contract of <see cref="LikePattern"/>.</summary>
public sealed class LikePatternTests
{
    /// <summary>Plain text is wrapped in wildcards and otherwise untouched.</summary>
    [Fact]
    public void Plain_text_is_wrapped_in_wildcards()
    {
        Assert.Equal("%example.com%", LikePattern.Contains("example.com"));
    }

    /// <summary>Each pattern character in the term is escaped so it matches only itself.</summary>
    /// <param name="term">The caller's term.</param>
    /// <param name="expected">The pattern that matches it literally.</param>
    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("my_site", "%my\\_site%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("%", "%\\%%")]
    [InlineData("%_\\'; drop", "%\\%\\_\\\\'; drop%")]
    public void Pattern_characters_are_escaped(string term, string expected)
    {
        Assert.Equal(expected, LikePattern.Contains(term));
    }

    /// <summary>A backslash is escaped once, before the escapes added for the other characters.</summary>
    [Fact]
    public void A_backslash_before_a_wildcard_is_escaped_once_each()
    {
        Assert.Equal("%\\\\\\%%", LikePattern.Contains("\\%"));
    }
}
