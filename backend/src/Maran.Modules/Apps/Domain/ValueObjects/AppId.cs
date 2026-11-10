using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Domain.ValueObjects;

/// <summary>
/// An app named by the closed catalogue and nothing else: <c>nginx</c>, <c>apache</c>,
/// <c>php-&lt;version&gt;</c> or <c>nodejs-&lt;major&gt;</c>. The only way a caller's text becomes something
/// the panel sends to the agent (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type.</b> The panel never names a package: it names an app and the agent maps that to the
/// operating system's packages. An id that is a bare <see cref="string"/> would let any text reach the
/// agent and the ledger; here an id exists only if it is a member of the catalogue, so a value that is not
/// one (a package name, a path, a version this panel does not offer, a control character) is refused at
/// the boundary and the agent's own revalidation is the second line, not the first
/// (rules/security.md "Input").
/// </para>
/// <para>
/// The versions are the ones the agent's catalogue carries: PHP 7.4 to 8.4 (the supported set of the
/// agent's <c>supported_versions.rs</c>) and Node.js 18, 20, 22 and 24. Whether a host's package sources
/// OFFER one is a different question, answered by <c>ListInstallableApps</c> and never by this type.
/// </para>
/// </remarks>
public readonly record struct AppId
{
    /// <summary>The PHP versions of the catalogue, newest first.</summary>
    public static readonly IReadOnlyList<string> PhpVersions = ["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"];

    /// <summary>The Node.js major versions of the catalogue, newest first.</summary>
    public static readonly IReadOnlyList<string> NodeVersions = ["24", "22", "20", "18"];

    /// <summary>The id of nginx.</summary>
    private const string NginxValue = "nginx";

    /// <summary>The id of Apache.</summary>
    private const string ApacheValue = "apache";

    /// <summary>The prefix of a PHP id.</summary>
    private const string PhpPrefix = "php-";

    /// <summary>The prefix of a Node.js id.</summary>
    private const string NodePrefix = "nodejs-";

    /// <summary>The catalogue family.</summary>
    public AppFamily Family { get; }

    /// <summary>The runtime version (<c>8.3</c>, <c>22</c>), or the empty string for a web server.</summary>
    public string Version { get; }

    /// <summary>Which of the catalogue's ids this is, as the agent and the API spell it.</summary>
    public string Value { get; }

    /// <summary>What the app is for.</summary>
    public AppKind Kind
    {
        get
        {
            return Family is AppFamily.Nginx or AppFamily.Apache ? AppKind.WebServer : AppKind.Runtime;
        }
    }

    /// <summary>Creates an id the parser has already judged to be a member of the catalogue.</summary>
    /// <param name="family">The family.</param>
    /// <param name="version">The runtime version, or the empty string for a web server.</param>
    /// <param name="value">The id text.</param>
    private AppId(AppFamily family, string version, string value)
    {
        Family = family;
        Version = version;
        Value = value;
    }

    /// <summary>The id of a catalogue web server or runtime, from its parts.</summary>
    /// <param name="family">The family.</param>
    /// <param name="version">The version of a runtime; ignored for a web server.</param>
    /// <returns>The id, or <see langword="null"/> when the version is not in the catalogue.</returns>
    public static AppId? Of(AppFamily family, string version)
    {
        return family switch
        {
            AppFamily.Nginx => new AppId(AppFamily.Nginx, string.Empty, NginxValue),
            AppFamily.Apache => new AppId(AppFamily.Apache, string.Empty, ApacheValue),
            AppFamily.Php when PhpVersions.Contains(version, StringComparer.Ordinal) =>
                new AppId(AppFamily.Php, version, PhpPrefix + version),
            AppFamily.Node when NodeVersions.Contains(version, StringComparer.Ordinal) =>
                new AppId(AppFamily.Node, version, NodePrefix + version),
            _ => null,
        };
    }

    /// <summary>Parses an id, accepting exactly the catalogue's spellings and nothing else.</summary>
    /// <param name="text">The candidate text; null, empty or anything outside the catalogue is refused.</param>
    /// <param name="appId">The id when the text is one; otherwise the default value, which must not be read.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a member of the catalogue.</returns>
    /// <remarks>
    /// Ordinal and exact: no trimming, no case folding. An id with a trailing space or an upper-case letter
    /// is not the catalogue's spelling, and accepting it would give one app two names.
    /// </remarks>
    public static bool TryParse(string? text, out AppId appId)
    {
        appId = default;

        if (text is null)
        {
            return false;
        }

        AppId? parsed = text switch
        {
            NginxValue => Of(AppFamily.Nginx, string.Empty),
            ApacheValue => Of(AppFamily.Apache, string.Empty),
            _ when text.StartsWith(PhpPrefix, StringComparison.Ordinal) =>
                Of(AppFamily.Php, text[PhpPrefix.Length..]),
            _ when text.StartsWith(NodePrefix, StringComparison.Ordinal) =>
                Of(AppFamily.Node, text[NodePrefix.Length..]),
            _ => null,
        };

        if (parsed is null)
        {
            return false;
        }

        appId = parsed.Value;
        return true;
    }

    /// <summary>The id text.</summary>
    /// <returns>The catalogue spelling.</returns>
    public override string ToString()
    {
        return Value;
    }
}
