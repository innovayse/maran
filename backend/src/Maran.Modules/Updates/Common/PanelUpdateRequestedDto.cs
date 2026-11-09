namespace Maran.Modules.Updates.Common;

/// <summary>What <c>POST /api/v1/panel-update</c> answers (202): the task that carries the update's progress.</summary>
/// <param name="TaskId">The task whose progress and result appear in the task list.</param>
public sealed record PanelUpdateRequestedDto(Guid TaskId);
