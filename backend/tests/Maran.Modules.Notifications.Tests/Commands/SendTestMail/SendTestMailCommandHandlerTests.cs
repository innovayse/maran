using Maran.Modules.Notifications.Commands.SendTestMail;
using Maran.Modules.Notifications.Resources;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Notifications.Tests.Commands.SendTestMail;

/// <summary>
/// The one mail path that reports its failure to a caller, because a caller is waiting: somebody
/// pressed a button precisely to find out whether mail works.
/// </summary>
public sealed class SendTestMailCommandHandlerTests
{
    /// <summary>The code the mailer reports when the panel has no mail settings at all.</summary>
    private const string SmtpNotConfiguredCode = "SmtpNotConfigured";

    /// <summary>The code the mailer reports when the mail server refused or could not be reached.</summary>
    private const string MailDeliveryFailedCode = "MailDeliveryFailed";

    /// <summary>The request every test in this class sends.</summary>
    private static readonly SendTestMailCommand Command = new("ops@example.com", "203.0.113.7", "curl/8");

    /// <summary>A delivered test message is journalled as sent, to the address it went to.</summary>
    [Fact]
    public async Task A_delivered_test_message_is_journalled_as_sent()
    {
        var mailer = new RecordingMailer();
        var audit = new RecordingAuditWriter();

        var result = await Handler(mailer, audit).HandleAsync(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.TestMailSent, entry.Action);
        Assert.Equal("ops@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The message's words come from the resx, not from a literal in the handler.</summary>
    /// <remarks>
    /// Asserted through the localizer's key rather than through an English sentence: the sentence is
    /// translated into three languages and would fail this test the day a translator improved it,
    /// while proving nothing the key does not already prove.
    /// </remarks>
    [Fact]
    public async Task The_test_messages_words_come_from_the_resource_file()
    {
        var mailer = new RecordingMailer();

        await Handler(mailer, new RecordingAuditWriter()).HandleAsync(Command, CancellationToken.None);

        var sent = Assert.Single(mailer.Sends);
        Assert.Equal("TestMailSubject", sent.Subject);
        Assert.Equal("TestMailBody", sent.Body);
    }

    /// <summary>A refused message is reported to the caller and journalled as a send failure.</summary>
    /// <remarks>
    /// Reporting it is what makes this path different from the background sender: there, a failure
    /// has nowhere to go but the journal; here, an administrator is waiting for the answer.
    /// </remarks>
    [Fact]
    public async Task A_refused_test_message_is_reported_to_the_caller_and_audited_as_a_send_failure()
    {
        var mailer = new RecordingMailer { Outcome = Result<bool>.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.Validation)) };
        var audit = new RecordingAuditWriter();

        var result = await Handler(mailer, audit).HandleAsync(Command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailDeliveryFailedCode, result.Error!.Code);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.MailSendFailed, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An unconfigured panel is journalled under its own action, not as a send failure.</summary>
    /// <remarks>
    /// "You have not set this up" and "your provider said no" send an operator to two different
    /// places, and one entry covering both would send them to the wrong one about half the time.
    /// </remarks>
    [Fact]
    public async Task An_unconfigured_panel_audits_the_test_message_as_skipped()
    {
        var mailer = new RecordingMailer { Outcome = Result<bool>.Fail(Error.Of(SmtpNotConfiguredCode, ErrorType.Validation)) };
        var audit = new RecordingAuditWriter();

        var result = await Handler(mailer, audit).HandleAsync(Command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SmtpNotConfiguredCode, result.Error!.Code);
        Assert.Equal(AuditActions.MailSkippedNoSmtp, Assert.Single(audit.Entries).Action);
    }

    /// <summary>Builds the handler over the doubles a test supplies.</summary>
    /// <param name="mailer">The mailer double.</param>
    /// <param name="audit">The journal double.</param>
    /// <returns>The handler.</returns>
    private static SendTestMailCommandHandler Handler(RecordingMailer mailer, RecordingAuditWriter audit)
    {
        var journal = new NotificationsAuditJournal(audit, new FakeCurrentUser());
        return new SendTestMailCommandHandler(mailer, journal, new StubStringLocalizer<NotificationMessages>());
    }
}
