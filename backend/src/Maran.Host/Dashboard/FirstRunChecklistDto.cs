namespace Maran.Host.Dashboard;

/// <summary>The first-run checklist: what a new administrator still has to do, derived from the data.</summary>
/// <remarks>
/// <para>
/// <b>Derived, never stored.</b> Each step is true when the panel holds the thing it asks for, so
/// the card cannot be dismissed into a state the data contradicts and nothing can go stale. The
/// price is that there is no "skip this step" in v1.
/// </para>
/// <para>
/// A step is done only when the panel holds something the administrator made: the standard plans and
/// the local backup destination a fresh installation ships with do not count.
/// </para>
/// </remarks>
/// <param name="Steps">Every step, in the order they are shown.</param>
/// <param name="Complete">
/// Whether every step was read and is done. A step that could not be read keeps this false: the
/// panel does not announce a finished setup it could not check.
/// </param>
public sealed record FirstRunChecklistDto(
    IReadOnlyList<FirstRunStepDto> Steps,
    bool Complete);
