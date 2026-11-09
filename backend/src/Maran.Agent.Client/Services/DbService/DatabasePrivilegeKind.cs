namespace Maran.Agent.Client.Services.DbService;

/// <summary>
/// A privilege a database user may hold on one of an account's own databases, as the agent's contract
/// names it, restated without the wire's zero value.
/// </summary>
/// <remarks>
/// The set is closed on purpose and mirrors <c>DatabasePrivilege</c> in <c>db.proto</c>: GRANT OPTION,
/// FILE, PROCESS, SUPER, RELOAD, SHUTDOWN, REPLICATION and every global privilege have no member, so no
/// caller of <see cref="Interfaces.IAgentDbClient"/> can ask for one. A privilege the wire cannot name is
/// not a privilege this panel can grant.
/// </remarks>
public enum DatabasePrivilegeKind
{
    /// <summary>SELECT.</summary>
    Select,

    /// <summary>INSERT.</summary>
    Insert,

    /// <summary>UPDATE.</summary>
    Update,

    /// <summary>DELETE.</summary>
    Delete,

    /// <summary>CREATE.</summary>
    Create,

    /// <summary>DROP.</summary>
    Drop,

    /// <summary>ALTER.</summary>
    Alter,

    /// <summary>INDEX.</summary>
    Index,

    /// <summary>REFERENCES.</summary>
    References,

    /// <summary>CREATE TEMPORARY TABLES.</summary>
    CreateTemporaryTables,

    /// <summary>LOCK TABLES.</summary>
    LockTables,

    /// <summary>CREATE VIEW.</summary>
    CreateView,

    /// <summary>SHOW VIEW.</summary>
    ShowView,

    /// <summary>CREATE ROUTINE.</summary>
    CreateRoutine,

    /// <summary>ALTER ROUTINE.</summary>
    AlterRoutine,

    /// <summary>EXECUTE.</summary>
    Execute,

    /// <summary>TRIGGER.</summary>
    Trigger,

    /// <summary>EVENT.</summary>
    Event,
}
