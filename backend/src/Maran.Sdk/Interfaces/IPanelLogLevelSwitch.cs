using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The running logger's minimum level, read and changed without a restart (issue #131).
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the Host, which owns the logger, and driven by the Monitoring module, which owns
/// the stored choice, its audit entry and its endpoint. A module may not reference the Host, so the
/// seam lives here; it exposes the narrowest thing that answers the question — one level — and
/// nothing about the logging library behind it.
/// </para>
/// <para>
/// The switch is process-local and holds no history: what an administrator chose survives a restart
/// only because the module stores it and applies it again at start-up.
/// </para>
/// </remarks>
public interface IPanelLogLevelSwitch
{
    /// <summary>The level the logger is writing at right now — an observation, not the stored choice.</summary>
    PanelLogLevel Current { get; }

    /// <summary>Changes the level the logger writes at, effective for the next line logged.</summary>
    /// <param name="level">The new minimum level.</param>
    void Apply(PanelLogLevel level);
}
