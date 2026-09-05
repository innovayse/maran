namespace Maran.Modules.Tasks.Domain.Enums;

/// <summary>Where a panel task has got to. Three values, and two of them are final.</summary>
public enum PanelTaskStatus
{
    /// <summary>The operation is still going. The only state a task can leave.</summary>
    Running,

    /// <summary>The operation finished its work. Final.</summary>
    Completed,

    /// <summary>The operation did not finish its work, and the row carries the code it answered with. Final.</summary>
    Failed,
}
