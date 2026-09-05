namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Lists the panel's most recent background tasks. Takes no parameters on purpose: the whole
/// surface is administrator-only in v1 and an administrator sees the whole server, so there is
/// nothing to scope it by and nothing a caller could point it at.
/// </summary>
public sealed record ListTasksQuery;
