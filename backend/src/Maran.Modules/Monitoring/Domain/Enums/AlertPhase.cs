namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>Where one monitored condition stands: the single persisted state of an <see cref="Entities.AlertState"/>.</summary>
/// <remarks>
/// A condition is in exactly one of three phases, and the phase is stored, not re-derived from a
/// counter and a flag that could disagree. <see cref="Healthy"/> is a real state — the last
/// observation found nothing wrong — not an absent value.
/// </remarks>
public enum AlertPhase
{
    /// <summary>The latest observation found the condition healthy; the breach counter is zero.</summary>
    Healthy = 0,

    /// <summary>
    /// The latest observation breached, but not yet enough times in a row to raise an alert. Nobody has
    /// been told anything.
    /// </summary>
    Pending = 1,

    /// <summary>An episode is open: the alert was raised and has not resolved.</summary>
    Firing = 2,
}
