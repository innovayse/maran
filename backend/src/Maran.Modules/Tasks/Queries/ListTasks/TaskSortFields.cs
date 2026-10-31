namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// The public names the tasks list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListTasksSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class TaskSortFields
{
    /// <summary>When the operation started — the moment a task row is created.</summary>
    public const string StartedAt = "startedAt";

    /// <summary>What the operation acts on.</summary>
    public const string Subject = "subject";
}
