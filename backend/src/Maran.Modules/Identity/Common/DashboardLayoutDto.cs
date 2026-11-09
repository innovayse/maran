namespace Maran.Modules.Identity.Common;

/// <summary>
/// The dashboard layout of the signed-in person: the panel's defaults for their audience with their
/// own choices applied. Both lists are in the order they are drawn, and the same shape is what a
/// save sends.
/// </summary>
/// <param name="Widgets">The widgets of the dashboard.</param>
/// <param name="Actions">The shortcuts of the quick-actions bar.</param>
public sealed record DashboardLayoutDto(
    IReadOnlyList<DashboardLayoutEntryDto> Widgets,
    IReadOnlyList<DashboardLayoutEntryDto> Actions);
