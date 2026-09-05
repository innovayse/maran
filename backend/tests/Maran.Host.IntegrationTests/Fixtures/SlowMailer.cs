using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A mailer that takes as long as a genuinely slow mail server, so a test can prove that nothing on
/// the request path waits for it.
/// </summary>
/// <remarks>
/// The delay is the whole point. R11 exists because a reset endpoint that awaited SMTP would answer
/// in seconds for an address that exists and instantly for one that does not — an enumeration oracle
/// anybody can read with a stopwatch. A mailer that returned quickly could not tell a decoupled
/// design from an inline one.
/// </remarks>
public sealed class SlowMailer : IMailer
{
    /// <summary>How long one send takes.</summary>
    public static readonly TimeSpan SendDuration = TimeSpan.FromSeconds(5);

    /// <summary>Completes the first time a send is entered, so a test can prove the message was delivered.</summary>
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every recipient a send was attempted for, in order.</summary>
    public List<string> Recipients { get; } = [];

    /// <summary>Resolves once a send has begun.</summary>
    public Task Entered
    {
        get
        {
            return _entered.Task;
        }
    }

    /// <summary>Waits <see cref="SendDuration"/>, then reports success.</summary>
    /// <param name="recipient">The address to deliver to.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">The plain-text body.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, five seconds later.</returns>
    public async Task<Result<bool>> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        lock (Recipients)
        {
            Recipients.Add(recipient);
        }

        _entered.TrySetResult();

        // The one place a test may wait on wall-clock time, because the duration IS the fixture: it
        // stands in for a mail server that takes seconds, which is the condition the decoupling has
        // to survive (rules/testing.md forbids sleeps that stand in for synchronisation, which this
        // is not).
        await Task.Delay(SendDuration, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
