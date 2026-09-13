using MailKit.Net.Smtp;
using MailKit.Security;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Models;
using Maran.Modules.Notifications.Resources;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// The panel's only outgoing-mail implementation, and the only file in the product that names a mail
/// library.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything about SMTP is confined here on purpose.</b> The rest of the module — the alert
/// evaluator, the test-mail command, the handler for other modules' requests — speaks only
/// <see cref="IMailer"/>, so a change of library, of transport, or of a provider's quirk touches one
/// file, and a test double for any of them is one small class.
/// </para>
/// <para>
/// <b>It never throws.</b> A mail server that refuses, times out, or resolves to nothing is somebody
/// else's infrastructure behaving badly, which is an expected outcome rather than a bug in this
/// process — so every failure leaves here as a typed <see cref="Error"/>. That is load-bearing for
/// R11: the background sender must not let a token-bearing message reach Wolverine's dead-letter
/// machinery, and the surest guarantee is that the layer under it does not raise.
/// </para>
/// <para>
/// <b>A timeout is always set, and there is deliberately no retry.</b> rules/csharp.md requires
/// every outbound call to be bounded, and <see cref="SendTimeout"/> bounds this one — without it a
/// mail server that accepts a connection and then says nothing would hold a background worker for
/// as long as the process lives. A retry, by contrast, is explicitly NOT wanted here (R11): a failed
/// send is journalled and abandoned, because the two things it might be retrying are a permanent
/// refusal, which will refuse again, and a password reset, whose token expires and whose owner can
/// simply ask again. A retry would also mean holding the token-bearing body in memory for longer,
/// for no gain.
/// </para>
/// <para>
/// <b>The provider's own words are logged, never returned.</b> A rejection text can name the host,
/// the software version, and the sender's account; the caller receives the code
/// <c>MailDeliveryFailed</c> and nothing else, exactly as the agent's error text is treated
/// (rules/security.md item 8). The BODY is never logged, at any level: for a reset mail it holds a
/// live token.
/// </para>
/// </remarks>
public sealed class SmtpMailer : IMailer
{
    /// <summary>How long one whole send may take before it is abandoned.</summary>
    /// <remarks>
    /// Thirty seconds covers a slow provider on a cold TLS handshake and is far short of the point
    /// at which a stuck send would matter — the sender is a background worker, and the request that
    /// caused the mail has long since been answered.
    /// </remarks>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a send the mail server would not complete.</summary>
    /// <remarks>
    /// The recipient and the reason, never the subject and never the body. The reason is the mail
    /// server's own sentence, which belongs in an operator's log and nowhere else.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogSendFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(SmtpMailer)),
            "The mail server would not accept a message for {Recipient}");

    /// <summary>The panel's mail settings, cached and invalidated on save.</summary>
    private readonly SmtpSettingsCache _settings;

    /// <summary>Where the mail server's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<SmtpMailer> _logger;

    /// <summary>Creates the mailer.</summary>
    /// <param name="settings">The panel's mail settings, cached and invalidated on save.</param>
    /// <param name="logger">Where the mail server's diagnostic text is recorded.</param>
    public SmtpMailer(SmtpSettingsCache settings, ILogger<SmtpMailer> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var profile = await _settings.GetAsync(cancellationToken);

        if (profile is null)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.SmtpNotConfigured), ErrorType.Validation));
        }

        try
        {
            await DeliverAsync(profile, recipient, subject, body, cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down, not the mail server refusing. Re-thrown so the caller's own
            // cancellation handling sees it as cancellation; the background handler treats it the
            // same way every other hosted component does.
            throw;
        }
        catch (Exception exception)
        {
            // Deliberately broad, and this is the one place in the module where that is right.
            // MailKit reports a refusal, a protocol violation, an authentication failure, a DNS
            // miss, a TLS mismatch and a timeout as six unrelated exception types, and the caller's
            // answer to every one of them is identical: journal it and give up. Catching them
            // individually would be a list that silently stops covering the seventh.
            LogSendFailed(_logger, recipient, exception);
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.MailDeliveryFailed), ErrorType.Failure));
        }
    }

    /// <summary>Maps the panel's stored choice onto the library's socket options.</summary>
    /// <param name="security">How the connection is to be protected.</param>
    /// <returns>The matching socket option.</returns>
    /// <remarks>
    /// Written out rather than cast, and the default arm is the STRICTER of the two encrypted modes
    /// rather than <see cref="SecureSocketOptions.None"/>: an unrecognised value must never
    /// downgrade a connection to plain text, which would hand the submission credential to anybody
    /// on the path.
    /// </remarks>
    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security)
    {
        return security switch
        {
            SmtpSecurity.None => SecureSocketOptions.None,
            SmtpSecurity.ImplicitTls => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        };
    }

    /// <summary>Builds the message and hands it to the server.</summary>
    /// <param name="profile">The panel's mail settings.</param>
    /// <param name="recipient">The address to deliver to.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">The plain-text body.</param>
    /// <param name="cancellationToken">Cancellation token for the send.</param>
    /// <remarks>
    /// Authentication is attempted only when a user name is configured. A relay on localhost takes
    /// none, and offering credentials it did not ask for is how a perfectly working relay starts
    /// refusing the panel's mail.
    /// </remarks>
    private static async Task DeliverAsync(
        SmtpProfile profile,
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(profile.FromName, profile.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient
        {
            Timeout = (int)SendTimeout.TotalMilliseconds,
        };

        await client.ConnectAsync(profile.Host, profile.Port, ToSocketOptions(profile.Security), cancellationToken);

        if (!string.IsNullOrEmpty(profile.Username))
        {
            await client.AuthenticateAsync(profile.Username, profile.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
