namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>
/// Where a registered remote server stands between "registered" and "ready to read", or what stopped it.
/// The state is the panel's record of its last decision or observation, never a live measurement.
/// </summary>
public enum RemoteServerState
{
    /// <summary>Registered; the host keys it offers are waiting for an administrator to compare and confirm one.</summary>
    HostKeyUnconfirmed,

    /// <summary>A host key is pinned and nothing has been tried with it yet, or the pin was just replaced.</summary>
    Pinned,

    /// <summary>The last connection test succeeded with the pinned host key.</summary>
    Ready,

    /// <summary>The server presented a host key other than the pinned one. Nothing is read until an administrator decides.</summary>
    HostKeyChanged,

    /// <summary>The server refused the panel's key.</summary>
    AuthRefused,

    /// <summary>The server could not be reached, or its address is one the panel refuses to connect to.</summary>
    Unreachable,
}
