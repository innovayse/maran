using Maran.Agent.Client.Services.DbService;
using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Mappers;

/// <summary>Restates the panel's <see cref="DatabasePrivilege"/> as the agent client's <see cref="DatabasePrivilegeKind"/>.</summary>
/// <remarks>
/// Every member is named with no default arm, so a privilege added on one side and not the other is an
/// exception the first time it is granted and never a privilege silently left out of a request.
/// </remarks>
public static class DatabasePrivilegeKindMapper
{
    /// <summary>Maps one privilege.</summary>
    /// <param name="privilege">The privilege to send to the agent.</param>
    /// <returns>The agent client's name for it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="privilege"/> is not a defined member.</exception>
    public static DatabasePrivilegeKind ToKind(DatabasePrivilege privilege)
    {
        return privilege switch
        {
            DatabasePrivilege.Select => DatabasePrivilegeKind.Select,
            DatabasePrivilege.Insert => DatabasePrivilegeKind.Insert,
            DatabasePrivilege.Update => DatabasePrivilegeKind.Update,
            DatabasePrivilege.Delete => DatabasePrivilegeKind.Delete,
            DatabasePrivilege.Create => DatabasePrivilegeKind.Create,
            DatabasePrivilege.Drop => DatabasePrivilegeKind.Drop,
            DatabasePrivilege.Alter => DatabasePrivilegeKind.Alter,
            DatabasePrivilege.Index => DatabasePrivilegeKind.Index,
            DatabasePrivilege.References => DatabasePrivilegeKind.References,
            DatabasePrivilege.CreateTemporaryTables => DatabasePrivilegeKind.CreateTemporaryTables,
            DatabasePrivilege.LockTables => DatabasePrivilegeKind.LockTables,
            DatabasePrivilege.CreateView => DatabasePrivilegeKind.CreateView,
            DatabasePrivilege.ShowView => DatabasePrivilegeKind.ShowView,
            DatabasePrivilege.CreateRoutine => DatabasePrivilegeKind.CreateRoutine,
            DatabasePrivilege.AlterRoutine => DatabasePrivilegeKind.AlterRoutine,
            DatabasePrivilege.Execute => DatabasePrivilegeKind.Execute,
            DatabasePrivilege.Trigger => DatabasePrivilegeKind.Trigger,
            DatabasePrivilege.Event => DatabasePrivilegeKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(privilege), privilege, "Not a privilege this panel can grant."),
        };
    }
}
