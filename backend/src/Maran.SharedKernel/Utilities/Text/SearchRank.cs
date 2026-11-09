namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// How well a search term matches the fields of one candidate: the order the global search shows its
/// hits in, decided once for every provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>It ranks candidates a database already matched; it never decides whether something matches.</b>
/// The provider's query selects rows with <c>ILIKE</c>, and this orders what came back. A candidate none
/// of whose fields contain the term (which the query would not have returned) ranks <see cref="NoMatch"/>.
/// </para>
/// <para>
/// Comparison is ordinal and case-insensitive, the same fold <c>ILIKE</c> applies to ASCII, so a hit never
/// ranks as a miss because of how the term was capitalised. No accent folding: the database pattern does
/// not fold accents either, and a rank that credited a match the query could not have found would be a
/// second definition of "matches".
/// </para>
/// </remarks>
public static class SearchRank
{
    /// <summary>A field equals the term.</summary>
    public const int Exact = 0;

    /// <summary>A field starts with the term.</summary>
    public const int Prefix = 1;

    /// <summary>A field contains the term somewhere inside.</summary>
    public const int Substring = 2;

    /// <summary>No field contains the term.</summary>
    public const int NoMatch = 3;

    /// <summary>Ranks a candidate by its best-matching field.</summary>
    /// <param name="term">The caller's search term.</param>
    /// <param name="fields">The candidate's searchable fields; null and empty fields never match.</param>
    /// <returns>The lowest (best) rank any field earns: <see cref="Exact"/> through <see cref="NoMatch"/>.</returns>
    public static int Of(string term, params string?[] fields)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(fields);

        var best = NoMatch;

        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field))
            {
                continue;
            }

            best = Math.Min(best, RankOf(term, field));
        }

        return best;
    }

    /// <summary>Ranks one field against the term.</summary>
    /// <param name="term">The caller's search term.</param>
    /// <param name="field">A non-empty field.</param>
    /// <returns>The field's rank.</returns>
    private static int RankOf(string term, string field)
    {
        if (string.Equals(field, term, StringComparison.OrdinalIgnoreCase))
        {
            return Exact;
        }

        if (field.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return Prefix;
        }

        return field.Contains(term, StringComparison.OrdinalIgnoreCase) ? Substring : NoMatch;
    }
}
