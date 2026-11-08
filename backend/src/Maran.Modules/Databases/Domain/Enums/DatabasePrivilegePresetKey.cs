namespace Maran.Modules.Databases.Domain.Enums;

/// <summary>The named privilege sets the users screen offers as quick fills.</summary>
public enum DatabasePrivilegePresetKey
{
    /// <summary>Reading data and nothing that changes it.</summary>
    ReadOnly,

    /// <summary>Reading and changing the rows, without changing the structure.</summary>
    ReadWrite,

    /// <summary>Every privilege the panel can grant on a database.</summary>
    FullAccess,
}
