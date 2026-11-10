using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Domain.ValueObjects;

/// <summary>
/// What a successful connection test established about a remote server, handed to the server as one value
/// so the facts it records are always the facts of a single test.
/// </summary>
/// <param name="IsRoot">Whether the login is the superuser.</param>
/// <param name="HomeFreeBytes">Free space under the home directories, or null when not measured.</param>
/// <param name="HomeTotalBytes">Size of the filesystem holding the home directories, or null when not measured.</param>
/// <param name="SystemRelease">The operating system release as the server reported it, or null.</param>
/// <param name="Kind">What kind of system it is, or null when the test could not tell.</param>
/// <param name="MaranVersion">The panel version found on the server, or null.</param>
/// <param name="DatabaseServer">The database server found (<c>MariaDb</c> or <c>MySql</c>), or null when none.</param>
/// <param name="WebServer">The web server found (<c>Nginx</c> or <c>Apache</c>), or null when none.</param>
/// <param name="MailFound">Whether mail storage was found.</param>
public sealed record TestedSourceSnapshot(
    bool IsRoot,
    long? HomeFreeBytes,
    long? HomeTotalBytes,
    string? SystemRelease,
    RemoteSourceKind? Kind,
    string? MaranVersion,
    string? DatabaseServer,
    string? WebServer,
    bool MailFound);
