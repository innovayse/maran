namespace Maran.Modules.Tasks.Queries.GetTask;

/// <summary>Reads one panel task. A task the caller may not see answers 404, never 403.</summary>
/// <param name="TaskId">The task to read.</param>
public sealed record GetTaskQuery(Guid TaskId);
