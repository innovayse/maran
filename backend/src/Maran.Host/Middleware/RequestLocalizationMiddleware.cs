using System.Globalization;

namespace Maran.Host.Middleware;

/// <summary>
/// Picks the request's culture from <c>Accept-Language</c>, limited to the cultures the backend
/// ships resources for (English, Russian, Armenian), falling back to English. This is what makes
/// <c>.resx</c> lookups in <c>IErrorTextProvider</c> implementations match the caller's language
/// (rules/csharp.md "The backend owns all user-facing message text").
/// </summary>
public sealed class RequestLocalizationMiddleware
{
    /// <summary>Two-letter culture names the backend has (or will have) resources for.</summary>
    private static readonly string[] SupportedCultures = ["en", "ru", "hy"];

    /// <summary>The culture used when <c>Accept-Language</c> is absent or names no supported culture.</summary>
    private static readonly CultureInfo FallbackCulture = CultureInfo.GetCultureInfo("en");

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    public RequestLocalizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Sets the current thread's culture from the request, then invokes the rest of the pipeline.</summary>
    /// <param name="context">The current HTTP request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var culture = ResolveCulture(context);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        await _next(context);
    }

    /// <summary>
    /// Parses the <c>Accept-Language</c> header in quality-value order and returns the first
    /// supported culture it names; falls back to <see cref="FallbackCulture"/> when the header is
    /// absent, unparseable, or names only unsupported cultures.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    private static CultureInfo ResolveCulture(HttpContext context)
    {
        var header = context.Request.Headers.AcceptLanguage;
        if (header.Count == 0)
        {
            return FallbackCulture;
        }

        var candidates = header
            .SelectMany(value =>
            {
                return value?.Split(',') ?? [];
            })
            .Select(ParseLanguageTag)
            .Where(candidate =>
            {
                return candidate is not null;
            })
            .OrderByDescending(candidate =>
            {
                return candidate!.Value.Quality;
            });

        foreach (var candidate in candidates)
        {
            var match = SupportedCultures.FirstOrDefault(supported =>
            {
                return string.Equals(supported, candidate!.Value.Language, StringComparison.OrdinalIgnoreCase);
            });

            if (match is not null)
            {
                return CultureInfo.GetCultureInfo(match);
            }
        }

        return FallbackCulture;
    }

    /// <summary>
    /// Parses one <c>Accept-Language</c> entry (e.g. <c>"ru-RU;q=0.8"</c>) into its primary
    /// two-letter language tag and quality value.
    /// </summary>
    /// <param name="entry">One comma-separated segment of the header.</param>
    /// <returns>The parsed language tag and quality, or null when the entry is empty.</returns>
    private static (string Language, double Quality)? ParseLanguageTag(string entry)
    {
        var trimmed = entry.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var parts = trimmed.Split(';', StringSplitOptions.TrimEntries);
        var languageTag = parts[0];
        var primaryLanguage = languageTag.Split('-')[0];

        var quality = 1.0;
        if (parts.Length > 1
            && parts[1].StartsWith("q=", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(parts[1].AsSpan(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedQuality))
        {
            quality = parsedQuality;
        }

        return (primaryLanguage, quality);
    }
}
