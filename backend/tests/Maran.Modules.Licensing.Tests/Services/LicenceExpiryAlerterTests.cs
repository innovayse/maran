using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LicenceExpiryAlerter"/>: the alert is a licensing event in the operator's language with the
/// expiry day and the days left, the renewal path is named, and no identifying fact (licence id, server id, link)
/// leaves in the mail. The shared path (journal, mail, webhook event, feed entry) is the real one.
/// </summary>
public sealed class LicenceExpiryAlerterTests
{
    /// <summary>A warning for a licence with three days left.</summary>
    private static readonly LicenceExpiryWarning Warning = new(3, 3, new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Builds the alerter over the real shared path, recording what it publishes.</summary>
    /// <param name="bus">Where the outputs are published.</param>
    /// <returns>The alerter.</returns>
    private static LicenceExpiryAlerter Alerter(RecordingMessageBus bus)
    {
        return new LicenceExpiryAlerter(
            new OperatorAlertDispatcher(
                StubAlertRecipientDirectory.Of([RenewalWorld.OperatorAddress]),
                bus,
                new FixedClock(DateTimeOffset.UnixEpoch)),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            RenewalWorld.AlertText());
    }

    /// <summary>Neither the subject nor the body is a resource key, in any language, and the body names the day and the days left.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task The_mail_reads_as_a_sentence_in_every_language_with_the_day_and_the_days_left(string culture)
    {
        using var scope = new CultureScope(culture);
        var bus = new RecordingMessageBus();

        await Alerter(bus).RaiseAsync(Warning, CancellationToken.None);

        var mail = Assert.Single(bus.Published.OfType<SendMailRequested>());
        Assert.DoesNotContain("LicenceExpiry", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("LicenceExpiry", mail.Body, StringComparison.Ordinal);
        Assert.Contains("2026-02-04", mail.Body, StringComparison.Ordinal);
        Assert.Contains("3", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// On the last day the English mail says "days left: 1" and never "1 days left"; the control is the same sentence
    /// with several days left.
    /// </summary>
    [Fact]
    public async Task The_english_mail_has_no_plural_slip_on_the_last_day()
    {
        using var scope = new CultureScope("en");
        var lastDay = new LicenceExpiryWarning(3, 1, new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));
        var lastBus = new RecordingMessageBus();
        var severalBus = new RecordingMessageBus();

        await Alerter(lastBus).RaiseAsync(lastDay, CancellationToken.None);
        await Alerter(severalBus).RaiseAsync(Warning, CancellationToken.None);

        var last = Assert.Single(lastBus.Published.OfType<SendMailRequested>()).Body;
        var several = Assert.Single(severalBus.Published.OfType<SendMailRequested>()).Body;
        Assert.Contains("days left: 1", last, StringComparison.Ordinal);
        Assert.DoesNotContain("1 days", last, StringComparison.Ordinal);
        Assert.Contains("days left: 3", several, StringComparison.Ordinal);
    }

    /// <summary>
    /// The day in the mail, the detail and the feed is the UTC calendar day of the instant, whatever offset the instant
    /// was written with; an instant late on the 26th UTC is the 26th, and the control is one just after midnight UTC.
    /// </summary>
    [Fact]
    public async Task The_day_is_the_utc_calendar_day_whatever_offset_the_expiry_carries()
    {
        var late = new LicenceExpiryWarning(3, 3, new DateTimeOffset(2026, 12, 27, 1, 0, 0, TimeSpan.FromHours(4)));
        var early = new LicenceExpiryWarning(3, 3, new DateTimeOffset(2026, 12, 26, 0, 30, 0, TimeSpan.Zero));
        var lateBus = new RecordingMessageBus();
        var earlyBus = new RecordingMessageBus();

        await Alerter(lateBus).RaiseAsync(late, CancellationToken.None);
        await Alerter(earlyBus).RaiseAsync(early, CancellationToken.None);

        Assert.Contains("expiry=2026-12-26", Assert.Single(lateBus.Published.OfType<OperatorAlertRaised>()).Alert.Detail, StringComparison.Ordinal);
        Assert.Equal("2026-12-26", Assert.Single(lateBus.Published.OfType<PanelNotificationRaised>()).Arguments[0]);
        Assert.Contains("expiry=2026-12-26", Assert.Single(earlyBus.Published.OfType<OperatorAlertRaised>()).Alert.Detail, StringComparison.Ordinal);
    }

    /// <summary>The Russian and Armenian subjects are translations, not the English sentence repeated.</summary>
    [Fact]
    public async Task The_russian_and_armenian_subjects_differ_from_the_english_one()
    {
        var subjects = new Dictionary<string, string>();
        foreach (var culture in new[] { "en", "ru", "hy" })
        {
            using var scope = new CultureScope(culture);
            var bus = new RecordingMessageBus();

            await Alerter(bus).RaiseAsync(Warning, CancellationToken.None);

            subjects[culture] = Assert.Single(bus.Published.OfType<SendMailRequested>()).Subject;
        }

        Assert.NotEqual(subjects["en"], subjects["ru"]);
        Assert.NotEqual(subjects["en"], subjects["hy"]);
    }

    /// <summary>The alert is a one-off licensing event whose detail is the threshold, the days left and the day, nothing else.</summary>
    [Fact]
    public async Task The_alert_is_a_licensing_event_carrying_only_threshold_days_and_day()
    {
        var bus = new RecordingMessageBus();

        await Alerter(bus).RaiseAsync(Warning, CancellationToken.None);

        var alert = Assert.Single(bus.Published.OfType<OperatorAlertRaised>()).Alert;
        Assert.Equal(AlertSource.Licensing, alert.Source);
        Assert.Equal(AlertTransition.Occurred, alert.Transition);
        Assert.Equal("LicenceExpiring", alert.Code);
        Assert.Equal("licence", alert.Subject);
        Assert.Equal("thresholdDays=3 daysLeft=3 expiry=2026-02-04", alert.Detail);
        Assert.Equal(NotificationSeverity.Warning, Assert.Single(bus.Published.OfType<PanelNotificationRaised>()).Severity);
    }
}
