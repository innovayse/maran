using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>One registered remote server as the list shows it.</summary>
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
public sealed record RemoteServerDto(
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
    DateTimeOffset CreatedAt);
