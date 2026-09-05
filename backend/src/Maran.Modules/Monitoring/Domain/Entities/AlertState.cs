using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Domain.Entities;

/// <summary>
/// What the panel currently believes about one monitored condition — this filesystem, that service
/// — and the counter that decides when believing it changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This row is the deduplication, and nothing else is.</b> The disk threshold is true on every
/// sample for as long as the disk is full, so an alert driven by the threshold alone sends a mail a
/// minute until somebody deletes something — which is how an operator learns to filter the panel's
/// mail, and therefore how the next alert is missed. <see cref="Observe"/> returns
/// <see cref="AlertTransition.Raised"/> exactly once per episode: on the observation that crosses
/// <see cref="BreachesBeforeAlert"/>, and never again until the condition has cleared.
/// </para>
/// <para>
/// <b>The counter is CONSECUTIVE, and a healthy observation resets it to zero.</b> Ten breaches
/// spread across an afternoon are ten transient spikes, not an outage, and a counter that only ever
/// climbed would eventually alert on every host that had ever been briefly busy. Ten in a row at the
/// sampler's cadence is ten minutes of a condition that has not gone away on its own.
/// </para>
/// <para>
/// <b>A row exists per (kind, subject) pair and survives the episode.</b> It is not deleted when the
/// alert resolves: the row IS the memory that says the condition is currently healthy, and deleting
/// it would make the next breach look like the first ten all over again — or, worse, make a resolve
/// mail arrive with no raise behind it.
/// </para>
/// </remarks>
public sealed class AlertState
{
    /// <summary>
    /// How many consecutive breaching observations are needed before the condition is called an
    /// alarm.
    /// </summary>
    /// <remarks>
    /// Ten, at the sampler's roughly-sixty-second cadence, so a condition has to persist for about
    /// ten minutes. It is a constant on the entity rather than a setting because it is the entity's
    /// own rule: a caller free to pass its own threshold could send a mail on the first sample,
    /// which is the behaviour this class exists to prevent.
    /// </remarks>
    public const int BreachesBeforeAlert = 10;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Which kind of condition this row watches.</summary>
    public AlertKind Kind { get; private set; }

    /// <summary>
    /// Which thing of that kind: the filesystem's mount point, or the managed service's name.
    /// Together with <see cref="Kind"/> it is the row's real identity, and the pair is unique.
    /// </summary>
    public string Subject { get; private set; }

    /// <summary>How many observations in a row have found the condition breaching.</summary>
    public int ConsecutiveBreaches { get; private set; }

    /// <summary>Whether the panel currently considers this condition to be in alarm.</summary>
    public bool IsFiring { get; private set; }

    /// <summary>When the current episode was raised, or <c>null</c> when the condition is healthy.</summary>
    /// <remarks>
    /// Cleared on resolve rather than kept as "when it last fired", so that its presence and
    /// <see cref="IsFiring"/> can never disagree about whether an episode is open.
    /// </remarks>
    public DateTimeOffset? RaisedAt { get; private set; }

    /// <summary>When the row was last written, whether or not the observation changed anything.</summary>
    public DateTimeOffset LastObservedAt { get; private set; }

    /// <summary>Starts watching a condition, in the healthy state.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="kind">Which kind of condition this row watches.</param>
    /// <param name="subject">Which thing of that kind — a mount point, a service name.</param>
    /// <param name="observedAt">When the row was created, from the panel's clock.</param>
    /// <remarks>
    /// Healthy, always, whatever the observation that caused the row to be created found. A row born
    /// firing would send a resolve mail for an episode nobody was told about; the first observation
    /// after construction starts the counter at one, which is where an episode has to start.
    /// </remarks>
    public AlertState(Guid id, AlertKind kind, string subject, DateTimeOffset observedAt)
    {
        Id = id;
        Kind = kind;
        Subject = subject;
        ConsecutiveBreaches = 0;
        IsFiring = false;
        RaisedAt = null;
        LastObservedAt = observedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AlertState()
    {
        Subject = string.Empty;
    }

    /// <summary>Records one observation and reports what it changed.</summary>
    /// <param name="breaching">Whether this observation found the condition unhealthy.</param>
    /// <param name="observedAt">When the observation was made, from the panel's clock.</param>
    /// <returns>
    /// <see cref="AlertTransition.Raised"/> on the observation that opens an episode,
    /// <see cref="AlertTransition.Resolved"/> on the one that closes it, and
    /// <see cref="AlertTransition.None"/> on every other — which is almost all of them.
    /// </returns>
    /// <remarks>
    /// The caller sends a mail when, and only when, this returns something other than
    /// <see cref="AlertTransition.None"/>. That is the whole contract: the caller holds no counter,
    /// compares no previous value, and cannot accidentally send twice.
    /// </remarks>
    public AlertTransition Observe(bool breaching, DateTimeOffset observedAt)
    {
        LastObservedAt = observedAt;

        if (!breaching)
        {
            ConsecutiveBreaches = 0;

            if (!IsFiring)
            {
                return AlertTransition.None;
            }

            IsFiring = false;
            RaisedAt = null;
            return AlertTransition.Resolved;
        }

        // Saturating rather than unbounded: a condition left unfixed for a year would otherwise
        // count half a million observations toward a threshold it crossed on the tenth, and the
        // only thing the extra counting could ever do is overflow.
        if (ConsecutiveBreaches < BreachesBeforeAlert)
        {
            ConsecutiveBreaches++;
        }

        if (IsFiring || ConsecutiveBreaches < BreachesBeforeAlert)
        {
            return AlertTransition.None;
        }

        IsFiring = true;
        RaisedAt = observedAt;
        return AlertTransition.Raised;
    }
}
