using Maran.SharedKernel.Utilities.Versions;

namespace Maran.SharedKernel.Tests.Utilities.Versions;

/// <summary>
/// The range grammar and the membership question, including the rule that a pre-release satisfies a
/// range only when a comparator names a pre-release of the same core. Every refusal sits beside an
/// accepted range.
/// </summary>
public sealed class VersionRangeTests
{
    /// <summary>Parses a range that must be valid, failing the test with the text when it is not.</summary>
    /// <param name="text">The range text.</param>
    /// <returns>The parsed range.</returns>
    private static VersionRange Range(string text)
    {
        Assert.True(VersionRange.TryParse(text, out var range), text);
        return range!;
    }

    /// <summary>Whether a range admits a version, both given as text.</summary>
    /// <param name="range">The range text.</param>
    /// <param name="version">The version text.</param>
    /// <returns>True when the range contains the version.</returns>
    private static bool Admits(string range, string version)
    {
        Assert.True(SemanticVersion.TryParse(version, out var parsed), version);
        return Range(range).Contains(parsed!);
    }

    /// <summary>A valid range reads back in canonical text, a bare version as an exact comparator.</summary>
    [Theory]
    [InlineData(">=1.2.0 <2.0.0", ">=1.2.0 <2.0.0")]
    [InlineData(">=0.0.0", ">=0.0.0")]
    [InlineData(">1.0.0", ">1.0.0")]
    [InlineData("<=3.1.4", "<=3.1.4")]
    [InlineData("=1.0.0", "=1.0.0")]
    [InlineData("1.0.0", "=1.0.0")]
    [InlineData(">=1.0.0-beta.1 <1.0.0", ">=1.0.0-beta.1 <1.0.0")]
    public void A_valid_range_parses_to_its_canonical_text(string text, string canonical)
    {
        Assert.Equal(canonical, Range(text).ToString());
    }

    /// <summary>Malformed text is refused without throwing and yields no range.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" >=1.0.0")]
    [InlineData(">=1.0.0 ")]
    [InlineData(">=1.0.0  <2.0.0")]
    [InlineData(">= 1.0.0")]
    [InlineData(">=1.0")]
    [InlineData("^1.2.0")]
    [InlineData("~1.2.0")]
    [InlineData("*")]
    [InlineData("1.x")]
    [InlineData(">=1.0.0 || <0.5.0")]
    [InlineData("=>1.0.0")]
    [InlineData(">>1.0.0")]
    [InlineData("<>1.0.0")]
    [InlineData("v1.0.0")]
    public void Malformed_text_is_refused_without_throwing(string? text)
    {
        Assert.False(VersionRange.TryParse(text, out var range));
        Assert.Null(range);
    }

    /// <summary>Eight comparators are accepted, nine are refused; text over the length limit is refused unread.</summary>
    [Fact]
    public void The_comparator_count_and_the_text_length_are_bounded()
    {
        var atLimit = string.Join(' ', Enumerable.Repeat(">=1.0.0", VersionRange.MaxComparators));
        var overLimit = string.Join(' ', Enumerable.Repeat(">=1.0.0", VersionRange.MaxComparators + 1));

        Assert.True(VersionRange.TryParse(atLimit, out _));
        Assert.False(VersionRange.TryParse(overLimit, out _));
        Assert.False(VersionRange.TryParse(new string('1', VersionRange.MaxLength + 1), out _));
    }

    /// <summary>A version is inside a bounded range exactly between its bounds.</summary>
    [Theory]
    [InlineData(">=1.2.0 <2.0.0", "1.2.0", true)]
    [InlineData(">=1.2.0 <2.0.0", "1.9.9", true)]
    [InlineData(">=1.2.0 <2.0.0", "1.1.9", false)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData(">1.0.0", "1.0.0", false)]
    [InlineData(">1.0.0", "1.0.1", true)]
    [InlineData("<=1.0.0", "1.0.0", true)]
    [InlineData("<=1.0.0", "1.0.1", false)]
    [InlineData("=1.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0+build", true)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData(">=0.0.0", "0.0.0", true)]
    [InlineData(">=0.0.0", "99.0.0", true)]
    public void A_version_is_inside_the_range_exactly_between_its_bounds(string range, string version, bool expected)
    {
        Assert.Equal(expected, Admits(range, version));
    }

    /// <summary>A pre-release is admitted only by a comparator that names a pre-release of the same core.</summary>
    [Theory]
    [InlineData(">=1.2.0", "1.3.0-beta.1", false)]
    [InlineData(">=1.2.0", "1.2.0-beta.1", false)]
    [InlineData(">=1.2.0-beta.1", "1.2.0-beta.2", true)]
    [InlineData(">=1.2.0-beta.1", "1.3.0-beta.2", false)]
    [InlineData(">=1.2.0-beta.1 <2.0.0", "1.2.0-rc.1", true)]
    [InlineData(">=1.2.0-beta.1", "1.2.0", true)]
    [InlineData("=1.0.0-rc.1", "1.0.0-rc.1", true)]
    [InlineData("=1.0.0-rc.1", "1.0.0-rc.2", false)]
    public void A_pre_release_is_admitted_only_by_a_comparator_naming_one_of_the_same_core(
        string range, string version, bool expected)
    {
        Assert.Equal(expected, Admits(range, version));
    }
}
