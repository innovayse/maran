namespace Maran.Modules.Databases.Domain.Enums;

/// <summary>
/// A set of <see cref="DatabasePrivilege"/> values, stored as one integer: the form a grant row holds.
/// </summary>
/// <remarks>
/// Bit <c>n</c> is set when the privilege numbered <c>n</c> in <see cref="DatabasePrivilege"/> is held.
/// Eighteen bits, so the value always fits an <c>integer</c> column and a stored value with any bit above
/// the eighteenth set is not one this panel wrote. Building and reading a set goes through
/// <c>DatabasePrivilegeSets</c>; nothing else does arithmetic on the bits.
/// </remarks>
[Flags]
public enum DatabasePrivilegeSet
{
    /// <summary>No privilege. A grant row never holds this: holding nothing is having no row.</summary>
    None = 0,

    /// <summary>SELECT.</summary>
    Select = 1 << 0,

    /// <summary>INSERT.</summary>
    Insert = 1 << 1,

    /// <summary>UPDATE.</summary>
    Update = 1 << 2,

    /// <summary>DELETE.</summary>
    Delete = 1 << 3,

    /// <summary>CREATE.</summary>
    Create = 1 << 4,

    /// <summary>DROP.</summary>
    Drop = 1 << 5,

    /// <summary>ALTER.</summary>
    Alter = 1 << 6,

    /// <summary>INDEX.</summary>
    Index = 1 << 7,

    /// <summary>REFERENCES.</summary>
    References = 1 << 8,

    /// <summary>CREATE TEMPORARY TABLES.</summary>
    CreateTemporaryTables = 1 << 9,

    /// <summary>LOCK TABLES.</summary>
    LockTables = 1 << 10,

    /// <summary>CREATE VIEW.</summary>
    CreateView = 1 << 11,

    /// <summary>SHOW VIEW.</summary>
    ShowView = 1 << 12,

    /// <summary>CREATE ROUTINE.</summary>
    CreateRoutine = 1 << 13,

    /// <summary>ALTER ROUTINE.</summary>
    AlterRoutine = 1 << 14,

    /// <summary>EXECUTE.</summary>
    Execute = 1 << 15,

    /// <summary>TRIGGER.</summary>
    Trigger = 1 << 16,

    /// <summary>EVENT.</summary>
    Event = 1 << 17,
}
