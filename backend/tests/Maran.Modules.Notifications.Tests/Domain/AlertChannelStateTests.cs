using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Tests.Domain;

/// <summary>When a channel counts as failing.</summary>
public sealed class AlertChannelStateTests
{
    /// <summary>The instant the fixtures start from.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A channel that failed and never succeeded is failing.</summary>
    [Fact]
    public void A_channel_that_only_ever_failed_is_failing()
    {
        var state = new AlertChannelState(AlertChannel.Mail, Start);
        state.RecordFailure(Start, "MailDeliveryFailed");

        Assert.True(state.IsFailing);
    }

    /// <summary>A later success ends the failing state; a later failure starts it again.</summary>
    [Fact]
    public void The_latest_outcome_decides_whether_the_channel_is_failing()
    {
        var state = new AlertChannelState(AlertChannel.Mail, Start);
        state.RecordFailure(Start, "MailDeliveryFailed");

        state.RecordSuccess(Start.AddMinutes(1));
        Assert.False(state.IsFailing);

        state.RecordFailure(Start.AddMinutes(2), "MailServerUnreachable");
        Assert.True(state.IsFailing);
        Assert.Equal("MailServerUnreachable", state.LastFailureCode);
    }

    /// <summary>A failure code longer than the column is cut, not rejected.</summary>
    [Fact]
    public void A_failure_code_longer_than_the_column_is_cut()
    {
        var state = new AlertChannelState(AlertChannel.Mail, Start);

        state.RecordFailure(Start, new string('x', 500));

        Assert.Equal(AlertChannelState.FailureCodeMaxLength, state.LastFailureCode!.Length);
    }
}
