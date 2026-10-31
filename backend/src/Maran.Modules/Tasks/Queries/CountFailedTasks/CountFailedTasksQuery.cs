namespace Maran.Modules.Tasks.Queries.CountFailedTasks;

/// <summary>
/// Counts the failed tasks the caller may see, for a screen that shows how many need attention rather
/// than which. A count is asked as a count: the task list is paged, so a page of it holds one page and
/// not the total.
/// </summary>
public sealed record CountFailedTasksQuery;
