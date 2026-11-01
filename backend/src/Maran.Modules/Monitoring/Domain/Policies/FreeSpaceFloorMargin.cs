using Maran.Modules.Monitoring.Models;

namespace Maran.Modules.Monitoring.Domain.Policies;

/// <summary>
/// Decides whether the root filesystem is close enough to the agent's free-space floor that an
/// operator must be told before the agent starts refusing work (issue #148).
/// </summary>
/// <remarks>
/// <para>
/// Written forward from the question: <i>when must the warning fire so that it arrives before the
/// first refusal?</i> The agent refuses once less than the floor is available, and the alert raises
/// only after <see cref="Services.AlertEvaluator"/> has seen the condition on consecutive checks. So
/// the warning cannot fire AT the floor — it would always arrive after the refusals it is meant to
/// announce. It fires while less than <see cref="Multiple"/> times the floor is available, which
/// leaves one whole floor's worth of room between the warning and the refusal.
/// </para>
/// <para>
/// That is a margin, not a promise: a disk that loses a floor's worth of space within the checks the
/// alert waits for still reaches the floor first. And it speaks for the ROOT filesystem only, which
/// is the one the agent's host metrics measure; the agent's refusal follows whichever filesystem a
/// write lands on (docs/monitoring/disk-figures.md).
/// </para>
/// </remarks>
public static class FreeSpaceFloorMargin
{
    /// <summary>How many floors' worth of room must be left for the root filesystem to be clear.</summary>
    public const ulong Multiple = 2;

    /// <summary>Whether <paramref name="reading"/> is within the warning margin of the floor.</summary>
    /// <param name="reading">This round's available space and the agent's floor.</param>
    /// <returns><c>true</c> while less than <see cref="Multiple"/> floors are available.</returns>
    /// <remarks>
    /// Compared as available divided by the multiple, so a floor near <see cref="ulong.MaxValue"/>
    /// cannot overflow a multiplication into a small number that would silence the alert. For whole
    /// numbers <c>available / 2 &lt; floor</c> holds exactly when <c>available &lt; 2 × floor</c>.
    /// </remarks>
    public static bool IsApproaching(FreeSpaceFloorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.AvailableBytes / Multiple < reading.FloorBytes;
    }
}
