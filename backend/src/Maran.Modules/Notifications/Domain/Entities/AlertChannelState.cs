using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Domain.Entities;

/// <summary>
/// What the last operator alerts did on one channel: when one was last tried, when one last arrived
/// and when one last failed, with the code of that failure. The dashboard reads it so a broken alert
/// path is visible instead of silent.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row per channel, ever.</b> The key is the channel, so a second row for the same channel
/// cannot exist. The row holds no address, no URL and no message text: nothing personal is stored,
/// and a failure's sentence is resolved from <see cref="LastFailureCode"/> when it is read, in the
/// reader's language (rules/csharp.md "A stored failure is resolved when it is READ").
/// </para>
/// <para>
/// <b>"Failing" is a comparison, not a flag.</b> A channel fails while its last failure is newer than
/// its last success, or it has failed and never succeeded; the first success after that clears it
/// without anybody having to reset anything. A flag would need a writer to remember to lower it.
/// </para>
/// <para>
/// A skipped send (outgoing mail is not configured) is not recorded here at all: nothing was
/// attempted, and "not configured" is a state the settings page already shows, not a failure
/// (the rule <c>MailSkippedNoSmtp</c> follows).
/// </para>
/// </remarks>
public sealed class AlertChannelState
{
    /// <summary>The width of <see cref="LastFailureCode"/>: an error code, never a sentence.</summary>
    public const int FailureCodeMaxLength = 100;

    /// <summary>The channel this row describes, and the row's key.</summary>
    public AlertChannel Channel { get; private set; }

    /// <summary>When an alert was last sent on this channel, successfully or not.</summary>
    public DateTimeOffset LastAttemptAt { get; private set; }

    /// <summary>When an alert last arrived, or <c>null</c> when none has.</summary>
    public DateTimeOffset? LastSucceededAt { get; private set; }

    /// <summary>When an alert last failed, or <c>null</c> when none has.</summary>
    public DateTimeOffset? LastFailedAt { get; private set; }

    /// <summary>The error code of the last failure, or <c>null</c> when none has failed.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>Whether the channel's most recent outcome is a failure.</summary>
    public bool IsFailing
    {
        get
        {
            return LastFailedAt is not null && (LastSucceededAt is null || LastFailedAt > LastSucceededAt);
        }
    }

    /// <summary>Creates the row for a channel on its first attempt.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="attemptedAt">When the first attempt was made.</param>
    public AlertChannelState(AlertChannel channel, DateTimeOffset attemptedAt)
    {
        Channel = channel;
        LastAttemptAt = attemptedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AlertChannelState()
    {
    }

    /// <summary>Records that an alert arrived.</summary>
    /// <param name="at">When, from the panel's clock.</param>
    public void RecordSuccess(DateTimeOffset at)
    {
        LastAttemptAt = at;
        LastSucceededAt = at;
    }

    /// <summary>Records that an alert did not arrive.</summary>
    /// <param name="at">When, from the panel's clock.</param>
    /// <param name="failureCode">The error code of the failure; cut to what the column holds.</param>
    public void RecordFailure(DateTimeOffset at, string failureCode)
    {
        LastAttemptAt = at;
        LastFailedAt = at;
        LastFailureCode = failureCode.Length > FailureCodeMaxLength ? failureCode[..FailureCodeMaxLength] : failureCode;
    }
}
