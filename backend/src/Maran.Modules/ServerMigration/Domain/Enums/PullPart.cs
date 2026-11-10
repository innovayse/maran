namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Which part of an export the receiving panel asks the exporting panel for (issue #127).</summary>
public enum PullPart
{
    /// <summary>The signed header and the agent's signed manifest: a preview that can be read a few times.</summary>
    Head = 1,

    /// <summary>The whole package, which the grant allows once.</summary>
    Package = 2,
}
