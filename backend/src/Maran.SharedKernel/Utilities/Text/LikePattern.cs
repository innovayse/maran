namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// Turns a caller's search term into a <c>LIKE</c>/<c>ILIKE</c> pattern that matches the term
/// literally, anywhere in the value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escaping, not parameterizing, is what this is for.</b> A pattern passed to
/// <c>EF.Functions.ILike</c> as a captured variable is already a SQL parameter, so the term cannot
/// inject SQL. What it can still do is speak the pattern language: a search for <c>50%</c> or
/// <c>my_site</c> would otherwise match "50 anything" and "my, any one character, site", and a term
/// of <c>%</c> alone would match every row. Each of <c>\</c>, <c>%</c> and <c>_</c> is therefore
/// prefixed with a backslash, the backslash first so the escapes added for the other two are not
/// themselves escaped again.
/// </para>
/// <para>
/// The escape character MUST be passed to the query as well:
/// <c>EF.Functions.ILike(column, pattern, LikePattern.EscapeCharacter)</c>. Without it Npgsql
/// writes <c>ESCAPE ''</c>, which switches escaping off, so every backslash this adds becomes a
/// literal and <c>_</c> is a wildcard again — measured by the endpoint tests searching for
/// <c>_</c>, which matched every row until the argument was passed. An earlier version of this
/// remark said the two-argument form was correct because the backslash is PostgreSQL's default;
/// it is the default only when no <c>ESCAPE</c> clause is written, and the provider writes one.
/// </para>
/// </remarks>
public static class LikePattern
{
    /// <summary>
    /// The escape character the pattern is written with; pass it as the third argument of
    /// <c>EF.Functions.ILike</c> so the query's <c>ESCAPE</c> clause agrees with the pattern.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>The escape character as used while building the pattern.</summary>
    private const string Escape = EscapeCharacter;

    /// <summary>The pattern wildcard matching any run of characters.</summary>
    private const string AnyRun = "%";

    /// <summary>Builds a pattern matching <paramref name="term"/> literally anywhere in a value.</summary>
    /// <param name="term">The caller's search term.</param>
    /// <returns><c>%term%</c> with every <c>\</c>, <c>%</c> and <c>_</c> in the term escaped.</returns>
    public static string Contains(string term)
    {
        var escaped = term
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace(AnyRun, Escape + AnyRun, StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

        return AnyRun + escaped + AnyRun;
    }
}
