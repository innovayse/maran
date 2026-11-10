namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>How much of an export's package a write produces (issue #127).</summary>
public enum PackageExtent
{
    /// <summary>
    /// Everything before the first artifact byte: the signed header and the agent's signed manifest. What the
    /// importing administrator previews, and nothing a stolen grant could use to read the customer's data.
    /// </summary>
    HeadOnly = 1,

    /// <summary>The head and the whole artifact, streamed.</summary>
    Whole = 2,
}
