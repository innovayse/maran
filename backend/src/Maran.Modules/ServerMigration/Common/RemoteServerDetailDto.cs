using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>
/// One registered remote server in full: the list's fields, the line the administrator installs on the server, the
/// host keys waiting for confirmation, and what the last test and discovery found. It carries the panel's PUBLIC key
/// only; the private half is never part of any response (rules/security.md item 8).
/// </summary>
/// <param name="Id">The server's identifier.</param>
/// <param name="Name">The label the administrator knows it by.</param>
/// <param name="Host">The DNS name or IP address the panel connects to.</param>
/// <param name="Port">The TCP port of the server's SSH daemon.</param>
/// <param name="SshUser">The login the panel's key is installed for.</param>
/// <param name="State">How far the server has got from registered to ready, or what stopped it.</param>
/// <param name="StateName">The state in the caller's language.</param>
/// <param name="HostKeyFingerprint">The fingerprint of the pinned host key, or <c>null</c> before one is confirmed.</param>
/// <param name="LastTestedAt">When the connection was last tried, or <c>null</c> if it never was.</param>
/// <param name="Kind">What kind of system the last successful test found, or <c>null</c> when none did.</param>
/// <param name="KindName">The kind in the caller's language, or <c>null</c> when there is none.</param>
/// <param name="AccountsFound">How many accounts the last discovery found, or <c>null</c> if none has run.</param>
/// <param name="CreatedAt">When the server was registered.</param>
/// <param name="AllowedFrom">The addresses the key is restricted to in the installed line, or <c>null</c> for anywhere.</param>
/// <param name="PublicKey">The panel's public key for this server, an OpenSSH public key line.</param>
/// <param name="AuthorizedKeysLine">The complete line to add to the server's <c>authorized_keys</c> for the login.</param>
/// <param name="HostKeyAlgorithm">The algorithm of the pinned host key, or <c>null</c> before one is confirmed.</param>
/// <param name="HostKeyConfirmedAt">When the administrator confirmed the host key, or <c>null</c>.</param>
/// <param name="HostKeyCandidates">The host keys the server offered at the last scan.</param>
/// <param name="LastFailure">Why the last attempt to connect failed, or <c>null</c> when it did not.</param>
/// <param name="LastFailureName">The failure in the caller's language, or <c>null</c> when there is none.</param>
/// <param name="SystemRelease">The server's operating system release as it reported itself, or <c>null</c>.</param>
/// <param name="MaranVersion">The panel version found on the server when it runs one, or <c>null</c>.</param>
/// <param name="IsRoot">Whether the login is the superuser, or <c>null</c> before a successful test.</param>
/// <param name="HomeFreeBytes">Free space under the home directories, or <c>null</c> when it was not measured.</param>
/// <param name="HomeTotalBytes">Size of the filesystem holding the home directories, or <c>null</c> when it was not measured.</param>
/// <param name="DatabaseServer">The database server found, or <c>null</c> when none was.</param>
/// <param name="WebServer">The web server found, or <c>null</c> when none was.</param>
/// <param name="MailFound">Whether mail storage was found, or <c>null</c> before a successful test.</param>
/// <param name="DiscoveredAt">When the last discovery finished, or <c>null</c> if none has.</param>
/// <param name="Warnings">What the last discovery could not read or did not report in full.</param>
public sealed record RemoteServerDetailDto(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string SshUser,
    RemoteServerState State,
    string StateName,
    string? HostKeyFingerprint,
    DateTimeOffset? LastTestedAt,
    RemoteSourceKind? Kind,
    string? KindName,
    int? AccountsFound,
    DateTimeOffset CreatedAt,
    string? AllowedFrom,
    string PublicKey,
    string AuthorizedKeysLine,
    string? HostKeyAlgorithm,
    DateTimeOffset? HostKeyConfirmedAt,
    IReadOnlyList<RemoteServerHostKeyCandidateDto> HostKeyCandidates,
    RemoteFailureKind? LastFailure,
    string? LastFailureName,
    string? SystemRelease,
    string? MaranVersion,
    bool? IsRoot,
    long? HomeFreeBytes,
    long? HomeTotalBytes,
    string? DatabaseServer,
    string? WebServer,
    bool? MailFound,
    DateTimeOffset? DiscoveredAt,
    IReadOnlyList<DiscoveryWarningDto> Warnings);
