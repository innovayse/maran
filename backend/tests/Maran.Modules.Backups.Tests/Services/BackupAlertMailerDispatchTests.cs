using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// What a backup alert becomes once it reaches the operator-alert dispatcher: one mail per address,
/// plus the webhook event and the feed entry, which do not depend on any address existing.
/// </summary>
public sealed class BackupAlertMailerDispatchTests
{
    /// <summary>A failed scheduled run is dispatched as a one-off event about the account, with the failure as detail.</summary>
    [Fact]
    public async Task A_failed_run_is_dispatched_as_an_occurred_event_about_the_account()
    {
        var world = new World(["ops@example.test"]);

        await world.Mailer.ScheduledRunFailedAsync("cust01", "BackupFailed", CancellationToken.None);

        var raised = Assert.Single(world.Bus.Published.OfType<OperatorAlertRaised>());
        Assert.Equal(AlertSource.Backups, raised.Alert.Source);
        Assert.Equal("ScheduledRunFailed", raised.Alert.Code);
        Assert.Equal(AlertTransition.Occurred, raised.Alert.Transition);
        Assert.Equal("cust01", raised.Alert.Subject);
        Assert.False(string.IsNullOrWhiteSpace(raised.Alert.Detail));
    }

    /// <summary>The feed entry is a warning from the backup source, carrying the detail first and a backups destination.</summary>
    [Fact]
    public async Task The_feed_entry_is_a_backup_warning_carrying_the_detail_first()
    {
        var world = new World(["ops@example.test"]);

        await world.Mailer.ScheduledRunFailedAsync("cust01", "BackupFailed", CancellationToken.None);

        var raised = Assert.Single(world.Bus.Published.OfType<OperatorAlertRaised>());
        var entry = Assert.Single(world.Bus.Published.OfType<PanelNotificationRaised>());
        Assert.Equal(NotificationSource.Backup, entry.Source);
        Assert.Equal(NotificationSeverity.Warning, entry.Severity);
        Assert.Equal("ScheduledRunFailed", entry.Code);
        Assert.Equal(raised.Alert.Detail, Assert.Single(entry.Arguments));
        Assert.True(NotificationDeepLinks.IsKnown(entry.DeepLink));
    }

    /// <summary>Every configured address gets its own mail, marked as an operator alert.</summary>
    [Fact]
    public async Task Every_address_gets_a_mail_marked_as_an_operator_alert()
    {
        var world = new World(["a@example.test", "b@example.test"]);

        await world.Mailer.DestinationRefusedAsync("cust01", "BackupDestinationUnavailable", CancellationToken.None);

        var mails = world.Bus.Published.OfType<SendMailRequested>().ToList();
        Assert.Equal(["a@example.test", "b@example.test"], mails.Select(mail => { return mail.Recipient; }));
        Assert.All(mails, mail => { Assert.Equal(MailPurpose.OperatorAlert, mail.Purpose); });
    }

    /// <summary>With no address the mail is skipped and journalled as a success, but the webhook event and the feed entry are still published.</summary>
    [Fact]
    public async Task With_no_address_only_the_mail_is_skipped()
    {
        var world = new World([]);

        await world.Mailer.ScheduledRunFailedAsync("cust01", "BackupFailed", CancellationToken.None);

        Assert.Empty(world.Bus.Published.OfType<SendMailRequested>());
        Assert.Single(world.Bus.Published.OfType<OperatorAlertRaised>());
        Assert.Single(world.Bus.Published.OfType<PanelNotificationRaised>());
        Assert.Single(
            world.Audit.Entries,
            entry => { return entry.Action == OutgoingMailAuditActions.MailSkippedNoSmtp && entry.Succeeded; });
    }

    /// <summary>The doubles one test needs, built around the real mailer and dispatcher.</summary>
    private sealed class World
    {
        /// <summary>The bus every output is published on.</summary>
        public RecordingMessageBus Bus { get; } = new();

        /// <summary>The journal the entries are written into.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The mailer under test.</summary>
        public BackupAlertMailer Mailer { get; }

        /// <summary>Builds the mailer over a directory answering <paramref name="recipients"/>.</summary>
        /// <param name="recipients">The alert addresses.</param>
        public World(IReadOnlyList<string> recipients)
        {
            Mailer = new BackupAlertMailer(
                new OperatorAlertDispatcher(
                    StubAlertRecipientDirectory.Of(recipients), Bus, new FakeClock(DateTimeOffset.UnixEpoch)),
                new BackupAuditJournal(Audit, FakeCurrentUser.Admin()),
                BackupsTestContext.AlertText(),
                BackupsTestContext.FailureNames());
        }
    }
}
