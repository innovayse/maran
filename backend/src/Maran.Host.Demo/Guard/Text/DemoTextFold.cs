using System.Globalization;
using System.Text;

namespace Maran.Host.Demo.Guard.Text;

/// <summary>
/// The folds the demo's text screen reads text through, so two spellings of the same thing compare equal: the
/// plain fold (compatibility forms, accents, invisible characters, case), the stricter fold (look-alike
/// letters of other scripts, digit and symbol swaps) and the splitting of folded text into words.
/// </summary>
/// <remarks>
/// A fold is applied identically to the text and to the listed term, so a trick that changes the text changes
/// the term the same way and the two still meet.
/// </remarks>
internal static class DemoTextFold
{
    /// <summary>Characters written between the letters of a word to break it up: they are dropped to rejoin it.</summary>
    private const string Joiners = ".-*_'’·+~^";

    /// <summary>
    /// Characters that draw nothing yet are not format or mark characters, so the Unicode category alone keeps
    /// them: the Hangul fillers (U+115F, U+1160, U+3164, U+FFA0) and the blank braille cell (U+2800).
    /// </summary>
    private static readonly HashSet<int> InvisibleSeparators = [0x115F, 0x1160, 0x3164, 0xFFA0, 0x2800];

    /// <summary>
    /// Letters of other scripts and Latin letters NFKD does not decompose that look like a Latin letter, mapped to
    /// it by the stricter fold. Keys are lower case; the comment names the glyph.
    /// </summary>
    private static readonly Dictionary<char, char> Lookalikes = new Dictionary<char, char>
    {
        // Cyrillic: a v e k m n o r c t u x, then i s j h d v y l q w.
        ['а'] = 'a',
        ['в'] = 'b',
        ['е'] = 'e',
        ['к'] = 'k',
        ['м'] = 'm',
        ['н'] = 'h',
        ['о'] = 'o',
        ['р'] = 'p',
        ['с'] = 'c',
        ['т'] = 't',
        ['у'] = 'y',
        ['х'] = 'x',
        ['і'] = 'i',
        ['ѕ'] = 's',
        ['ј'] = 'j',
        ['һ'] = 'h',
        ['ԁ'] = 'd',
        ['ѵ'] = 'v',
        ['ү'] = 'y',
        ['ӏ'] = 'l',
        ['ԛ'] = 'q',
        ['ԝ'] = 'w',
        ['ѡ'] = 'w',

        // Greek: alpha omicron rho nu iota tau epsilon kappa upsilon eta chi beta omega final-sigma lunate-sigma.
        ['α'] = 'a',
        ['ο'] = 'o',
        ['ρ'] = 'p',
        ['ν'] = 'v',
        ['ι'] = 'i',
        ['τ'] = 't',
        ['ε'] = 'e',
        ['κ'] = 'k',
        ['υ'] = 'u',
        ['η'] = 'n',
        ['χ'] = 'x',
        ['β'] = 'b',
        ['ω'] = 'w',
        ['ς'] = 's',
        ['ϲ'] = 'c',

        // Armenian: seh oh vo co ho qe za.
        ['ս'] = 'u',
        ['օ'] = 'o',
        ['ո'] = 'n',
        ['ց'] = 'g',
        ['հ'] = 'h',
        ['ք'] = 'p',
        ['ա'] = 'u',
        ['զ'] = 'q',

        // Latin letters that carry a stroke or no dot, which no decomposition removes.
        ['ı'] = 'i',
        ['ø'] = 'o',
        ['ł'] = 'l',
        ['đ'] = 'd',
        ['ħ'] = 'h',
        ['ŧ'] = 't',
        ['ð'] = 'd',
    };

    /// <summary>Digits and symbols written in place of a letter, mapped by the stricter fold inside words with letters.</summary>
    private static readonly Dictionary<char, char> LetterSwaps = new Dictionary<char, char>
    {
        ['0'] = 'o',
        ['1'] = 'i',
        ['3'] = 'e',
        ['4'] = 'a',
        ['5'] = 's',
        ['7'] = 't',
        ['@'] = 'a',
        ['$'] = 's',
    };

    /// <summary>
    /// Folds text so equivalent spellings compare equal: compatibility forms composed, accents and every format
    /// or invisible character dropped (outside the basic plane too), case folded, and the ideographic full stop
    /// read as a dot, which is how a browser reads it in a host.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text; control characters are kept, because they separate words.</returns>
    /// <exception cref="ArgumentException">The text is not well-formed Unicode.</exception>
    internal static string Plain(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark
                || InvisibleSeparators.Contains(rune.Value))
            {
                continue;
            }

            builder.Append(rune.Value == 0x3002 ? "." : Rune.ToLowerInvariant(rune).ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Folds text for reading links: compatibility forms composed (full-width letters become plain ones),
    /// format and invisible characters and control characters dropped, case folded, and the ideographic full stop
    /// read as a dot. Unlike <see cref="Plain"/> it KEEPS accents: a host such as <c>exämple.com</c> is a
    /// different host from <c>example.com</c> and must stay recognisably non-ASCII, so that it is refused rather
    /// than mistaken for the allowed one. Control characters go because a URL parser removes tab, line feed and
    /// carriage return, so a link broken up by one is still one link to a browser.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    /// <exception cref="ArgumentException">The text is not well-formed Unicode.</exception>
    internal static string ForLinks(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Format or UnicodeCategory.Control
                || InvisibleSeparators.Contains(rune.Value))
            {
                continue;
            }

            builder.Append(rune.Value == 0x3002 ? "." : Rune.ToLowerInvariant(rune).ToString());
        }

        return builder.ToString();
    }

    /// <summary>Maps look-alike letters of other scripts to the Latin letter they imitate.</summary>
    /// <param name="plain">The text under <see cref="Plain"/>.</param>
    /// <returns>The text under the stricter fold (digit and symbol swaps are applied per word, in <see cref="TextWords"/>).</returns>
    internal static string Strict(string plain)
    {
        var builder = new StringBuilder(plain.Length);
        foreach (var c in plain)
        {
            builder.Append(Lookalikes.TryGetValue(c, out var latin) ? latin : c);
        }

        return builder.ToString();
    }

    /// <summary>The words of a listed term: its folded letters and digits, nothing joined.</summary>
    /// <param name="term">The term as written in a list, without a trailing stem mark.</param>
    /// <param name="strict">Whether the stricter fold is used.</param>
    /// <returns>The words.</returns>
    /// <exception cref="ArgumentException">The term is not well-formed Unicode.</exception>
    internal static List<string> TermWords(string term, bool strict)
    {
        var folded = Plain(term);
        return SwapLetters(Split(strict ? Strict(folded) : folded, strict), strict);
    }

    /// <summary>The words of a text: runs of letters and digits, with runs of single characters read as one word.</summary>
    /// <param name="folded">The text under <see cref="Plain"/>, and under <see cref="Strict"/> too when strict.</param>
    /// <param name="strict">Whether digit and symbol swaps are applied inside words that have letters.</param>
    /// <param name="joined">
    /// Whether the characters written inside words to break them up (<c>.</c> <c>-</c> <c>*</c> <c>_</c> and
    /// the like) are dropped first, so <c>fu*ck</c> and <c>sh_it</c> read as one word each.
    /// </param>
    /// <returns>The words.</returns>
    internal static List<string> TextWords(string folded, bool strict, bool joined)
    {
        var text = joined ? RemoveJoiners(folded) : folded;
        return SwapLetters(MergeSingleCharacterRuns(Split(text, strict)), strict);
    }

    /// <summary>Drops the characters people write between letters to break a word up.</summary>
    /// <param name="folded">The folded text.</param>
    /// <returns>The text without them.</returns>
    private static string RemoveJoiners(string folded)
    {
        var builder = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (!Joiners.Contains(c, StringComparison.Ordinal))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits folded text into words: runs of letters and digits (and, when strict, the swap symbols).</summary>
    /// <param name="folded">The folded text.</param>
    /// <param name="strict">Whether <c>@</c> and <c>$</c> belong to a word, because they stand in for letters.</param>
    /// <returns>The words.</returns>
    private static List<string> Split(string folded, bool strict)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in folded)
        {
            if (char.IsLetterOrDigit(c) || (strict && c is '@' or '$'))
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>Joins every run of three or more one-character words into a single word.</summary>
    /// <param name="words">The words.</param>
    /// <returns>The words with such runs joined.</returns>
    private static List<string> MergeSingleCharacterRuns(List<string> words)
    {
        var merged = new List<string>();
        var index = 0;
        while (index < words.Count)
        {
            var end = index;
            while (end < words.Count && words[end].Length == 1)
            {
                end++;
            }

            if (end - index >= 3)
            {
                merged.Add(string.Concat(words.Skip(index).Take(end - index)));
                index = end;
                continue;
            }

            merged.Add(words[index]);
            index++;
        }

        return merged;
    }

    /// <summary>
    /// Maps digits and symbols that stand in for letters, but only inside a word that has a letter of its own:
    /// <c>5h1t</c> is a word with swaps, while <c>455</c>, a port or a cron field is a number and stays one.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="strict">Whether the swaps apply at all.</param>
    /// <returns>The words with swaps applied.</returns>
    private static List<string> SwapLetters(List<string> words, bool strict)
    {
        if (!strict)
        {
            return words;
        }

        return words.Select(SwapWord).ToList();
    }

    /// <summary>Applies the swaps to one word if it has a letter of its own.</summary>
    /// <param name="word">The word.</param>
    /// <returns>The word with swaps applied, or unchanged when it has no letter.</returns>
    private static string SwapWord(string word)
    {
        if (!word.Any(char.IsLetter))
        {
            return word;
        }

        var builder = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            builder.Append(LetterSwaps.TryGetValue(c, out var letter) ? letter : c);
        }

        return builder.ToString();
    }
}
