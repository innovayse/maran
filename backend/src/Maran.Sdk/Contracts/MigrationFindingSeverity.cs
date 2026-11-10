namespace Maran.Sdk.Contracts;

/// <summary>How much a finding of the dry run matters to an import.</summary>
public enum MigrationFindingSeverity
{
    /// <summary>Something the administrator is told and need not act on.</summary>
    Info = 0,

    /// <summary>Something that will be imported differently from how it was, or not at all, and does not stop the import.</summary>
    Warning = 1,

    /// <summary>Something that would make the import fail or collide; the import refuses to start while any exists.</summary>
    Conflict = 2,
}
