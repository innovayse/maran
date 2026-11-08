namespace Maran.Host.Demo.Pseudonymisation;

/// <summary>Cuts a <c>User-Agent</c> down to a browser family, which is all the demo's journal may keep.</summary>
/// <remarks>
/// A full user agent plus an address narrows a visitor to a handful of people; the family alone does not.
/// The order of the tests matters: Edge, Opera and Chrome all say <c>Chrome/</c> and every browser says
/// <c>Safari/</c>, so the more specific token is looked for first.
/// </remarks>
public static class UserAgentFamilies
{
    /// <summary>What an empty header becomes.</summary>
    public const string None = "";

    /// <summary>The family of a header.</summary>
    /// <param name="header">The raw <c>User-Agent</c> header value.</param>
    /// <returns>A short family name, or <see cref="None"/> for an empty header.</returns>
    public static string Of(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return None;
        }

        if (Has(header, "Edg/") || Has(header, "EdgA/") || Has(header, "EdgiOS/"))
        {
            return "Edge";
        }

        if (Has(header, "OPR/") || Has(header, "Opera"))
        {
            return "Opera";
        }

        if (Has(header, "Firefox/") || Has(header, "FxiOS/"))
        {
            return "Firefox";
        }

        if (Has(header, "Chrome/") || Has(header, "CriOS/"))
        {
            return "Chrome";
        }

        if (Has(header, "Safari/"))
        {
            return "Safari";
        }

        return Has(header, "curl/") ? "curl" : "Other";
    }

    /// <summary>Whether the header contains a token.</summary>
    /// <param name="header">The header.</param>
    /// <param name="token">The token.</param>
    /// <returns><c>true</c> when it does, ignoring case.</returns>
    private static bool Has(string header, string token)
    {
        return header.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
