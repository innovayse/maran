namespace Maran.Host.Dashboard;

/// <summary>One step of the first-run checklist and whether it is done.</summary>
/// <param name="Key">Which step this is.</param>
/// <param name="Done">
/// Whether the administrator has done it, or unavailable when the module that knows could not be
/// asked. Never a plain "not done" for a source nobody read (issue #248).
/// </param>
/// <param name="RouteName">The screen where the step is done.</param>
public sealed record FirstRunStepDto(
    FirstRunStepKey Key,
    DashboardSection<bool> Done,
    FirstRunRoute RouteName);
