namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// The bounds the API holds the fields of a remote server to, written once for the validator and for the tests
/// that cross each of them.
/// </summary>
public static class RemoteServerFieldLimits
{
    /// <summary>The longest label a server may be given.</summary>
    public const int NameMaxLength = 80;

    /// <summary>The longest host name or address literal accepted (a DNS name is at most 253 characters).</summary>
    public const int HostMaxLength = 253;

    /// <summary>The lowest TCP port.</summary>
    public const int PortMinimum = 1;

    /// <summary>The highest TCP port.</summary>
    public const int PortMaximum = 65535;

    /// <summary>The longest login a Linux system accepts.</summary>
    public const int SshUserMaxLength = 32;

    /// <summary>The longest <c>allowedFrom</c> text accepted.</summary>
    public const int AllowedFromMaxLength = 400;

    /// <summary>The most addresses or ranges an <c>allowedFrom</c> list may name.</summary>
    public const int AllowedFromMaxEntries = 16;
}
