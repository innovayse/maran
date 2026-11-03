using Maran.Modules.Tasks.Domain.Enums;

namespace Maran.Modules.Tasks.Common;

/// <summary>One status the tasks list can be filtered by, as the filter's choice list offers it.</summary>
/// <param name="Status">
/// The status itself: the enum, so the panel's one camelCase enum converter spells it exactly as the
/// list's <c>status</c> filter binds it, and the filter sends back what it was given.
/// </param>
/// <param name="StatusName">The status as an operator reads it, resolved for the request's culture.</param>
public sealed record TaskStatusChoiceDto(PanelTaskStatus Status, string StatusName);
