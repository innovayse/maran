namespace Maran.Modules.Tasks.Queries.ListRunningTasks;

/// <summary>
/// Lists every task still running that the caller may see, newest first, as one read for API
/// consumers. The shell's badge does not read it: it opens the multiplexed running-tasks stream
/// instead (<c>RunningTasksStreamService</c>, issue #274). Takes no parameters: the scope is the
/// caller's own, applied by the context's query filter.
/// </summary>
/// <remarks>
/// Not paged, and not capped, because the set is transient by nature: a task leaves it the moment it
/// completes or fails, and the start-up reconciler closes any a restart abandoned. A badge built from
/// one page of the history would count the running tasks that happened to be on that page — the
/// failure a count read off a paged list always has.
/// </remarks>
public sealed record ListRunningTasksQuery;
