using System.Diagnostics.CodeAnalysis;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// What this panel accepts as its own public address: the origin (<c>https://host[:port]</c>) every
/// link it puts in a mail or shows an administrator starts with. The single definition both the
/// start-up check of the setting and any future reader share, so the shape cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// <b>Strict on purpose, because the value is written into a mail the panel sends in its own name.</b>
/// A malformed origin is not cosmetic: user information (<c>https://panel.example.com@evil.example</c>)
/// makes a browser open a different host than the one the reader sees, and a path or query would
/// change what the appended token link means. So the accepted shape is exactly an origin: the
/// <c>https</c> scheme, a host that is a DNS name, <c>localhost</c> or an IP address, an optional port
/// of 1 to 65535, and at most one trailing slash. Nothing else survives.
/// </para>
/// <para>
/// <b>Raw text is checked before it is parsed.</b> <see cref="Uri"/> silently drops tabs, line feeds
/// and carriage returns inside a URL and folds a backslash into a slash, so a value that parses
/// cleanly may not be the value that was written. The value also travels through a line-oriented
/// environment file (rules/security.md item 4), so any control or whitespace character, any
/// backslash and any percent sign is refused on the text itself, before the parser can normalise it
/// away.
/// </para>
/// <para>
/// <b>No percent sign, so no zone id.</b> A scoped IPv6 literal (<c>https://[fe80::1%25eth0]:8443</c>)
/// names an interface of the machine that reads it, not an address a mail recipient can reach, and a
/// zone id must not appear in a link. <see cref="Uri"/> accepts it, so the refusal is on the raw text;
/// it also refuses a percent-encoded host name, which is not the host the reader sees. The installer's
/// shell gate (<c>panel_public_url_is_valid</c>) has no <c>%</c> in its alphabet, so the two agree.
/// </para>
/// <para>
/// <b>Plain HTTP is refused, loopback is not.</b> A credential link over <c>http</c> is readable on
/// the path; the panel itself only serves HTTPS. A development host uses <c>https://localhost:…</c>,
/// which is why that one single-label name is accepted next to the two-or-more-label names
/// <see cref="HostNameRule"/> allows.
/// </para>
/// </remarks>
public static class PublicUrlRule
{
    /// <summary>The longest value accepted, well above any real origin and far below a request limit.</summary>
    public const int MaximumLength = 2048;

    /// <summary>The scheme prefix every accepted value starts with, compared without regard to case.</summary>
    private const string SchemePrefix = "https://";

    /// <summary>The one single-label host name accepted, for a development machine.</summary>
    private const string LoopbackName = "localhost";

    /// <summary>Reduces a configured value to its canonical origin, or refuses it.</summary>
    /// <param name="value">The configured text, exactly as read from configuration.</param>
    /// <param name="normalised">
    /// The origin in its one spelling: lower-case host, IPv6 in brackets, the port only when it is not
    /// 443, and no trailing slash. Null when the value is refused.
    /// </param>
    /// <returns>True when the value is an acceptable public origin.</returns>
    public static bool TryNormalise(string? value, [NotNullWhen(true)] out string? normalised)
    {
        normalised = null;

        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength || ContainsRefusedCharacter(value))
        {
            return false;
        }

        if (!value.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase)
            || !HasOnlyAnOriginAfterTheScheme(value[SchemePrefix.Length..]))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.UserInfo.Length > 0 || uri.Port is < 1 or > 65535 || !IsAcceptableHost(uri))
        {
            return false;
        }

        normalised = uri.IsDefaultPort
            ? $"{SchemePrefix}{uri.Host}"
            : $"{SchemePrefix}{uri.Host}:{uri.Port}";
        return true;
    }

    /// <summary>
    /// The DNS host name of an acceptable public origin, in lower case, for the reader that must know
    /// which name the panel is reached by and not only whether the origin is well formed.
    /// </summary>
    /// <param name="value">The configured text, exactly as read from configuration.</param>
    /// <param name="hostName">
    /// The host of the origin. Null when the value is refused, and also when its host is not a DNS
    /// name worth reserving: an IP address (<see cref="ReservedHostNameRule"/> already refuses every
    /// address-shaped name) or <c>localhost</c>.
    /// </param>
    /// <returns>True when the value is an acceptable origin whose host is a DNS name.</returns>
    public static bool TryGetDnsName(string? value, [NotNullWhen(true)] out string? hostName)
    {
        hostName = null;

        if (!TryNormalise(value, out var origin))
        {
            return false;
        }

        var uri = new Uri(origin);
        if (uri.HostNameType != UriHostNameType.Dns || uri.DnsSafeHost == LoopbackName)
        {
            return false;
        }

        hostName = uri.DnsSafeHost;
        return true;
    }

    /// <summary>Whether a value is an acceptable public origin.</summary>
    /// <param name="value">The configured text.</param>
    /// <returns>True when <see cref="TryNormalise"/> accepts it.</returns>
    public static bool IsPublicUrl(string? value)
    {
        return TryNormalise(value, out _);
    }

    /// <summary>
    /// Whether the text holds a control character, whitespace, a backslash, a percent sign or any
    /// non-ASCII character anywhere. A non-ASCII host would be accepted by <see cref="Uri"/> as an
    /// internationalised name and written into a mail in its Unicode spelling, which the installer's
    /// character gate never produces.
    /// </summary>
    /// <param name="value">The raw text.</param>
    /// <returns>True when any character is one a URL written into a mail and an env file must not hold.</returns>
    private static bool ContainsRefusedCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character)
                || char.IsWhiteSpace(character)
                || character == '\\'
                || character == '%'
                || character > '\u007f')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether what follows the scheme is an authority and nothing more: no user information, and no
    /// path, query or fragment other than a single closing slash.
    /// </summary>
    /// <param name="afterScheme">The text after <c>https://</c>.</param>
    /// <returns>True when the first <c>/</c>, <c>?</c> or <c>#</c> is absent or is the very last character and a slash.</returns>
    private static bool HasOnlyAnOriginAfterTheScheme(string afterScheme)
    {
        if (afterScheme.Contains('@'))
        {
            return false;
        }

        var boundary = afterScheme.AsSpan().IndexOfAny('/', '?', '#');
        var authorityLength = boundary < 0 ? afterScheme.Length : boundary;

        // A colon with no port after it ("https://host:") is accepted by Uri as the default port; the
        // installer's gate refuses it, and the two must say the same thing about the same text.
        if (authorityLength == 0 || afterScheme[authorityLength - 1] == ':')
        {
            return false;
        }

        return boundary < 0 || (boundary == afterScheme.Length - 1 && afterScheme[boundary] == '/');
    }

    /// <summary>Whether the parsed host is a DNS name of the panel's shape, <c>localhost</c>, or an IP address.</summary>
    /// <param name="uri">The parsed address.</param>
    /// <returns>True when the host may be the panel's.</returns>
    private static bool IsAcceptableHost(Uri uri)
    {
        switch (uri.HostNameType)
        {
            case UriHostNameType.IPv4:
                return true;
            case UriHostNameType.IPv6:
                return true;
            case UriHostNameType.Dns:
                return uri.DnsSafeHost == LoopbackName || HostNameRule.IsHostName(uri.DnsSafeHost);
            default:
                return false;
        }
    }
}
