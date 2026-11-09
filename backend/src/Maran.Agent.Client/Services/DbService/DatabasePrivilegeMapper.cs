using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.DbService;

/// <summary>Restates the panel's <see cref="DatabasePrivilegeKind"/> as the wire's <c>DatabasePrivilege</c>.</summary>
/// <remarks>
/// Beside the client for the reason every wire mapper is: the generated types may not leave this
/// project. Every member is named, with no fall-through to a default, so a member added to one enum and
/// not the other is an exception the first time it is sent and not a privilege silently dropped from a
/// request.
/// </remarks>
internal static class DatabasePrivilegeMapper
{
    /// <summary>Maps one privilege onto its wire value.</summary>
    /// <param name="kind">The privilege to send.</param>
    /// <returns>The wire value, never the unspecified zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not a defined member, which is a programming error rather than a
    /// customer's input: the panel builds the value from its own closed set.
    /// </exception>
    public static DatabasePrivilege ToWire(DatabasePrivilegeKind kind)
    {
        return kind switch
        {
            DatabasePrivilegeKind.Select => DatabasePrivilege.Select,
            DatabasePrivilegeKind.Insert => DatabasePrivilege.Insert,
            DatabasePrivilegeKind.Update => DatabasePrivilege.Update,
            DatabasePrivilegeKind.Delete => DatabasePrivilege.Delete,
            DatabasePrivilegeKind.Create => DatabasePrivilege.Create,
            DatabasePrivilegeKind.Drop => DatabasePrivilege.Drop,
            DatabasePrivilegeKind.Alter => DatabasePrivilege.Alter,
            DatabasePrivilegeKind.Index => DatabasePrivilege.Index,
            DatabasePrivilegeKind.References => DatabasePrivilege.References,
            DatabasePrivilegeKind.CreateTemporaryTables => DatabasePrivilege.CreateTemporaryTables,
            DatabasePrivilegeKind.LockTables => DatabasePrivilege.LockTables,
            DatabasePrivilegeKind.CreateView => DatabasePrivilege.CreateView,
            DatabasePrivilegeKind.ShowView => DatabasePrivilege.ShowView,
            DatabasePrivilegeKind.CreateRoutine => DatabasePrivilege.CreateRoutine,
            DatabasePrivilegeKind.AlterRoutine => DatabasePrivilege.AlterRoutine,
            DatabasePrivilegeKind.Execute => DatabasePrivilege.Execute,
            DatabasePrivilegeKind.Trigger => DatabasePrivilege.Trigger,
            DatabasePrivilegeKind.Event => DatabasePrivilege.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a database privilege this panel can grant."),
        };
    }
}
