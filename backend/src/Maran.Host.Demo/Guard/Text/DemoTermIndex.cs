namespace Maran.Host.Demo.Guard.Text;

/// <summary>
/// The blocked terms of one fold, ready to be looked for in a text's words: terms that match whole words, and
/// stems that match any word starting with them.
/// </summary>
/// <remarks>
/// <para>
/// A line of a list is a whole-word term unless it ends in <c>*</c>, which makes its last word a stem:
/// <c>fuck*</c> matches <c>fuck</c>, <c>fucking</c> and <c>fucker</c>. The choice is made per term, because it
/// is a judgement per word: a short or ambiguous word (<c>nazi</c>, <c>escort</c>) is listed whole together with
/// the few inflections wanted, while a stem that no ordinary word starts with (<c>fuck</c>, <c>пизд</c>) covers
/// every inflection, which languages that inflect heavily need. A stem matches at the start of a word only, so
/// <c>cunt*</c> does not find <c>Scunthorpe</c>.
/// </para>
/// <para>
/// A stem has at least <see cref="MinimumStemLength"/> letters, so a typo in a list cannot ban every word that
/// starts with one letter.
/// </para>
/// </remarks>
internal sealed class DemoTermIndex
{
    /// <summary>The fewest letters a stem may have.</summary>
    internal const int MinimumStemLength = 3;

    /// <summary>The whole-word terms, as their words joined by a space.</summary>
    private readonly HashSet<string> _whole;

    /// <summary>The stems by the words that come before them (empty for a one-word stem), joined by a space.</summary>
    private readonly Dictionary<string, List<string>> _stems;

    /// <summary>The most words any term has, which bounds the windows tried over a text.</summary>
    private readonly int _longest;

    /// <summary>Builds the index of one fold over the listed terms.</summary>
    /// <param name="terms">The terms as written in the lists; blank entries are ignored.</param>
    /// <param name="strict">Whether the stricter fold is used.</param>
    /// <exception cref="ArgumentException">A stem is shorter than <see cref="MinimumStemLength"/> letters.</exception>
    public DemoTermIndex(IEnumerable<string> terms, bool strict)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var whole = new HashSet<string>(StringComparer.Ordinal);
        var stems = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var longest = 1;
        foreach (var entry in terms.Select(term => { return term.Trim(); }))
        {
            var isStem = entry.EndsWith('*');
            var words = DemoTextFold.TermWords(isStem ? entry[..^1] : entry, strict);
            if (words.Count == 0)
            {
                continue;
            }

            longest = Math.Max(longest, words.Count);
            if (!isStem)
            {
                whole.Add(string.Join(' ', words));
                continue;
            }

            if (words[^1].Length < MinimumStemLength)
            {
                throw new ArgumentException(
                    $"The stem '{entry}' is shorter than {MinimumStemLength} letters.",
                    nameof(terms));
            }

            var leading = string.Join(' ', words.Take(words.Count - 1));
            if (!stems.TryGetValue(leading, out var list))
            {
                list = [];
                stems[leading] = list;
            }

            list.Add(words[^1]);
        }

        _whole = whole;
        _stems = stems;
        _longest = longest;
    }

    /// <summary>Whether a term is a run of the words, as whole words or ending in a stem.</summary>
    /// <param name="words">The words of a text under the same fold.</param>
    /// <returns><c>true</c> when any term matches.</returns>
    public bool Matches(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        for (var start = 0; start < words.Count; start++)
        {
            var leading = string.Empty;
            for (var length = 1; length <= _longest && start + length <= words.Count; length++)
            {
                var last = words[start + length - 1];
                var window = leading.Length == 0 ? last : leading + " " + last;
                if (_whole.Contains(window) || StartsWithStem(leading, last))
                {
                    return true;
                }

                leading = window;
            }
        }

        return false;
    }

    /// <summary>Whether a word starts with a stem that follows the given leading words.</summary>
    /// <param name="leading">The words before the last one, joined by a space (empty when there are none).</param>
    /// <param name="word">The last word of the window.</param>
    /// <returns><c>true</c> when one of the stems after those leading words starts the word.</returns>
    private bool StartsWithStem(string leading, string word)
    {
        if (!_stems.TryGetValue(leading, out var stems))
        {
            return false;
        }

        foreach (var stem in stems)
        {
            if (word.StartsWith(stem, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
