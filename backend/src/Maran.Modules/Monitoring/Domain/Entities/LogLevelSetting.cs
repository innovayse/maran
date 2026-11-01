using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The panel log level an administrator chose, kept so the choice survives a restart (issue #131).
/// </summary>
/// <remarks>
/// <para>
/// One row is the whole table: the key is always <see cref="SingletonId"/> and nothing generates
/// another, so two concurrent saves contend on one row instead of leaving two answers to "what level
/// did the administrator choose".
/// </para>
/// <para>
/// What this row holds is an INTENTION. What the logger is actually writing at is read from the
/// running switch, never from here — the two differ on a panel where nobody has chosen yet (the
/// configured level is in force and there is no row), and a screen that reported the row would agree
/// with the panel's own decision whatever the logger did.
/// </para>
/// </remarks>
public sealed class LogLevelSetting
{
    /// <summary>The fixed key of the only row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000106c");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The level the administrator chose.</summary>
    public PanelLogLevel Level { get; private set; }

    /// <summary>When the level was last chosen.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Records the first choice of level on this panel.</summary>
    /// <param name="level">The level chosen.</param>
    /// <param name="changedAt">When it was chosen.</param>
    public LogLevelSetting(PanelLogLevel level, DateTimeOffset changedAt)
    {
        Id = SingletonId;
        Level = level;
        ChangedAt = changedAt;
    }

    /// <summary>EF Core's materialisation constructor; never called by code.</summary>
    private LogLevelSetting()
    {
    }

    /// <summary>Records a new choice of level.</summary>
    /// <param name="level">The level chosen.</param>
    /// <param name="changedAt">When it was chosen.</param>
    public void Change(PanelLogLevel level, DateTimeOffset changedAt)
    {
        Level = level;
        ChangedAt = changedAt;
    }
}
