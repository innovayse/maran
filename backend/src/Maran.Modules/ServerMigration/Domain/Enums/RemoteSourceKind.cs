namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>What kind of system a successful test found on a remote server.</summary>
public enum RemoteSourceKind
{
    /// <summary>An ordinary Linux server, read through its system files and commands.</summary>
    GenericLinux,

    /// <summary>A server running this panel.</summary>
    Maran,
}
