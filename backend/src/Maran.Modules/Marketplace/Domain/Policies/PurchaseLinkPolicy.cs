namespace Maran.Modules.Marketplace.Domain.Policies;

/// <summary>
/// Decides which purchase address the panel shows. The address comes from the marketplace's answer and is
/// rendered as a link in the administrator's browser, so only a plain https address with no credentials in
/// it is passed on; anything else is dropped and the screen shows the price without a link.
/// </summary>
public static class PurchaseLinkPolicy
{
    /// <summary>The longest address passed on.</summary>
    public const int MaxLength = 2048;

    /// <summary>Returns the address when it may be shown, and null otherwise.</summary>
    /// <param name="url">The address the marketplace gave, or null.</param>
    /// <returns>The same text when it is an absolute https address of at most <see cref="MaxLength"/> characters with no user information; otherwise null.</returns>
    public static string? HttpsOnly(string? url)
    {
        if (url is null || url.Length > MaxLength || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        return parsed.Scheme == Uri.UriSchemeHttps && parsed.UserInfo.Length == 0 && !url.Any(char.IsControl)
            ? url
            : null;
    }
}
