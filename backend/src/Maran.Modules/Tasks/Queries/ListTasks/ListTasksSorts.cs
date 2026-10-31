using Maran.Modules.Tasks.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// The orders the tasks list accepts. <see cref="ListTasksQueryValidator"/> refuses any other sort and
/// <see cref="ListTasksQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListTasksSorts
{
    /// <summary>
    /// <see cref="TaskSortFields.StartedAt"/> and <see cref="TaskSortFields.Subject"/>, newest first
    /// when none is named — the order the list had before it was paged — with ties broken by the task
    /// id.
    /// </summary>
    /// <remarks>
    /// A task has no creation instant apart from <see cref="PanelTask.StartedAt"/>: the row is opened
    /// when the operation starts, so the start IS when the task was created.
    /// </remarks>
    public static readonly SortMap<PanelTask> Map = new SortMapBuilder<PanelTask>()
        .By(TaskSortFields.StartedAt, task => task.StartedAt)
        .By(TaskSortFields.Subject, task => task.Subject)
        .DefaultTo(TaskSortFields.StartedAt, SortDirection.Descending)
        .TieBreaker(task => task.Id);
}
