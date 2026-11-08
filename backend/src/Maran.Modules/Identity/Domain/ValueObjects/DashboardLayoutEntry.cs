namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// One entry of an effective dashboard list: the key of a widget or shortcut and whether the person
/// turned it off. A hidden entry stays in the list so it keeps its place when it is turned back on.
/// </summary>
/// <param name="Key">The widget or shortcut, by the key the SPA maps to what it draws.</param>
/// <param name="Hidden">True when the person turned the entry off.</param>
public sealed record DashboardLayoutEntry(string Key, bool Hidden);
