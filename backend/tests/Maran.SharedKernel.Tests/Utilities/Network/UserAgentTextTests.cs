using System.Globalization;
using System.Text;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// The cap this panel puts on a caller-chosen <c>User-Agent</c> before it is written into the
/// session row and the audit row of the same save.
/// </summary>
/// <remarks>
/// Every assertion here is about ONE question: can the value that comes back out be stored in
/// <c>character varying(512)</c>? PostgreSQL measures that column in characters, and a character
/// there is a code point — so the code point count of the result, not its text elements and not
/// its UTF-16 units, is what these tests hold. The type shipped with no test at all, and with a
/// cap that counted text elements, which is why the boundary cases below are the interesting ones.
/// </remarks>
public sealed class UserAgentTextTests
{
    /// <summary>The NUL character, spelled out because a raw one makes this file binary.</summary>
    private const string Nul = "\u0000";

    /// <summary>The zero width joiner, spelled out because it is invisible in source.</summary>
    private const string ZeroWidthJoiner = "\u200D";

    /// <summary>The strict encoder Npgsql writes parameters through, which throws where the default substitutes.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(true, true);

    /// <summary>A value one character short of the cap is returned exactly as it was sent.</summary>
    [Fact]
    public void A_value_one_character_short_of_the_cap_is_returned_exactly_as_it_was_sent()
    {
        var sent = new string('a', UserAgentText.MaxLength - 1);

        Assert.Equal(sent, UserAgentText.Capped(sent));
    }

    /// <summary>A value of exactly the cap is returned exactly as it was sent.</summary>
    [Fact]
    public void A_value_of_exactly_the_cap_is_returned_exactly_as_it_was_sent()
    {
        var sent = new string('a', UserAgentText.MaxLength);

        Assert.Equal(sent, UserAgentText.Capped(sent));
    }

    /// <summary>A value one character over the cap loses exactly its last character.</summary>
    [Fact]
    public void A_value_one_character_over_the_cap_loses_exactly_its_last_character()
    {
        var sent = new string('a', UserAgentText.MaxLength + 1);

        Assert.Equal(new string('a', UserAgentText.MaxLength), UserAgentText.Capped(sent));
    }

    /// <summary>An empty user agent is returned unchanged rather than treated as absent.</summary>
    [Fact]
    public void An_empty_user_agent_is_returned_unchanged_rather_than_treated_as_absent()
    {
        // A client that sends no header at all reaches this as the empty string, and the column is
        // required — so the empty string has to survive the cap rather than becoming null.
        Assert.Equal(string.Empty, UserAgentText.Capped(string.Empty));
    }

    /// <summary>A null user agent is refused at the call rather than dereferenced.</summary>
    [Fact]
    public void A_null_user_agent_is_refused_at_the_call_rather_than_dereferenced()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            UserAgentText.Capped(null!);
        });
    }

    /// <summary>An ascii value whose crlf sits on the boundary still fits the column.</summary>
    [Fact]
    public void An_ascii_value_whose_crlf_sits_on_the_boundary_still_fits_the_column()
    {
        // The plainest possible demonstration that text elements are the wrong unit: CR LF is ONE
        // text element and TWO characters, so a cap counting elements returned 513 characters here
        // for an input containing nothing but ASCII. No emoji required, and no unusual client
        // either — the header is whatever the caller typed.
        var sent = new string('a', UserAgentText.MaxLength - 1) + "\r\n" + new string('b', 100);

        AssertStorable(UserAgentText.Capped(sent));
    }

    /// <summary>A run of family emoji is capped to what the column counts and not to what a reader sees.</summary>
    [Fact]
    public void A_run_of_family_emoji_is_capped_to_what_the_column_counts_and_not_to_what_a_reader_sees()
    {
        // One family is a single text element built from four emoji and three zero width joiners:
        // seven code points that the column charges for one at a time. Capping at 512 elements sent
        // 3584 characters at a 512 character column.
        var family = string.Join(
            ZeroWidthJoiner,
            "\U0001F468",
            "\U0001F469",
            "\U0001F467",
            "\U0001F466");

        var sent = string.Concat(Enumerable.Repeat(family, 600));

        AssertStorable(UserAgentText.Capped(sent));
    }

    /// <summary>A run of regional indicator flags is capped even though it is few text elements.</summary>
    [Fact]
    public void A_run_of_regional_indicator_flags_is_capped_even_though_it_is_few_text_elements()
    {
        // 400 flags are only 400 text elements, so an element counting cap never reached its
        // cutting branch at all — it returned all 800 characters untouched.
        var sent = string.Concat(Enumerable.Repeat("\U0001F1E6\U0001F1F2", 400));

        AssertStorable(UserAgentText.Capped(sent));
    }

    /// <summary>A run of combining marks is capped by characters and not by rendered glyphs.</summary>
    [Fact]
    public void A_run_of_combining_marks_is_capped_by_characters_and_not_by_rendered_glyphs()
    {
        var sent = string.Concat(Enumerable.Repeat("e\u0301", 600));

        AssertStorable(UserAgentText.Capped(sent));
    }

    /// <summary>A run of non bmp emoji is capped by characters and never cut inside a surrogate pair.</summary>
    [Fact]
    public void A_run_of_non_bmp_emoji_is_capped_by_characters_and_never_cut_inside_a_surrogate_pair()
    {
        var sent = string.Concat(Enumerable.Repeat("\U0001F600", 600));

        var capped = UserAgentText.Capped(sent);

        AssertStorable(capped);

        // The cut landing between the two halves of a pair is the failure the type was written for:
        // a lone surrogate is not encodable as UTF-8, and Npgsql's encoder throws on it inside the
        // save that writes the session and the journal row together.
        Assert.Equal(0, capped.Length % 2);
    }

    /// <summary>A single grapheme longer than the whole cap is cut on a code point rather than overflowing.</summary>
    [Fact]
    public void A_single_grapheme_longer_than_the_whole_cap_is_cut_on_a_code_point_rather_than_overflowing()
    {
        // Nothing bounds the length of one ZWJ chain, so "keep every grapheme whole" and "never
        // exceed 512 characters" cannot both hold. This asserts which one gives way: the column
        // wins, because overflowing loses the entire audit row while a split cluster spoils one
        // glyph at the end of a diagnostic field.
        var chain = new StringBuilder("\U0001F468");

        for (var joins = 0; joins < 400; joins++)
        {
            chain.Append(ZeroWidthJoiner + "\U0001F469");
        }

        var sent = chain.ToString();

        var capped = UserAgentText.Capped(sent);

        Assert.Equal(1, TextElementsIn(sent));
        AssertStorable(capped);

        // Assert the VALUE, not just the bound. A cut that advanced its index by one UTF-16 unit
        // per code point instead of by the code point's own width still lands under the cap and
        // still encodes - it simply throws away a third of the field - so a test that only checks
        // "fits the column" cannot see it. The contract is to FILL the column, not to undershoot it.
        Assert.Equal(UserAgentText.MaxLength, CodePointsIn(capped));
    }

    /// <summary>A multi byte utf8 value under the cap survives the strict encoder untouched.</summary>
    [Fact]
    public void A_multi_byte_utf8_value_under_the_cap_survives_the_strict_encoder_untouched()
    {
        const string sent = "Mozilla/5.0 (Ереван; "
            + "Երևան; 東京) AppleWebKit/537.36";

        var capped = UserAgentText.Capped(sent);

        Assert.Equal(sent, capped);
        AssertStorable(capped);
    }

    /// <summary>Control characters including nul are preserved because the field is evidence and not a command.</summary>
    [Fact]
    public void Control_characters_including_nul_are_preserved_because_the_field_is_evidence_and_not_a_command()
    {
        // Deliberate: this type caps a length, it does not sanitise. An operator reading a session
        // list needs to see that the caller sent something strange, and stripping it here would
        // quietly rewrite the evidence. Rendering it safely is the SPA's escaping, as it is for
        // every other caller-supplied string.
        const string sent = "curl/8.0 " + Nul + "\r\n";

        Assert.Equal(sent, UserAgentText.Capped(sent));
    }

    /// <summary>Every capped value fits the column and encodes under the driver's strict fallback.</summary>
    /// <param name="capped">The value <see cref="UserAgentText.Capped"/> returned.</param>
    /// <remarks>
    /// Both assertions carry a positive control, because both measure on an axis that can go blind:
    /// a character counter that quietly counted text elements would report a small number for every
    /// input in this file, and an encoder configured with the default replacement fallback would
    /// accept every input in this file too. The controls prove each one can still say no.
    /// </remarks>
    private static void AssertStorable(string capped)
    {
        Assert.Equal(2, CodePointsIn("\U0001F600" + ZeroWidthJoiner));

        var characters = CodePointsIn(capped);

        Assert.True(
            characters <= UserAgentText.MaxLength,
            $"character varying({UserAgentText.MaxLength}) was handed {characters} characters");

        Assert.Throws<EncoderFallbackException>(() =>
        {
            StrictUtf8.GetBytes(capped + "\uD83D");
        });

        StrictUtf8.GetBytes(capped);
    }

    /// <summary>Counts the code points in a value, which is what PostgreSQL charges the column for.</summary>
    /// <param name="value">The value to measure.</param>
    /// <returns>The number of characters the column sees.</returns>
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

    /// <summary>Counts the grapheme clusters in a value.</summary>
    /// <param name="value">The value to measure.</param>
    /// <returns>The number of text elements a reader would perceive.</returns>
    private static int TextElementsIn(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var elements = 0;

        while (enumerator.MoveNext())
        {
            elements++;
        }

        return elements;
    }
}
