namespace Maran.Modules.Identity.Common;

/// <summary>One widget or shortcut of the dashboard layout.</summary>
/// <param name="Key">The widget or shortcut, by the key the SPA maps to what it draws.</param>
/// <param name="Hidden">True when the person turned the entry off; it keeps its place in the list.</param>
public sealed record DashboardLayoutEntryDto(string Key, bool Hidden);
