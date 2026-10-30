namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Lists the most recent background tasks the caller may see. Takes no parameters on purpose: the
/// scope is the caller's own — the whole server for an administrator, their own account for a
/// customer — and it is applied by the context's query filter, so there is nothing a caller could
/// point it at.
/// </summary>
public sealed record ListTasksQuery;
