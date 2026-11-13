namespace Maran.Sdk.Contracts;

/// <summary>
/// Where a panel task stands, as a module that holds a task's id asks about it
/// (<see cref="Interfaces.ITaskStateReader"/>). Deliberately narrower than the task itself: the one fact
/// a caller needs is whether the operation behind the id can still be running.
/// </summary>
public enum TaskState
{
    /// <summary>No task has this id: it was never recorded, or its row has been pruned. Nothing can be running behind it.</summary>
    Unknown = 0,

    /// <summary>The operation is still going.</summary>
    Running = 1,

    /// <summary>The operation finished its work.</summary>
    Completed = 2,

    /// <summary>The operation did not finish its work.</summary>
    Failed = 3,
}
