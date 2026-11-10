namespace Maran.Modules.Apps.Common;

/// <summary>What an install or an uninstall answers (202): the task that carries the operation's progress.</summary>
/// <param name="TaskId">The task whose progress, log and result appear in the task list.</param>
public sealed record AppOperationAcceptedDto(Guid TaskId);
