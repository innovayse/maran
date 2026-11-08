using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.ValueObjects;

namespace Maran.Modules.Databases.Domain.Policies;

/// <summary>
/// The one place the panel decides which privileges each named preset fills in. The browser holds none
/// of them (rules/architecture.md "The backend owns the data, the SPA renders it"): it lists what this
/// catalog answers, lets the customer adjust the boxes, and sends the privileges, which the command's
/// validator and the agent's closed contract remain the authority on.
/// </summary>
/// <remarks>
/// A preset is a convenience and never a stored fact. A grant row holds the privileges; it does not
/// remember which preset produced them, so changing a preset here changes nothing already granted.
/// </remarks>
public static class DatabasePrivilegePresetCatalog
{
    /// <summary>Every preset the screen offers, in the order it lists them.</summary>
    public static readonly IReadOnlyList<DatabasePrivilegePreset> All =
    [
        new DatabasePrivilegePreset(
            DatabasePrivilegePresetKey.ReadOnly,
            [DatabasePrivilege.Select, DatabasePrivilege.ShowView]),
        new DatabasePrivilegePreset(
            DatabasePrivilegePresetKey.ReadWrite,
            [
                DatabasePrivilege.Select,
                DatabasePrivilege.Insert,
                DatabasePrivilege.Update,
                DatabasePrivilege.Delete,
                DatabasePrivilege.LockTables,
                DatabasePrivilege.ShowView,
                DatabasePrivilege.Execute,
            ]),
        new DatabasePrivilegePreset(DatabasePrivilegePresetKey.FullAccess, DatabasePrivilegeSets.All),
    ];
}
