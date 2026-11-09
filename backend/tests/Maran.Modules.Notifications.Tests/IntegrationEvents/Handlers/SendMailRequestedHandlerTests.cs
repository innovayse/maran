using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.IntegrationEvents.Handlers;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Notifications.Tests.IntegrationEvents.Handlers;

/// <summary>
/// R11's guarantee, stated as tests: the background sender records every failure and lets none of
/// them escape, because an escaping failure is how a token-bearing envelope reaches Wolverine's
/// dead-letter store and rests there on disk.
/// </summary>
public sealed class SendMailRequestedHandlerTests
{
    /// <summary>The code the mailer reports when the panel has no mail settings at all.</summary>
    /// <remarks>
    /// Spelled as a literal because the module's <c>ErrorMessages</c> is generated from the resx and
    /// is internal to the module assembly. That is the same idiom the other module test suites use,
    /// and it has a virtue: the code IS the contract, so a test asserting on the string catches a
    /// rename that a <c>nameof</c> would silently follow.
    /// </remarks>
    private const string SmtpNotConfiguredCode = "SmtpNotConfigured";

    /// <summary>The code the mailer reports when the mail server refused or could not be reached.</summary>
    private const string MailDeliveryFailedCode = "MailDeliveryFailed";

    /// <summary>A message shaped like the one this exists for: a reset mail carrying a live token.</summary>
    private static readonly SendMailRequested ResetMail = new(
        "customer@example.com",
        "Reset your password",
        "Use this link within the hour: https://panel.example.com/reset?token=SECRET-TOKEN-VALUE");

    /// <summary>An alert mail, which the dashboard's delivery state is about.</summary>
    private static readonly SendMailRequested AlertMail = new(
        "ops@example.com",
        "Disk is full",
        "The disk is 95% full.",
        MailPurpose.OperatorAlert);

    /// <summary>A mailer failure is journalled and never thrown.</summary>
    /// <remarks>
    /// The named test R11 rests on. Its mutation — rethrowing instead of catching — must turn it red,
    /// because a thrown handler is exactly what hands the token-bearing envelope to the dead-letter
    /// machinery this design exists to keep it out of.
    /// </remarks>
    [Fact]
    public async Task a_mailer_failure_is_audited_and_never_thrown()
    {
        var mailer = new RecordingMailer
        {
            Outcome = Result.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.Validation)),
        };
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        await handler.HandleAsync(ResetMail, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OutgoingMailAuditActions.MailSendFailed, entry.Action);
        Assert.Equal(ResetMail.Recipient, entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A mailer that throws is journalled and never rethrown.</summary>
    /// <remarks>
    /// The other half of the same guarantee. A mailer returning a failure result exercises the
    /// <c>if</c>; a mailer that raises exercises the <c>catch</c>, and only the second one proves the
    /// handler survives a library that decided to throw.
    /// </remarks>
    [Fact]
    public async Task A_mailer_that_throws_is_audited_and_never_rethrown()
    {
        var mailer = new RecordingMailer { Throws = new InvalidOperationException("the socket died") };
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        await handler.HandleAsync(ResetMail, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OutgoingMailAuditActions.MailSendFailed, entry.Action);
    }

    /// <summary>Cancellation is caught too, because a shutdown must not dead-letter the envelope either.</summary>
    /// <remarks>
    /// Every other background component in this panel rethrows on shutdown. This one deliberately
    /// does not: losing one unsent mail costs the user a second click, while a token written to disk
    /// costs rather more.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_send_is_audited_and_never_rethrown()
    {
        var mailer = new RecordingMailer { Throws = new OperationCanceledException() };
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await handler.HandleAsync(ResetMail, cancelled.Token);

        Assert.Single(audit.Entries);
    }

    /// <summary>A panel with no mail settings records that the mail was skipped, under its own action.</summary>
    /// <remarks>
    /// A separate action from a send failure because the two send an operator to different places:
    /// one is "you have not set this up", the other "your provider said no". Without the entry, a
    /// password reset that silently never arrives has nothing behind it at all.
    /// </remarks>
    [Fact]
    public async Task An_unconfigured_panel_records_that_the_mail_was_skipped()
    {
        var mailer = new RecordingMailer
        {
            Outcome = Result.Fail(Error.Of(SmtpNotConfiguredCode, ErrorType.Validation)),
        };
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        await handler.HandleAsync(ResetMail, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OutgoingMailAuditActions.MailSkippedNoSmtp, entry.Action);
        // And SUCCEEDED, which is the half this test did not assert: changing the outcome broke
        // nothing here, so a fresh install kept writing a red FAILED row for mail that was
        // correctly not sent (issue #71). The send-failure test above asserts the opposite, so the
        // two together say which event is which rather than that an entry was written.
        Assert.True(entry.Succeeded);
    }

    /// <summary>A journal that itself fails does not let the failure escape the handler either.</summary>
    /// <remarks>
    /// The audit write reaches PostgreSQL, so it can fail — and an exception out of it would leave
    /// the handler by the back door, which is the one thing the whole class exists to prevent.
    /// </remarks>
    [Fact]
    public async Task A_journal_that_fails_does_not_let_the_failure_escape()
    {
        var mailer = new RecordingMailer { Throws = new InvalidOperationException("the socket died") };
        var handler = Handler(mailer, new RefusingAuditWriter());

        await handler.HandleAsync(ResetMail, CancellationToken.None);
    }

    /// <summary>A successful send writes no failure entry, so the journal stays worth reading.</summary>
    [Fact]
    public async Task A_successful_send_writes_no_entry()
    {
        var mailer = new RecordingMailer();
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        await handler.HandleAsync(ResetMail, CancellationToken.None);

        Assert.Empty(audit.Entries);
        var sent = Assert.Single(mailer.Sends);
        Assert.Equal(ResetMail.Recipient, sent.Recipient);
        Assert.Equal(ResetMail.Body, sent.Body);
    }

    /// <summary>The journal never carries the body, which for a reset mail is the token itself.</summary>
    [Fact]
    public async Task The_journal_never_carries_the_body_of_a_failed_message()
    {
        var mailer = new RecordingMailer
        {
            Outcome = Result.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.Validation)),
        };
        var audit = new RecordingAuditWriter();
        var handler = Handler(mailer, audit);

        await handler.HandleAsync(ResetMail, CancellationToken.None);

        var serialized = System.Text.Json.JsonSerializer.Serialize(audit.Entries);
        Assert.DoesNotContain("SECRET-TOKEN-VALUE", serialized, StringComparison.Ordinal);
    }

    /// <summary>A failed operator alert marks the mail channel as failing, with the failure's code and not the server's words.</summary>
    [Fact]
    public async Task A_failed_operator_alert_marks_the_mail_channel_failing()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var mailer = new RecordingMailer { Outcome = Result.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.UpstreamRefused)) };

        await Handler(mailer, new RecordingAuditWriter(), dbContext).HandleAsync(AlertMail, CancellationToken.None);

        var state = await dbContext.AlertChannelStates.SingleAsync();
        Assert.Equal(AlertChannel.Mail, state.Channel);
        Assert.True(state.IsFailing);
        Assert.Equal(MailDeliveryFailedCode, state.LastFailureCode);
    }

    /// <summary>A delivered operator alert after a failure clears the failing state (the inverse control).</summary>
    [Fact]
    public async Task A_delivered_operator_alert_after_a_failure_clears_the_failing_state()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var mailer = new RecordingMailer { Outcome = Result.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.UpstreamRefused)) };
        var handler = Handler(mailer, new RecordingAuditWriter(), dbContext);
        await handler.HandleAsync(AlertMail, CancellationToken.None);

        mailer.Outcome = Result.Ok();
        await handler.HandleAsync(AlertMail, CancellationToken.None);

        var state = await dbContext.AlertChannelStates.SingleAsync();
        Assert.False(state.IsFailing);
        Assert.NotNull(state.LastSucceededAt);
    }

    /// <summary>A failed password-reset mail says nothing about alerts, so it records no channel state.</summary>
    [Fact]
    public async Task A_failed_transactional_mail_records_no_alert_channel_state()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var mailer = new RecordingMailer { Outcome = Result.Fail(Error.Of(MailDeliveryFailedCode, ErrorType.UpstreamRefused)) };

        await Handler(mailer, new RecordingAuditWriter(), dbContext).HandleAsync(ResetMail, CancellationToken.None);

        Assert.Empty(await dbContext.AlertChannelStates.ToListAsync());
    }

    /// <summary>An operator alert skipped because mail is not configured is not a delivery failure.</summary>
    [Fact]
    public async Task A_skipped_operator_alert_records_no_alert_channel_state()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var mailer = new RecordingMailer { Outcome = Result.Fail(Error.Of(SmtpNotConfiguredCode, ErrorType.Validation)) };

        await Handler(mailer, new RecordingAuditWriter(), dbContext).HandleAsync(AlertMail, CancellationToken.None);

        Assert.Empty(await dbContext.AlertChannelStates.ToListAsync());
    }

    /// <summary>Builds the handler over the doubles a test supplies.</summary>
    /// <param name="mailer">The mailer double.</param>
    /// <param name="audit">The journal double.</param>
    /// <param name="dbContext">The context the channel state is written through; a fresh one when omitted.</param>
    /// <returns>The handler.</returns>
    private static SendMailRequestedHandler Handler(
        RecordingMailer mailer,
        Sdk.Interfaces.IAuditWriter audit,
        NotificationsDbContext? dbContext = null)
    {
        var journal = new NotificationsAuditJournal(audit, new FakeCurrentUser());
        var recorder = new AlertChannelStateRecorder(dbContext ?? NotificationsTestContext.Create(), new FakeClock());
        return new SendMailRequestedHandler(mailer, journal, recorder, NullLogger<SendMailRequestedHandler>.Instance);
    }
}
