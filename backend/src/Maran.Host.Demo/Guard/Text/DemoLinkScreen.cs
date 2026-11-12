using System.Text.RegularExpressions;

namespace Maran.Host.Demo.Guard.Text;

/// <summary>
/// Finds links in demo text that go somewhere other than the allow-list: it answers whether a text names a
/// host off the list, or uses a scheme that runs script or carries content in place of naming a host.
/// </summary>
/// <remarks>
/// <para>
/// <b>What counts as a link.</b> <c>scheme://host</c> for every scheme (so <c>s3://</c>, <c>git://</c> and
/// <c>php://</c> are links and need an allowed host like any other); a special scheme (<c>http</c>, <c>https</c>,
/// <c>ftp</c>, <c>ws</c>, <c>wss</c>) with fewer than two slashes (<c>http:/host</c>, <c>http:host</c>), which a
/// browser reads the same; a protocol-relative <c>//host.tld</c>; <c>www.host</c>; <c>javascript:</c>,
/// <c>vbscript:</c> and <c>magnet:?</c>; and <c>data:</c> followed by a media type, <c>;</c> or <c>,</c> at the
/// start of a URL (after white space, a quote, <c>&lt;</c>, <c>&gt;</c> or <c>=</c>), so <c>url(data:image/png...)</c>
/// in a style sheet and <c>{data:1}</c> in a script are not.
/// </para>
/// <para>
/// <b>How the host is read.</b> The text is expected under <see cref="DemoTextFold.ForLinks"/>, which removes
/// control characters (a browser strips tab, line feed and carriage return from a URL, so
/// <c>http://example.com&lt;TAB&gt;.evil.com</c> is one host) and keeps accents (<c>exämple.com</c> is not
/// <c>example.com</c>). The authority ends at the first <c>/</c>, <c>\</c>, <c>?</c> or <c>#</c>, at white space and at
/// <c>&lt;</c> or <c>&gt;</c> (a browser cannot read a host through those). Quotes and the backtick do NOT end it,
/// because a browser keeps them in the host; only quotes, brackets and punctuation at its very end are taken as
/// the text around the link. The host must then be made of <c>a-z 0-9 . -</c> in non-empty labels, or it is
/// refused: percent escapes, quotes inside the host, internationalised names and bracketed IPv6 are all
/// beyond what this screen can judge, and what it cannot judge it refuses. Only after that is the host
/// compared with the allow-list: equal to an entry or a name under one.
/// </para>
/// <para>
/// <b>Two lists.</b> The allow-list names the documentation and test namespaces (<c>example.*</c>,
/// <c>test</c>, <c>invalid</c>): never loopback, because a link a server later fetches must not reach the
/// panel's own services. The documentation list (<c>w3.org</c>, <c>apache.org</c>, <c>schema.org</c>) is
/// allowed for <c>http</c> and <c>https</c> only, because XML namespaces, DOCTYPEs and licence headers in
/// the files a visitor edits carry exactly those and nothing else.
/// </para>
/// </remarks>
internal sealed class DemoLinkScreen
{
    /// <summary>The longest any one pattern may run on a text before the text is refused.</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The schemes a browser reads with fewer than two slashes after the colon.</summary>
    private static readonly HashSet<string> SpecialSchemes = new(StringComparer.Ordinal)
    {
        "http", "https", "ftp", "ws", "wss",
    };

    /// <summary>The schemes whose links a documentation host may be named in.</summary>
    private static readonly HashSet<string> WebSchemes = new(StringComparer.Ordinal) { "http", "https" };

    /// <summary>Punctuation that follows a link in prose and is not part of its host.</summary>
    private static readonly char[] TrailingPunctuation =
        ['.', ',', ';', ':', '!', '"', '\'', '`', ')', ']', '}', '*', '_'];

    /// <summary>
    /// A scheme, its colon and the slashes after it; the authority is read ahead without being consumed, so a
    /// link nested after another scheme (<c>blob:https://host</c>) is found in turn.
    /// </summary>
    private static readonly Regex SchemeUrl = new(
        @"(?<![a-z0-9+.\-])(?<scheme>[a-z][a-z0-9+.\-]{0,31}):(?<slashes>[/\\]*)(?=(?<authority>[^\s/\\?#<>]*))",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary>A protocol-relative link: two slashes (one of them forward) after anything but a word or a scheme.</summary>
    private static readonly Regex ProtocolRelative = new(
        @"(?<![a-z0-9+.\-:/\\])(?:/[/\\]|\\/)[/\\]*(?<authority>[^\s/\\?#<>]*)",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary>A scheme that runs script, or a magnet link.</summary>
    private static readonly Regex ContentScheme = new(
        @"(?<![a-z0-9+.\-])(?:(?:javascript|vbscript):|magnet:\?)",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary><c>data:</c> with a media type (or the empty one) at the start of a URL.</summary>
    private static readonly Regex DataScheme = new(
        @"(?<=^|[\s""'`<>=])data:(?:[a-z0-9][a-z0-9.+\-]*/[a-z0-9][a-z0-9.+\-]*|[;,])",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary>A link written with <c>www.</c> and no scheme.</summary>
    private static readonly Regex WwwUrl = new(
        @"(?<![a-z0-9\-])(?<host>www\d{0,3}\.[a-z0-9\-]+(?:\.[a-z0-9\-]+)*)",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary>The hosts any link may name, folded.</summary>
    private readonly IReadOnlyList<string> _allowedHosts;

    /// <summary>The hosts an <c>http</c> or <c>https</c> link may name besides those, folded.</summary>
    private readonly IReadOnlyList<string> _documentationHosts;

    /// <summary>Builds the screen over explicit lists.</summary>
    /// <param name="allowedHosts">The hosts any link may name; blank entries are ignored.</param>
    /// <param name="documentationHosts">The hosts only an <c>http</c> or <c>https</c> link may name; blank entries are ignored.</param>
    public DemoLinkScreen(IEnumerable<string> allowedHosts, IEnumerable<string> documentationHosts)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        ArgumentNullException.ThrowIfNull(documentationHosts);

        _allowedHosts = FoldHosts(allowedHosts);
        _documentationHosts = FoldHosts(documentationHosts);
    }

    /// <summary>Whether the text links somewhere that is not allowed.</summary>
    /// <param name="text">The text under <see cref="DemoTextFold.ForLinks"/>.</param>
    /// <returns><c>true</c> for a link to a host off the lists, a host this screen cannot judge, or a content or script scheme.</returns>
    /// <exception cref="RegexMatchTimeoutException">A pattern ran past its limit.</exception>
    public bool HasForeignUrl(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ContentScheme.IsMatch(text)
            || DataScheme.IsMatch(text)
            || HasForeignSchemeUrl(text)
            || HasForeignProtocolRelative(text)
            || HasForeignWww(text);
    }

    /// <summary>Folds the entries of a host list the way the text is folded.</summary>
    /// <param name="hosts">The entries.</param>
    /// <returns>The folded, non-blank entries.</returns>
    private static List<string> FoldHosts(IEnumerable<string> hosts)
    {
        return hosts
            .Where(host => { return !string.IsNullOrWhiteSpace(host); })
            .Select(host => { return DemoTextFold.ForLinks(host.Trim()); })
            .ToList();
    }

    /// <summary>The host of a URL authority: trailing prose, user information, port and brackets removed.</summary>
    /// <param name="authority">The text between the slashes and the first <c>/</c>, <c>\</c>, <c>?</c> or <c>#</c>.</param>
    /// <returns>The host, possibly empty.</returns>
    private static string HostOf(string authority)
    {
        var host = authority.StartsWith('[') ? authority : authority.TrimEnd(TrailingPunctuation);
        host = host[(host.LastIndexOf('@') + 1)..];
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);
            host = close > 0 ? host[1..close] : host[1..];
        }
        else
        {
            var colon = host.IndexOf(':', StringComparison.Ordinal);
            host = colon >= 0 ? host[..colon] : host;
        }

        return host.TrimEnd('.');
    }

    /// <summary>Whether a host is made of non-empty labels of <c>a-z 0-9 -</c>, the only shape this screen can judge.</summary>
    /// <param name="host">The host.</param>
    /// <returns><c>true</c> for a host of that shape.</returns>
    private static bool HasJudgeableShape(string host)
    {
        return host.Length is > 0 and <= 253
            && host.Split('.').All(label =>
            {
                return label.Length > 0
                    && label.All(c => { return c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'; });
            });
    }

    /// <summary>Whether a host is equal to a list entry or a name under one.</summary>
    /// <param name="host">The host.</param>
    /// <param name="list">The folded entries.</param>
    /// <returns><c>true</c> when it is.</returns>
    private static bool IsUnder(string host, IReadOnlyList<string> list)
    {
        return list.Any(entry =>
        {
            return string.Equals(host, entry, StringComparison.Ordinal)
                || host.EndsWith("." + entry, StringComparison.Ordinal);
        });
    }

    /// <summary>Whether a link of a scheme to an authority is allowed.</summary>
    /// <param name="scheme">The scheme; the documentation list applies to <c>http</c> and <c>https</c> only.</param>
    /// <param name="authority">The authority as it appears in the text.</param>
    /// <returns><c>true</c> when the host is judgeable and on a list that applies.</returns>
    private bool IsAllowed(string scheme, string authority)
    {
        var host = HostOf(authority);
        return HasJudgeableShape(host)
            && (IsUnder(host, _allowedHosts) || (WebSchemes.Contains(scheme) && IsUnder(host, _documentationHosts)));
    }

    /// <summary>Whether a <c>scheme:</c> link in the text goes off the lists.</summary>
    /// <param name="text">The folded text.</param>
    /// <returns><c>true</c> when one does.</returns>
    private bool HasForeignSchemeUrl(string text)
    {
        foreach (Match match in SchemeUrl.Matches(text))
        {
            var scheme = match.Groups["scheme"].Value;
            var authority = match.Groups["authority"].Value;
            // A one-letter scheme is a Windows drive (C:\\dir), not a link.
            var isLink = scheme.Length > 1
                && (match.Groups["slashes"].Length >= 2 || (SpecialSchemes.Contains(scheme) && authority.Length > 0));
            if (isLink && !IsAllowed(scheme, authority))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a protocol-relative link in the text goes off the lists.</summary>
    /// <remarks>
    /// Only an authority with a dot counts, so a code comment such as <c>//TODO</c> is not read as a host.
    /// </remarks>
    /// <param name="text">The folded text.</param>
    /// <returns><c>true</c> when one does.</returns>
    private bool HasForeignProtocolRelative(string text)
    {
        foreach (Match match in ProtocolRelative.Matches(text))
        {
            var authority = match.Groups["authority"].Value;
            if (HostOf(authority).Contains('.', StringComparison.Ordinal) && !IsAllowed("https", authority))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a <c>www.</c> link in the text goes off the lists.</summary>
    /// <param name="text">The folded text.</param>
    /// <returns><c>true</c> when one does.</returns>
    private bool HasForeignWww(string text)
    {
        foreach (Match match in WwwUrl.Matches(text))
        {
            if (!IsAllowed("https", match.Groups["host"].Value))
            {
                return true;
            }
        }

        return false;
    }
}
