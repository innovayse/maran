using System.Text;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>Behavioural contract of <see cref="ColumnText"/>.</summary>
public sealed class ColumnTextTests
{
    /// <summary>A high surrogate whose pair is one non-BMP character: U+1F600.</summary>
    private const string Emoji = "\U0001F600";

    /// <summary>A value already within the column's width is returned unchanged.</summary>
    [Fact]
    public void A_value_that_already_fits_is_returned_unchanged()
    {
        const string sent = "account-42";

        Assert.Same(sent, ColumnText.Fit(sent, 64));
    }

    /// <summary>A value over the width comes back at exactly the width, not merely under it.</summary>
    [Fact]
    public void A_value_over_the_width_is_cut_to_exactly_the_width()
    {
        var sent = new string('a', 400);

        var fitted = ColumnText.Fit(sent, 256);

        Assert.Equal(256, fitted.Length);
        Assert.Equal(new string('a', 256), fitted);
    }

    /// <summary>The count is code points, so a run of non-BMP characters is bounded by what the column charges.</summary>
    /// <remarks>
    /// 200 emoji are 400 UTF-16 units and 200 characters as PostgreSQL counts them. A cap that
    /// counted units would cut this in half for no reason; one that counted grapheme clusters would
    /// let seven-code-point clusters through. The column counts code points and so does this.
    /// </remarks>
    [Fact]
    public void A_run_of_non_bmp_characters_is_counted_in_code_points_and_not_in_utf16_units()
    {
        var sent = string.Concat(Enumerable.Repeat(Emoji, 200));

        var fitted = ColumnText.Fit(sent, 256);

        Assert.Same(sent, fitted);
        Assert.Equal(200, CodePointsIn(fitted));
    }

    /// <summary>An over-long run of non-BMP characters is cut on a code point, never inside a pair.</summary>
    /// <remarks>
    /// The cap is ODD on purpose. An emoji is two UTF-16 units, so the code point that would carry
    /// the count from 254 to 255 straddles the unit index a naive slice would cut at. The
    /// assertions are the two halves of the defect: the value stays inside the column's count, and
    /// it survives an encoder that refuses lone surrogates — which is the one Npgsql uses.
    /// </remarks>
    [Fact]
    public void An_over_long_run_of_non_bmp_characters_is_cut_on_a_code_point_and_never_inside_a_surrogate_pair()
    {
        var sent = string.Concat(Enumerable.Repeat(Emoji, 400));

        var fitted = ColumnText.Fit(sent, 255);

        Assert.Equal(255, CodePointsIn(fitted));
        Assert.Equal(string.Concat(Enumerable.Repeat(Emoji, 255)), fitted);
        Assert.Equal(510, fitted.Length);
        Assert.Equal(fitted, StrictUtf8RoundTrip(fitted));
    }

    /// <summary>A cut that lands on a surrogate pair keeps the whole character out rather than half of it.</summary>
    /// <remarks>
    /// The plainest statement of the bug this type exists for: one ASCII character followed by
    /// emoji, cut at an even code point count, puts the boundary in the middle of a pair. Before
    /// the fix the last unit of the result was an unpaired high surrogate and the INSERT threw.
    /// </remarks>
    [Fact]
    public void A_cut_landing_on_a_surrogate_pair_keeps_the_whole_character_out_rather_than_half_of_it()
    {
        var sent = "a" + string.Concat(Enumerable.Repeat(Emoji, 10));

        var fitted = ColumnText.Fit(sent, 4);

        Assert.Equal("a" + Emoji + Emoji + Emoji, fitted);
        Assert.Equal(7, fitted.Length);
        Assert.Equal(fitted, StrictUtf8RoundTrip(fitted));
    }

    /// <summary>A width of zero returns the empty string rather than throwing or passing the value through.</summary>
    [Fact]
    public void A_width_of_zero_returns_the_empty_string()
    {
        Assert.Equal(string.Empty, ColumnText.Fit("anything", 0));
    }

    /// <summary>A negative width is refused at the call rather than producing a nonsense slice.</summary>
    [Fact]
    public void A_negative_width_is_refused_at_the_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            ColumnText.Fit("anything", -1);
        });
    }

    /// <summary>A null value is refused at the call rather than dereferenced.</summary>
    [Fact]
    public void A_null_value_is_refused_at_the_call()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            ColumnText.Fit(null!, 64);
        });
    }

    /// <summary>Counts the characters PostgreSQL charges a <c>character varying</c> column for.</summary>
    /// <param name="value">The text to measure.</param>
    /// <returns>The number of code points in <paramref name="value"/>.</returns>
    private static int CodePointsIn(string value)
    {
        var runes = value.EnumerateRunes();
        var points = 0;

        while (runes.MoveNext())
        {
            points++;
        }

        return points;
    }

    /// <summary>Encodes and decodes through an encoder that refuses lone surrogates, as Npgsql's does.</summary>
    /// <param name="value">The text to round-trip.</param>
    /// <returns>The value, when it is encodable at all.</returns>
    /// <exception cref="EncoderFallbackException">The value holds an unpaired surrogate.</exception>
    private static string StrictUtf8RoundTrip(string value)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        return strict.GetString(strict.GetBytes(value));
    }
}
