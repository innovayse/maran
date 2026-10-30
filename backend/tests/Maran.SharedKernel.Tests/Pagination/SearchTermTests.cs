using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>Behavioural contract of <see cref="SearchTerm"/>.</summary>
public sealed class SearchTermTests
{
    /// <summary>Absent, empty and whitespace-only text are all "no search" — one state, three spellings.</summary>
    /// <param name="text">A spelling of "no search".</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("    ")]
    public void Absent_empty_and_whitespace_text_is_no_search(string? text)
    {
        Assert.Equal(SearchTermVerdict.Accepted, SearchTerm.Parse(text, out var term));
        Assert.True(term.IsNone);
        Assert.Equal(SearchTerm.None, term);
    }

    /// <summary>A term is trimmed, and the trimmed text is what it carries.</summary>
    [Fact]
    public void A_term_is_trimmed()
    {
        Assert.Equal(SearchTermVerdict.Accepted, SearchTerm.Parse("  olive  ", out var term));
        Assert.False(term.IsNone);
        Assert.Equal("olive", term.Value);
    }

    /// <summary>Spaces inside a term are part of it.</summary>
    [Fact]
    public void Spaces_inside_a_term_are_kept()
    {
        Assert.Equal("olive grove", SearchTerm.Of(" olive grove ").Value);
    }

    /// <summary>A term at the limit is accepted, the inverse control for the refusal below.</summary>
    [Fact]
    public void A_term_at_the_limit_is_accepted()
    {
        var text = new string('a', PageDefaults.SearchMaxLength);

        Assert.Equal(SearchTermVerdict.Accepted, SearchTerm.Parse(text, out var term));
        Assert.Equal(text, term.Value);
    }

    /// <summary>A term one character over the limit is refused as too long, and the term is left as none.</summary>
    [Fact]
    public void A_term_over_the_limit_is_refused_as_too_long()
    {
        var text = new string('a', PageDefaults.SearchMaxLength + 1);

        Assert.Equal(SearchTermVerdict.TooLong, SearchTerm.Parse(text, out var term));
        Assert.True(term.IsNone);
    }

    /// <summary>The limit is measured on the trimmed term: surrounding spaces do not count against it.</summary>
    [Fact]
    public void The_limit_is_measured_after_trimming()
    {
        var text = "  " + new string('a', PageDefaults.SearchMaxLength) + "  ";

        Assert.Equal(SearchTermVerdict.Accepted, SearchTerm.Parse(text, out _));
    }

    /// <summary>
    /// A control character anywhere is refused — including a trailing newline that trimming would
    /// otherwise have removed.
    /// </summary>
    /// <param name="text">Text carrying a control character.</param>
    [Theory]
    [InlineData("site\n")]
    [InlineData("\nsite")]
    [InlineData("a\rb")]
    [InlineData("\0")]
    [InlineData("tab\there")]
    [InlineData("\t")]
    public void A_control_character_is_refused(string text)
    {
        Assert.Equal(SearchTermVerdict.ControlCharacter, SearchTerm.Parse(text, out var term));
        Assert.True(term.IsNone);
    }

    /// <summary>No search has no text: reading it throws instead of answering a null or an empty string.</summary>
    [Fact]
    public void Reading_the_value_of_no_search_throws()
    {
        Assert.Throws<InvalidOperationException>(() => { return SearchTerm.None.Value; });
    }

    /// <summary>The default value of the type is no search, which is what an absent parameter leaves behind.</summary>
    [Fact]
    public void The_default_value_is_no_search()
    {
        Assert.True(default(SearchTerm).IsNone);
    }

    /// <summary>Building a term from text that is not a valid search throws.</summary>
    [Fact]
    public void Building_a_term_from_invalid_text_throws()
    {
        Assert.Throws<ArgumentException>(() => { return SearchTerm.Of("a\nb"); });
    }
}
