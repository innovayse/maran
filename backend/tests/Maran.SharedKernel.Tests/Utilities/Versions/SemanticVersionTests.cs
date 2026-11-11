using Maran.SharedKernel.Utilities.Versions;

namespace Maran.SharedKernel.Tests.Utilities.Versions;

/// <summary>
/// The strict semantic version parser and the ordering the specification defines. Every refusal sits
/// beside the accepted spelling it is closest to.
/// </summary>
public sealed class SemanticVersionTests
{
    /// <summary>Parses text that must be valid, failing the test with the text when it is not.</summary>
    /// <param name="text">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static SemanticVersion Version(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), text);
        return version!;
    }

    /// <summary>A valid version parses to its parts and reads back to the text it was written as.</summary>
    [Theory]
    [InlineData("0.0.0", 0, 0, 0, "", "")]
    [InlineData("1.2.3", 1, 2, 3, "", "")]
    [InlineData("10.20.30", 10, 20, 30, "", "")]
    [InlineData("1.0.0-alpha", 1, 0, 0, "alpha", "")]
    [InlineData("1.0.0-alpha.1", 1, 0, 0, "alpha.1", "")]
    [InlineData("1.0.0-0.3.7", 1, 0, 0, "0.3.7", "")]
    [InlineData("1.0.0-x-y.z", 1, 0, 0, "x-y.z", "")]
    [InlineData("1.0.0+20130313144700", 1, 0, 0, "", "20130313144700")]
    [InlineData("1.0.0-beta+exp.sha.5114f85", 1, 0, 0, "beta", "exp.sha.5114f85")]
    [InlineData("1.0.0+001", 1, 0, 0, "", "001")]
    public void A_valid_version_parses_to_its_parts_and_reads_back(
        string text, int major, int minor, int patch, string preRelease, string build)
    {
        var version = Version(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
        Assert.Equal(build, version.Build);
        Assert.Equal(text, version.ToString());
    }

    /// <summary>Malformed text is refused without throwing and yields no version.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3 ")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.x")]
    [InlineData("1..3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-alpha..1")]
    [InlineData("1.2.3-alpha.")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-alpha_1")]
    [InlineData("1.2.3+build!")]
    [InlineData("1.2.3+build..1")]
    [InlineData("2147483648.0.0")]
    [InlineData("99999999999999999999.0.0")]
    public void Malformed_text_is_refused_without_throwing(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out var version));
        Assert.Null(version);
    }

    /// <summary>The largest number an int holds is accepted; one more is refused.</summary>
    [Fact]
    public void The_largest_number_that_fits_is_accepted_and_the_next_is_refused()
    {
        Assert.True(SemanticVersion.TryParse("2147483647.0.0", out _));
        Assert.False(SemanticVersion.TryParse("2147483648.0.0", out _));
    }

    /// <summary>Text longer than the limit is refused unread, while text at the limit is read.</summary>
    [Fact]
    public void Text_longer_than_the_limit_is_refused_and_text_at_the_limit_is_read()
    {
        var atLimit = "1.0.0-" + new string('a', SemanticVersion.MaxLength - "1.0.0-".Length);
        var overLimit = atLimit + "a";

        Assert.Equal(SemanticVersion.MaxLength, atLimit.Length);
        Assert.True(SemanticVersion.TryParse(atLimit, out _));
        Assert.False(SemanticVersion.TryParse(overLimit, out _));
    }

    /// <summary>The specification's own precedence example sorts exactly as it is written.</summary>
    [Fact]
    public void The_specifications_precedence_example_sorts_in_the_written_order()
    {
        string[] ascending =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11",
            "1.0.0-rc.1", "1.0.0", "2.0.0", "2.1.0", "2.1.1",
        ];

        for (var i = 0; i < ascending.Length - 1; i++)
        {
            Assert.True(Version(ascending[i]).CompareTo(Version(ascending[i + 1])) < 0, ascending[i]);
            Assert.True(Version(ascending[i + 1]).CompareTo(Version(ascending[i])) > 0, ascending[i + 1]);
        }
    }

    /// <summary>Numeric identifiers compare by value, so ten is above nine, even past the size of an int.</summary>
    [Fact]
    public void Numeric_pre_release_identifiers_compare_by_value_whatever_their_size()
    {
        Assert.True(Version("1.0.0-beta.11").CompareTo(Version("1.0.0-beta.2")) > 0);
        Assert.True(Version("1.0.0-9999999999999999999999").CompareTo(Version("1.0.0-9999999999999999999998")) > 0);
        Assert.True(Version("1.0.0-9").CompareTo(Version("1.0.0-10")) < 0);
    }

    /// <summary>Build metadata takes no part in equality or order, while a different pre-release does.</summary>
    [Fact]
    public void Build_metadata_is_ignored_and_a_pre_release_is_not()
    {
        Assert.True(Version("1.2.3+a").Equals(Version("1.2.3+b")));
        Assert.Equal(Version("1.2.3+a").GetHashCode(), Version("1.2.3+b").GetHashCode());
        Assert.Equal(0, Version("1.2.3+a").CompareTo(Version("1.2.3")));
        Assert.False(Version("1.2.3-alpha").Equals(Version("1.2.3")));
    }

    /// <summary>A pre-release is flagged, a release is not.</summary>
    [Fact]
    public void Only_a_version_with_a_pre_release_part_is_a_prerelease()
    {
        Assert.True(Version("1.0.0-rc.1").IsPrerelease);
        Assert.False(Version("1.0.0").IsPrerelease);
        Assert.False(Version("1.0.0+build").IsPrerelease);
    }

    /// <summary>Equality against null and against a different type is false, never a throw.</summary>
    [Fact]
    public void Equality_against_null_or_another_type_is_false()
    {
        Assert.False(Version("1.0.0").Equals((SemanticVersion?)null));
        Assert.False(Version("1.0.0").Equals((object?)"1.0.0"));
        Assert.True(Version("1.0.0").Equals((object?)Version("1.0.0")));
    }

    /// <summary>The six operators agree with the specification's precedence for a lower and a higher version.</summary>
    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    public void The_operators_follow_precedence(string lower, string higher)
    {
        var low = Version(lower);
        var high = Version(higher);

        Assert.True(low < high);
        Assert.True(low <= high);
        Assert.True(high > low);
        Assert.True(high >= low);
        Assert.True(low != high);
        Assert.False(low == high);
        Assert.False(low > high);
        Assert.False(low >= high);
        Assert.False(high < low);
        Assert.False(high <= low);
    }

    /// <summary>Two versions that differ only in build metadata are equal under every operator.</summary>
    [Fact]
    public void Versions_differing_only_in_build_metadata_are_equal_under_the_operators()
    {
        var left = Version("1.2.3+a");
        var right = Version("1.2.3+b");

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left <= right);
        Assert.True(left >= right);
        Assert.False(left < right);
        Assert.False(left > right);
    }

    /// <summary>Null orders below every version and equals only null, so no operator throws.</summary>
    [Fact]
    public void Null_orders_below_every_version_under_the_operators()
    {
        var version = Version("0.0.0");
        SemanticVersion? none = null;

        Assert.True(none == null);
        Assert.True(none != version);
        Assert.False(version == none);
        Assert.True(none < version);
        Assert.True(none <= version);
        Assert.True(none <= null);
        Assert.True(none >= null);
        Assert.False(none > version);
        Assert.False(none >= version);
        Assert.True(version > none);
        Assert.True(version >= none);
        Assert.False(version < none);
        Assert.False(version <= none);
    }
}
