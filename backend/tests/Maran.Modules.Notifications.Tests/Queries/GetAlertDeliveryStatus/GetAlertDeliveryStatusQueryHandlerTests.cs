using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Queries.GetAlertDeliveryStatus;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Queries.GetAlertDeliveryStatus;

/// <summary>Whether the dashboard is told that alerts are arriving, failing, or not yet tried.</summary>
public sealed class GetAlertDeliveryStatusQueryHandlerTests
{
    /// <summary>The instant the fixtures start from.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A channel nothing was sent on is unknown, not ok.</summary>
    [Fact]
    public async Task A_channel_nothing_was_sent_on_is_unknown()
    {
        await using var dbContext = NotificationsTestContext.Create();

        var status = await ReadAsync(dbContext);

        Assert.Equal("unknown", status.Mail.State);
        Assert.Equal("unknown", status.Webhook.State);
        Assert.Null(status.Mail.FailureMessage);
    }

    /// <summary>A channel whose last alert failed is failing, with the sentence resolved from the stored code.</summary>
    [Fact]
    public async Task A_channel_whose_last_alert_failed_is_failing_with_a_resolved_sentence()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var state = new AlertChannelState(AlertChannel.Mail, Start);
        state.RecordFailure(Start, "MailDeliveryFailed");
        dbContext.AlertChannelStates.Add(state);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var status = await ReadAsync(dbContext);

        Assert.Equal("failing", status.Mail.State);
        Assert.Equal(Start, status.Mail.LastFailureAt);
        Assert.Equal("The mail server refused it.", status.Mail.FailureMessage);
        Assert.Equal("unknown", status.Webhook.State);
    }

    /// <summary>A failure code no table claims reads as the generic sentence, never as the code.</summary>
    [Fact]
    public async Task An_unclaimed_failure_code_reads_as_the_generic_sentence()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var state = new AlertChannelState(AlertChannel.Mail, Start);
        state.RecordFailure(Start, "SomethingNobodyDefined");
        dbContext.AlertChannelStates.Add(state);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var status = await ReadAsync(dbContext);

        Assert.Equal("The last alert could not be delivered.", status.Mail.FailureMessage);
    }

    /// <summary>A success after a failure reads ok (the inverse control).</summary>
    [Fact]
    public async Task A_success_after_a_failure_reads_ok()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var state = new AlertChannelState(AlertChannel.Mail, Start);
        state.RecordFailure(Start, "MailDeliveryFailed");
        state.RecordSuccess(Start.AddMinutes(1));
        dbContext.AlertChannelStates.Add(state);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var status = await ReadAsync(dbContext);

        Assert.Equal("ok", status.Mail.State);
        Assert.Null(status.Mail.LastFailureAt);
    }

    /// <summary>Reads the status through a handler with a fixed error-text table.</summary>
    /// <param name="dbContext">The context.</param>
    /// <returns>The status.</returns>
    private static async Task<Common.AlertDeliveryStatusDto> ReadAsync(Persistence.NotificationsDbContext dbContext)
    {
        var texts = new StubErrorTexts(new Dictionary<string, string>
        {
            ["MailDeliveryFailed"] = "The mail server refused it.",
            ["AlertDeliveryFailed"] = "The last alert could not be delivered.",
        });
        var handler = new GetAlertDeliveryStatusQueryHandler(dbContext, texts);

        var result = await handler.HandleAsync(new GetAlertDeliveryStatusQuery(), CancellationToken.None);
        return result.Value;
    }
}
