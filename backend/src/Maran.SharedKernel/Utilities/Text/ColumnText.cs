namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// The rule for shortening a string so that a <c>character varying(n)</c> column can hold it:
/// counted in the unit the column counts, and cut where the value stays encodable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The column counts code points, and nothing else does.</b> PostgreSQL measures
/// <c>character varying(n)</c> in characters, and a character there is a code point.
/// <see cref="string.Length"/> counts UTF-16 code units and a text element enumerator counts
/// grapheme clusters; neither equals the column's unit, and the disagreement runs both ways. One
/// family emoji is a single text element and seven code points, so a cap counted in text elements
/// bounds nothing. A CR LF pair is one text element and two characters, which is enough to put a
/// pure-ASCII value one character over a cap that believed it was under. Over the line PostgreSQL
/// raises 22001 and refuses the whole statement rather than truncating, so the row is lost.
/// </para>
/// <para>
/// <b>The cut falls on a code point, never inside a surrogate pair.</b> Slicing at a fixed UTF-16
/// index can land between the two halves of one character and leave a LONE SURROGATE. That value
/// cannot be encoded as UTF-8: .NET's ambient <c>Encoding.UTF8</c> substitutes U+FFFD for it, but
/// Npgsql's write buffer holds a <c>UTF8Encoding</c> with an <c>EncoderExceptionFallback</c> and
/// throws instead — inside the save, taking every other change in it down as well.
/// <see cref="System.Text.Rune"/> boundaries are exactly code point boundaries, so walking runes
/// gives both properties at once: the count the column charges, and a cut it can encode.
/// </para>
/// <para>
/// <b>Shortening, not refusing.</b> The callers are evidence fields — a journal subject, a task's
/// log — recorded alongside an operation rather than acted upon. Refusing the operation because its
/// description is long inverts the cost: what an over-long value threatens is the record of what
/// happened, and a shortened record beats none. A field the panel READS BACK and acts on belongs
/// behind a validator that refuses, not behind this.
/// </para>
/// <para>
/// <b>It lives in SharedKernel because the mistake is not one module's.</b> Every module writes
/// caller-supplied text into a widthed column through the shared journal, and a module may not
/// import another module's types (rules/architecture.md), so the first disqualification test in
/// rules/csharp.md settles it: not module-specific, therefore <c>Utilities/&lt;Subject&gt;/</c>. It
/// is a pure function of its two arguments, which keeps it out of <c>Services/</c> and
/// <c>Domain/</c>.
/// </para>
/// </remarks>
public static class ColumnText
{
    /// <summary>Shortens a value to what a <c>character varying(n)</c> column can hold.</summary>
    /// <param name="value">The text on its way to the column.</param>
    /// <param name="maxCharacters">The column's width, in characters as PostgreSQL counts them.</param>
    /// <returns>
    /// <paramref name="value"/> when it already fits, otherwise its longest prefix of at most
    /// <paramref name="maxCharacters"/> code points, cut on a code point boundary.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCharacters"/> is negative.</exception>
    public static string Fit(string value, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        // A string of N UTF-16 units holds at most N code points, so a value this short cannot be
        // over the column's count. The fast path is a bound, not an optimisation: it is the reason
        // the walk below is reached only by values that might actually overflow.
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        var runes = value.EnumerateRunes();
        var taken = 0;
        var end = 0;

        while (taken < maxCharacters && runes.MoveNext())
        {
            taken++;
            end += runes.Current.Utf16SequenceLength;
        }

        return value[..end];
    }
}
