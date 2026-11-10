namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Where an export grant stands at a moment (issue #127).</summary>
public enum ExportGrantState
{
    /// <summary>Not revoked, not expired and its artifact not yet read: it still opens the package.</summary>
    Live = 1,

    /// <summary>The artifact was read; the grant opens nothing more.</summary>
    Used = 2,

    /// <summary>An administrator ended it before it was used or expired.</summary>
    Revoked = 3,

    /// <summary>Its time ran out without its artifact being read.</summary>
    Expired = 4,
}
