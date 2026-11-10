namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Why the last attempt to connect to a remote server failed.</summary>
public enum RemoteFailureKind
{
    /// <summary>The server presented a host key other than the pinned one.</summary>
    HostKeyChanged,

    /// <summary>The server refused the panel's key.</summary>
    AuthRefused,

    /// <summary>No connection could be made or kept.</summary>
    Unreachable,

    /// <summary>The address resolves to somewhere the panel never connects (loopback, link-local, this host itself).</summary>
    AddressRefused,
}
