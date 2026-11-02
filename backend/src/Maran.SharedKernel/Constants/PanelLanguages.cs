namespace Maran.SharedKernel.Constants;

/// <summary>
/// The languages the panel ships text in — its resx triples and the SPA's locale trees — named once,
/// so the request localization and every field that stores somebody's language agree on the list.
/// </summary>
/// <remarks>
/// It lives in the SharedKernel because the Host reads it to pick a request's culture and a module
/// reads it to validate a stored language (an account owner's, issue #194), and a module may not
/// reference the Host. A language added here owes its resx files and its SPA locale tree in the same
/// change: naming it here is a promise that text exists for it.
/// </remarks>
public static class PanelLanguages
{
    /// <summary>English: the invariant resx language and the fallback for anything unsupported.</summary>
    public const string English = "en";

    /// <summary>Russian.</summary>
    public const string Russian = "ru";

    /// <summary>Armenian.</summary>
    public const string Armenian = "hy";

    /// <summary>Every supported two-letter language name, English first.</summary>
    public static readonly IReadOnlyList<string> Supported = [English, Russian, Armenian];

    /// <summary>Says whether <paramref name="language"/> is exactly one of <see cref="Supported"/>.</summary>
    /// <remarks>
    /// Ordinal and case-sensitive on purpose: a stored language is compared and rendered as written,
    /// so <c>EN</c> is refused rather than accepted as a second spelling of <c>en</c>.
    /// </remarks>
    /// <param name="language">The candidate language name.</param>
    /// <returns><see langword="true"/> when the panel ships text in that language.</returns>
    public static bool IsSupported(string? language)
    {
        return language is not null && Supported.Contains(language, StringComparer.Ordinal);
    }
}
