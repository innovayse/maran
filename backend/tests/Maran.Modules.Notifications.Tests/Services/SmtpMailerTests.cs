using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Notifications.Tests.Services;

/// <summary>
/// The mailer against a real socket: the transport security the panel stored is the transport
/// security that reaches the wire, a mail server behaving badly becomes a result rather than an
/// exception, and an unconfigured panel says so under its own code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a socket and not a double.</b> <c>ToSocketOptions</c>' discard arm IS the STARTTLS path —
/// <see cref="SmtpSecurity"/> has three members and two of them are named — so an edit changing it
/// to <c>SecureSocketOptions.None</c> compiles, and every panel on the fresh-install default then
/// submits its provider credential in the clear. Nothing short of watching what the client puts on
/// the wire can tell that edit from the correct code.
/// </para>
/// <para>
/// <b>Why the mapping case enumerates the enum.</b> Three hand-written cases would still pass on the
/// day a fourth member is added and silently falls into the discard arm — which is the same class of
/// defect. <see cref="EverySecurityMode"/> reads <c>Enum.GetValues</c>, and
/// <see cref="ExpectedFor"/> throws for a member nobody pinned, so a new member fails this suite
/// rather than inheriting whatever the discard arm happens to say.
/// </para>
/// </remarks>
public sealed class SmtpMailerTests
{
    /// <summary>The code the mailer reports when the panel has no mail settings at all.</summary>
    /// <remarks>
    /// A literal rather than a <c>nameof</c>, because the string IS the contract: two handlers branch
    /// on it (<c>SendMailRequestedHandler</c> and <c>SendTestMailCommandHandler</c>) to tell "you have
    /// not set this up" apart from "your provider said no". A <c>nameof</c> would follow a rename and
    /// leave both handlers reading a code nothing produces.
    /// </remarks>
    private const string SmtpNotConfiguredCode = "SmtpNotConfigured";

    /// <summary>The code the mailer reports when the mail server refused or could not be reached.</summary>
    private const string MailDeliveryFailedCode = "MailDeliveryFailed";

    /// <summary>The instant the seeded settings row records as its save time.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every member of <see cref="SmtpSecurity"/>, read from the enum rather than listed.</summary>
    /// <returns>One case per member, including any added after this was written.</returns>
    public static TheoryData<SmtpSecurity> EverySecurityMode()
    {
        var data = new TheoryData<SmtpSecurity>();

        foreach (var member in Enum.GetValues<SmtpSecurity>())
        {
            data.Add(member);
        }

        return data;
    }

    /// <summary>Each stored security mode reaches the wire as the connection it names.</summary>
    /// <remarks>
    /// The mutation this exists for: changing <c>ToSocketOptions</c>' discard arm to
    /// <c>SecureSocketOptions.None</c>. STARTTLS then completes a plaintext transaction against a
    /// server that never offered the extension, and the delivered assertion for
    /// <see cref="SmtpSecurity.StartTls"/> goes red.
    /// </remarks>
    /// <param name="security">The mode stored in the panel's settings.</param>
    [Theory]
    [MemberData(nameof(EverySecurityMode))]
    public async Task Every_stored_security_mode_reaches_the_wire_as_the_connection_it_names(SmtpSecurity security)
    {
        using var server = new FakeSmtpServer();
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, server.Port, security);

        using var scopes = Scopes(dbContext);
        var mailer = new SmtpMailer(scopes.Settings, NullLogger<SmtpMailer>.Instance);

        var result = await mailer.SendAsync("ops@example.com", "Subject", "Body", CancellationToken.None);

        var expected = ExpectedFor(security);
        Assert.Equal(expected.Delivered, result.IsSuccess);
        Assert.Equal(expected.OpensWithTls, server.SawTlsHandshake);
        Assert.Equal(expected.SpeaksSmtpInTheClear, server.Commands.Contains("EHLO"));
        Assert.Equal(expected.Delivered ? 1 : 0, server.AcceptedMessages);

        if (!expected.Delivered)
        {
            Assert.Equal(MailDeliveryFailedCode, result.Error!.Code);
        }
    }

    /// <summary>A mail server that hangs up mid-conversation becomes a failed result, never an exception.</summary>
    /// <remarks>
    /// The type's "it never throws" claim, which is load-bearing for R11: the reset mail is sent in
    /// the background off a local queue, so a throw here escapes into the messaging runtime rather
    /// than into a caller, and the envelope it was carrying holds a live token. The mutation:
    /// replacing the broad catch's <c>return</c> with a rethrow.
    /// </remarks>
    [Fact]
    public async Task A_mail_server_that_hangs_up_becomes_a_failed_result_and_not_an_exception()
    {
        using var server = new FakeSmtpServer(hangsUpAfterGreeting: true);
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, server.Port, SmtpSecurity.None);

        using var scopes = Scopes(dbContext);
        var mailer = new SmtpMailer(scopes.Settings, NullLogger<SmtpMailer>.Instance);

        var result = await mailer.SendAsync("ops@example.com", "Subject", "Body", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailDeliveryFailedCode, result.Error!.Code);
    }

    /// <summary>The provider's own words never leave the mailer as a result value.</summary>
    /// <remarks>
    /// A rejection sentence can name the host, the software version and the sender's account. The
    /// caller gets a code and nothing else (rules/security.md item 8), so the failure carries no room
    /// for the mail server's text at all.
    /// </remarks>
    [Fact]
    public async Task A_failure_carries_only_a_code_and_never_the_mail_servers_words()
    {
        using var server = new FakeSmtpServer(hangsUpAfterGreeting: true);
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, server.Port, SmtpSecurity.None);

        using var scopes = Scopes(dbContext);
        var mailer = new SmtpMailer(scopes.Settings, NullLogger<SmtpMailer>.Instance);

        var result = await mailer.SendAsync("ops@example.com", "Subject", "Body", CancellationToken.None);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Error);
        Assert.DoesNotContain("fake ESMTP", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", serialized, StringComparison.Ordinal);
    }

    /// <summary>A panel with no mail settings fails under its own code and opens no connection.</summary>
    /// <remarks>
    /// Two handlers branch on this exact string to journal <c>MailSkippedNoSmtp</c> rather than
    /// <c>MailSendFailed</c>. Its mutation — returning <c>MailDeliveryFailed</c> here — turns this
    /// red, and would otherwise send an administrator who never configured mail to look at a mail
    /// server that does not exist.
    /// </remarks>
    [Fact]
    public async Task An_unconfigured_panel_fails_as_not_configured_without_opening_a_connection()
    {
        using var server = new FakeSmtpServer();
        await using var dbContext = NotificationsTestContext.Create();

        using var scopes = Scopes(dbContext);
        var mailer = new SmtpMailer(scopes.Settings, NullLogger<SmtpMailer>.Instance);

        var result = await mailer.SendAsync("ops@example.com", "Subject", "Body", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SmtpNotConfiguredCode, result.Error!.Code);
        Assert.Empty(server.Commands);
        Assert.False(server.SawTlsHandshake);
    }

    /// <summary>What one security mode must look like from the far end of the socket.</summary>
    /// <param name="security">The mode being pinned.</param>
    /// <returns>Whether the message is delivered, whether TLS comes first, and whether SMTP is spoken in the clear.</returns>
    /// <remarks>
    /// The server offers neither STARTTLS nor TLS, so the three modes separate cleanly: a plain
    /// connection completes, a STARTTLS connection speaks in the clear and then refuses to continue
    /// without the extension it requires, and implicit TLS never gets as far as a word of SMTP.
    /// </remarks>
    private static (bool Delivered, bool OpensWithTls, bool SpeaksSmtpInTheClear) ExpectedFor(SmtpSecurity security)
    {
        return security switch
        {
            SmtpSecurity.None => (Delivered: true, OpensWithTls: false, SpeaksSmtpInTheClear: true),
            SmtpSecurity.StartTls => (Delivered: false, OpensWithTls: false, SpeaksSmtpInTheClear: true),
            SmtpSecurity.ImplicitTls => (Delivered: false, OpensWithTls: true, SpeaksSmtpInTheClear: false),
            _ => throw new InvalidOperationException(
                $"{security} was added to SmtpSecurity without pinning the socket option it maps to. "
                + "Add its case here rather than letting it inherit ToSocketOptions' discard arm."),
        };
    }

    /// <summary>Builds the container the settings cache resolves its scopes from.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    /// <returns>The factory, whose <c>Settings</c> is the cache the mailer reads through.</returns>
    private static TestScopeFactory Scopes(NotificationsDbContext dbContext)
    {
        return new TestScopeFactory(dbContext);
    }

    /// <summary>Points the panel's mail settings at the fake server.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="port">The loopback port the fake server is listening on.</param>
    /// <param name="security">The transport security to store.</param>
    /// <returns>Resolves once the row is saved.</returns>
    /// <remarks>
    /// The user name is empty on purpose: the fake server advertises no <c>AUTH</c>, and the mailer
    /// offers credentials only when one is configured.
    /// </remarks>
    private static async Task SeedAsync(NotificationsDbContext dbContext, int port, SmtpSecurity security)
    {
        dbContext.SmtpSettings.Add(new SmtpSettings(
            "127.0.0.1",
            port,
            security,
            string.Empty,
            string.Empty,
            "panel@example.com",
            "Panel",
            "ops@example.com",
            Saved));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
