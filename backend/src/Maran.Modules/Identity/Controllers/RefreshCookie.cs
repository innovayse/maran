using Maran.Modules.Identity.Domain.ValueObjects;


namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The cookie the refresh token travels in, and the only place its attributes are written.
/// </summary>
/// <remarks>
/// Every attribute is load-bearing (spec §10). <c>HttpOnly</c> means an XSS cannot read the token;
/// <c>Secure</c> keeps it off plaintext HTTP; <c>SameSite=Strict</c> means no cross-site request
/// carries it, which is the CSRF defence; and the narrow <c>Path</c> means it is sent only to the
/// endpoints that rotate or revoke it rather than riding along on every API call, so a logging
/// proxy or a mistaken debug dump of an ordinary request cannot contain it.
///
/// Deleting must repeat all of them: a browser matches a deletion by name, path and domain, so a
/// cookie "deleted" with different attributes is simply not deleted.
/// </remarks>
public static class RefreshCookie
{
    /// <summary>The cookie's name.</summary>
    public const string Name = "maran_refresh";

    /// <summary>The only path the cookie is sent to.</summary>
    private const string CookiePath = "/api/v1/auth";

    /// <summary>Writes the refresh token as a cookie.</summary>
    /// <param name="response">The response to append the cookie to.</param>
    /// <param name="session">The session whose token is being handed out.</param>
    public static void Append(HttpResponse response, IssuedSession session)
    {
        response.Cookies.Append(Name, session.RefreshToken, Options(session.ExpiresAt));
    }

    /// <summary>Deletes the cookie, matching the attributes it was written with.</summary>
    /// <param name="response">The response to append the deletion to.</param>
    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(Name, Options(expires: null));
    }

    /// <summary>Builds the cookie's attributes.</summary>
    /// <param name="expires">When the cookie should expire, or null for a deletion.</param>
    /// <returns>The options, identical for writing and deleting apart from the expiry.</returns>
    private static CookieOptions Options(DateTimeOffset? expires)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expires,
        };
    }
}
