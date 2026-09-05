using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>A mailer that records what it was asked to send and answers however a test says.</summary>
/// <remarks>
/// It can also be told to THROW, which is the case the mail handler exists to survive: a mailer that
/// only ever returned a failure result would let a handler with no try/catch pass, and the whole
/// point of R11 is that nothing escapes that handler.
/// </remarks>
public sealed class RecordingMailer : IMailer
{
    /// <summary>Every send attempted, in order, as (recipient, subject, body).</summary>
    public List<(string Recipient, string Subject, string Body)> Sends { get; } = [];

    /// <summary>The result every send returns, unless <see cref="Throws"/> is set.</summary>
    public Result<bool> Outcome { get; set; } = Result<bool>.Ok(true);

    /// <summary>When set, every send throws this instead of returning.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Records the send and answers as configured.</summary>
    /// <param name="recipient">The address to deliver to.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">The plain-text body.</param>
    /// <param name="cancellationToken">Unused; nothing here waits.</param>
    /// <returns>The configured outcome.</returns>
    public Task<Result<bool>> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        Sends.Add((recipient, subject, body));

        if (Throws is not null)
        {
            throw Throws;
        }

        return Task.FromResult(Outcome);
    }
}
