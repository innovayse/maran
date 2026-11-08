namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "does this apex collide with a zone another account owns".</summary>
public enum ZoneOverlapVerdict
{
    /// <summary>No zone of another account is this name, above it or below it.</summary>
    Clear,

    /// <summary>Another account owns this very name.</summary>
    SameAsForeignZone,

    /// <summary>The name is a parent of a zone another account owns, so it would answer for that subtree.</summary>
    ParentOfForeignZone,

    /// <summary>The name is a child of a zone another account owns, which would answer for it instead.</summary>
    ChildOfForeignZone,
}
