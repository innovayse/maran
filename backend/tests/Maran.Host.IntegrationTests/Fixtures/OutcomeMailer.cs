using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IMailer"/> double whose answer a test chooses and can change between sends: it
/// delivers, refuses the way a mail server refuses, or reports that no mail settings exist.
/// </summary>
/// <remarks>
/// The three outcomes are the three the real mailer produces and the alert-delivery state tells
/// apart: a delivery that worked, a delivery that was attempted and failed, and no attempt at all
/// because outgoing mail is not configured. The codes are the names of the Notifications module's
/// own error messages, spelled out here because that table is internal to the module.
/// </remarks>
public sealed class OutcomeMailer : IMailer
{
    /// <summary>The code of a delivery the mail server refused.</summary>
    private const string DeliveryFailedCode = "MailDeliveryFailed";

    /// <summary>The code of a send that never started because no mail settings are stored.</summary>
    private const string NotConfiguredCode = "SmtpNotConfigured";

    /// <summary>What the next send answers.</summary>
    private Result _outcome;

    /// <summary>Creates a mailer that delivers every message until told otherwise.</summary>
    public OutcomeMailer()
    {
        _outcome = Result.Ok();
    }

    /// <summary>Makes every following send succeed.</summary>
    public void Deliver()
    {
        _outcome = Result.Ok();
    }

    /// <summary>Makes every following send fail the way a refused delivery does.</summary>
    public void RefuseDelivery()
    {
        _outcome = Result.Fail(Error.Of(DeliveryFailedCode, ErrorType.UpstreamRefused));
    }

    /// <summary>Makes every following send fail the way a panel with no mail settings does.</summary>
    public void LackSettings()
    {
        _outcome = Result.Fail(Error.Of(NotConfiguredCode, ErrorType.Validation));
    }

    /// <inheritdoc />
    public Task<Result> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_outcome);
    }
}
