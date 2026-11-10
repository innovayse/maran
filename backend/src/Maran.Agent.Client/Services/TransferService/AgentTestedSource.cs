namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What the fixed probes established about a source.</summary>
/// <remarks>
/// Every member is an observation, never an assumption. A probe that could not run leaves its member
/// <c>null</c>: the wire's "unknown" (empty text, a zero beside a <c>*_known</c> false, an unspecified
/// enum) is read once, in <c>TransferMapper</c>, so the panel's model never carries a zero it did not
/// receive.
/// </remarks>
/// <param name="IsRoot">The remote login is root (uid 0).</param>
/// <param name="HomeFreeBytes">Free bytes on the filesystem that holds the account homes, or <c>null</c> when it could not be read.</param>
/// <param name="HomeTotalBytes">Total bytes of that filesystem, or <c>null</c> when it could not be read.</param>
/// <param name="SystemRelease">The source's <c>PRETTY_NAME</c> from its os-release file, bounded and printable, or <c>null</c>.</param>
/// <param name="Kind">What kind of host the source is, or <c>null</c> when it was not determined.</param>
/// <param name="MaranVersion">The version string of the source's panel when <paramref name="Kind"/> is <see cref="SourceKind.Maran"/>, otherwise <c>null</c>.</param>
/// <param name="DatabaseServer">The database server found, or <c>null</c> when none was found or the probe could not run.</param>
/// <param name="WebServer">The web server found, or <c>null</c> when none was found or the probe could not run.</param>
/// <param name="MailFound">Whether a mail store was found. Mail is not migrated; the report warns.</param>
public sealed record AgentTestedSource(
    bool IsRoot,
    long? HomeFreeBytes,
    long? HomeTotalBytes,
    string? SystemRelease,
    SourceKind? Kind,
    string? MaranVersion,
    SourceDatabaseServer? DatabaseServer,
    SourceWebServer? WebServer,
    bool MailFound);
