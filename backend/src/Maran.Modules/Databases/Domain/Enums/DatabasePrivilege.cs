namespace Maran.Modules.Databases.Domain.Enums;

/// <summary>
/// One privilege a database user may hold on one of the account's own databases. The closed list of
/// eighteen the panel will ever grant, in the order the screen shows them.
/// </summary>
/// <remarks>
/// <para>
/// GRANT OPTION, FILE, PROCESS, SUPER, RELOAD, SHUTDOWN, REPLICATION and every global privilege are not
/// members, and that is the whole of how they are refused: a request cannot name what the type does not
/// have, so no validator has to remember to reject them. The agent's contract is closed the same way
/// (<c>DatabasePrivilege</c> in <c>db.proto</c>), so a value added here without being added there fails
/// loudly the first time it is sent and is never silently dropped.
/// </para>
/// <para>
/// The numeric value is the privilege's bit position in <see cref="DatabasePrivilegeSet"/>, which is
/// what the database stores. It is a position and not an identifier: a new privilege takes the next
/// number and an existing one is never renumbered, because a renumbering would silently change what
/// every stored grant means.
/// </para>
/// </remarks>
public enum DatabasePrivilege
{
    /// <summary>SELECT.</summary>
    Select = 0,

    /// <summary>INSERT.</summary>
    Insert = 1,

    /// <summary>UPDATE.</summary>
    Update = 2,

    /// <summary>DELETE.</summary>
    Delete = 3,

    /// <summary>CREATE.</summary>
    Create = 4,

    /// <summary>DROP.</summary>
    Drop = 5,

    /// <summary>ALTER.</summary>
    Alter = 6,

    /// <summary>INDEX.</summary>
    Index = 7,

    /// <summary>REFERENCES.</summary>
    References = 8,

    /// <summary>CREATE TEMPORARY TABLES.</summary>
    CreateTemporaryTables = 9,

    /// <summary>LOCK TABLES.</summary>
    LockTables = 10,

    /// <summary>CREATE VIEW.</summary>
    CreateView = 11,

    /// <summary>SHOW VIEW.</summary>
    ShowView = 12,

    /// <summary>CREATE ROUTINE.</summary>
    CreateRoutine = 13,

    /// <summary>ALTER ROUTINE.</summary>
    AlterRoutine = 14,

    /// <summary>EXECUTE.</summary>
    Execute = 15,

    /// <summary>TRIGGER.</summary>
    Trigger = 16,

    /// <summary>EVENT.</summary>
    Event = 17,
}
