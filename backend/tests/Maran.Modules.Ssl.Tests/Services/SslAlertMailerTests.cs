using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Resources;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The certificate expiry alert (issue #161): raised once, inside the fourteen-day window, in the
/// language of the culture it is composed in, naming the domain, the days left and the last failure
/// and no path — and journalled as skipped when there is nobody to tell.
/// </summary>
public sealed class SslAlertMailerTests
{
    /// <summary>The recipient the stub directory answers with.</summary>
    private const string Recipient = "ops@example.test";

    /// <summary>The domain of the certificate under alert.</summary>
    private const string Domain = "shop.example.test";

    /// <summary>The failure code a failed renewal stored on the certificate.</summary>
    private const string FailureCode = "AcmeAuthorityUnreachable";

    /// <summary>The instant every case works at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A certificate inside the window is announced once, to the recipient, with its facts.</summary>
    [Fact]
    public async Task A_certificate_inside_the_window_is_announced_once_with_its_domain_and_days_left()
    {
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(10).AddHours(6));

        var raised = await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        Assert.True(raised);
        var mail = Assert.Single(world.Mails());
        Assert.Equal(Recipient, mail.Recipient);
        Assert.Contains(Domain, mail.Subject, StringComparison.Ordinal);
        Assert.Contains(Domain, mail.Body, StringComparison.Ordinal);
        Assert.Contains(" 10 ", mail.Body, StringComparison.Ordinal);
        Assert.Contains("2026-06-11 18:00", mail.Body, StringComparison.Ordinal);
        Assert.Equal(Now, certificate.ExpiryAlertedAt);
    }

    /// <summary>The raised alert is journalled as the panel, against the domain, and succeeded.</summary>
    [Fact]
    public async Task The_raised_alert_is_journalled_against_the_domain()
    {
        var world = World.Create(Recipient);

        await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(3)), Now, CancellationToken.None);

        var entry = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
        Assert.Equal($"CertificateExpiry:{Domain}", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second pass over the same certificate raises nothing: one alert per material.</summary>
    [Fact]
    public async Task A_second_pass_over_the_same_certificate_raises_nothing()
    {
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(10));

        var first = await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);
        var second = await world.Mailer.NotifyIfDueAsync(certificate, Now.AddHours(6), CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(world.Mails());
        Assert.Equal(Now, certificate.ExpiryAlertedAt);
    }

    /// <summary>A certificate already marked as alerted raises nothing, however close its expiry is.</summary>
    [Fact]
    public async Task A_certificate_already_marked_as_alerted_raises_nothing()
    {
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(2));
        certificate.MarkExpiryAlerted(Now.AddDays(-1));

        var raised = await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        Assert.False(raised);
        Assert.Empty(world.Mails());
        Assert.Empty(world.Audit.Entries);
        Assert.Equal(Now.AddDays(-1), certificate.ExpiryAlertedAt);
    }

    /// <summary>A certificate with more than fourteen days left raises nothing and is not marked.</summary>
    [Fact]
    public async Task A_certificate_outside_the_window_raises_nothing_and_is_not_marked()
    {
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(14).AddHours(1));

        var raised = await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        Assert.False(raised);
        Assert.Empty(world.Mails());
        Assert.Empty(world.Audit.Entries);
        Assert.Null(certificate.ExpiryAlertedAt);
    }

    /// <summary>Exactly fourteen days left is inside the window: the boundary is inclusive.</summary>
    [Fact]
    public async Task A_certificate_with_exactly_fourteen_days_left_is_announced()
    {
        var world = World.Create(Recipient);

        var raised = await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(14)), Now, CancellationToken.None);

        Assert.True(raised);
        Assert.Single(world.Mails());
    }

    /// <summary>An already expired certificate is announced with zero days left, never a negative figure.</summary>
    [Fact]
    public async Task An_expired_certificate_is_announced_with_zero_days_left()
    {
        var world = World.Create(Recipient);

        var raised = await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(-2)), Now, CancellationToken.None);

        Assert.True(raised);
        var mail = Assert.Single(world.Mails());
        Assert.Contains(" 0 ", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(" -", mail.Body, StringComparison.Ordinal);
        Assert.Contains("2026-05-30 12:00", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>After a renewal clears the mark, the next approach to expiry is announced afresh.</summary>
    [Fact]
    public async Task A_cleared_mark_lets_the_next_approach_to_expiry_alert_again()
    {
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(5));
        await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        certificate.ClearExpiryAlert();
        var raised = await world.Mailer.NotifyIfDueAsync(certificate, Now.AddDays(1), CancellationToken.None);

        Assert.True(raised);
        Assert.Equal(2, world.Mails().Count);
    }

    /// <summary>The mail carries the localized sentence of the last failure, and no path.</summary>
    /// <param name="culture">The culture the mail is composed in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task The_mail_names_the_domain_the_days_left_and_the_failure_and_no_path(string culture)
    {
        using var scope = new CultureScope(culture);
        var world = World.Create(Recipient);
        var certificate = Expiring(Now.AddDays(9).AddHours(1));
        certificate.RenewalFailed(FailureCode, Now.AddHours(-2));

        await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        var mail = Assert.Single(world.Mails());
        foreach (var text in new[] { mail.Subject, mail.Body })
        {
            Assert.Contains(Domain, text, StringComparison.Ordinal);
            Assert.DoesNotContain("/", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        }

        Assert.Contains("9", mail.Body, StringComparison.Ordinal);
        Assert.Contains(StubErrorTextProvider.SentencePrefix + FailureCode, mail.Body, StringComparison.Ordinal);
    }

    /// <summary>The Russian mail is Russian text and the English mail is English text.</summary>
    [Fact]
    public async Task The_subject_and_body_are_localized_in_english_and_russian()
    {
        SendMailRequested english;
        SendMailRequested russian;

        using (new CultureScope("en"))
        {
            var world = World.Create(Recipient);
            await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(4)), Now, CancellationToken.None);
            english = Assert.Single(world.Mails());
        }

        using (new CultureScope("ru"))
        {
            var world = World.Create(Recipient);
            await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(4)), Now, CancellationToken.None);
            russian = Assert.Single(world.Mails());
        }

        Assert.Contains("about to expire", english.Subject, StringComparison.Ordinal);
        Assert.Contains("Срок действия", russian.Subject, StringComparison.Ordinal);
        Assert.NotEqual(english.Body, russian.Body);
        Assert.NotEqual(english.Subject, russian.Subject);
    }

    /// <summary>A certificate whose renewal has not failed gets no failure sentence in its mail.</summary>
    [Fact]
    public async Task A_certificate_with_no_failed_renewal_carries_no_failure_sentence()
    {
        using var scope = new CultureScope("en");
        var world = World.Create(Recipient);

        await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(6)), Now, CancellationToken.None);

        var mail = Assert.Single(world.Mails());
        Assert.DoesNotContain(StubErrorTextProvider.SentencePrefix, mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("last renewal attempt", mail.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>With no alert address configured the alert is journalled as skipped and nothing is published.</summary>
    [Fact]
    public async Task With_no_alert_address_the_alert_is_journalled_as_skipped_and_no_mail_is_published()
    {
        var world = World.Create(null);
        var certificate = Expiring(Now.AddDays(7));

        var raised = await world.Mailer.NotifyIfDueAsync(certificate, Now, CancellationToken.None);

        Assert.True(raised);
        Assert.Empty(world.Mails());
        Assert.Single(world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
        var skipped = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == MailAuditActions.MailSkippedNoSmtp; });
        Assert.Equal($"CertificateExpiry:{Domain}", skipped.Subject);
        Assert.True(skipped.Succeeded);
        Assert.Equal(Now, certificate.ExpiryAlertedAt);
    }

    /// <summary>With an alert address configured, no skipped-mail entry is journalled.</summary>
    /// <remarks>The inverse control of the case above: a mailer that always journals a skip fails here.</remarks>
    [Fact]
    public async Task With_an_alert_address_no_skipped_entry_is_journalled()
    {
        var world = World.Create(Recipient);

        await world.Mailer.NotifyIfDueAsync(Expiring(Now.AddDays(7)), Now, CancellationToken.None);

        Assert.DoesNotContain(world.Audit.Entries, entry => { return entry.Action == MailAuditActions.MailSkippedNoSmtp; });
    }

    /// <summary>Builds an Acme certificate for <see cref="Domain"/> expiring at <paramref name="notAfter"/>.</summary>
    /// <param name="notAfter">When the certificate expires.</param>
    /// <returns>A certificate that has not been alerted.</returns>
    private static Certificate Expiring(DateTimeOffset notAfter)
    {
        return SslTestContext.Certificate(Guid.NewGuid(), Domain, notAfter);
    }

    /// <summary>The mailer under test with the doubles it was built over.</summary>
    private sealed class World
    {
        /// <summary>The mailer under test, over the module's real resource files.</summary>
        public required SslAlertMailer Mailer { get; init; }

        /// <summary>The bus the request to send is published on.</summary>
        public required RecordingMessageBus Bus { get; init; }

        /// <summary>The journal the entries are written into.</summary>
        public required RecordingAuditWriter Audit { get; init; }

        /// <summary>Builds the mailer over a recipient directory answering <paramref name="recipient"/>.</summary>
        /// <param name="recipient">The alert address, or null for a panel with none.</param>
        /// <returns>The mailer with its recording doubles.</returns>
        public static World Create(string? recipient)
        {
            var bus = new RecordingMessageBus();
            var audit = new RecordingAuditWriter();
            var factory = new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance);

            return new World
            {
                Bus = bus,
                Audit = audit,
                Mailer = new SslAlertMailer(
                    new OperatorAlertMailer(new StubAlertRecipientDirectory(recipient), bus),
                    new CertificateAuditJournal(audit, FakeCurrentUser.Admin()),
                    new StringLocalizer<NotificationMessages>(factory),
                    new CertificateRenewalFailureMessages(new StubErrorTextProvider())),
            };
        }

        /// <summary>The alert mails published so far.</summary>
        /// <returns>Every <see cref="SendMailRequested"/> on the bus, in order.</returns>
        public List<SendMailRequested> Mails()
        {
            return [.. Bus.Published.OfType<SendMailRequested>()];
        }
    }
}
