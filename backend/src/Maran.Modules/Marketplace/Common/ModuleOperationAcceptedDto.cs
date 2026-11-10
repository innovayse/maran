namespace Maran.Modules.Marketplace.Common;

/// <summary>What install, update, remove and rollback answer (202): the task that carries the operation's progress.</summary>
/// <param name="TaskId">The task whose progress and result appear in the task list.</param>
public sealed record ModuleOperationAcceptedDto(Guid TaskId);
