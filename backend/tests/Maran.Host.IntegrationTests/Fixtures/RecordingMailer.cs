using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IMailer"/> double that keeps every message it was given and can be made slow.
/// </summary>
/// <remarks>
/// The delay is the whole point of the type. R11 says the reset endpoint must not wait for SMTP,
/// because a known address would then cost a full round trip while an unknown one returned
/// instantly — an account-enumeration oracle anybody can read with a stopwatch. A mailer that takes
/// five seconds turns that difference from a microsecond nobody can measure over a network into a
/// gap no measurement could miss, so a test can prove the ABSENCE of it.
/// </remarks>
public sealed class RecordingMailer : IMailer
{
    /// <summary>How long each send takes before it succeeds.</summary>
    private readonly TimeSpan _delay;

    /// <summary>The messages recorded so far. Guarded by itself; never handed out directly.</summary>
    private readonly List<SentMail> _sent = [];

    /// <summary>Creates the mailer.</summary>
    /// <param name="delay">How long each send should take.</param>
    public RecordingMailer(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>Every message handed to this mailer, in order.</summary>
    /// <remarks>
    /// A copy taken under the lock, because sends run on the message queue's threads while a test
    /// reads: indexing or counting the live list while another send appends to it is a data race in
    /// its own right, quite apart from the ordering one <see cref="Entered"/> used to carry.
    /// </remarks>
    public IReadOnlyList<SentMail> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    /// <summary>Signalled once a send has been recorded, before the delay.</summary>
    /// <remarks>
    /// <para>
    /// A test asserting that a publish was fast needs to know the publish went somewhere: a message
    /// that reached no handler at all would be just as fast, and would pass a timing assertion while
    /// meaning the opposite.
    /// </para>
    /// <para>
    /// The signal is raised after <see cref="Sent"/> has been appended to, so every waiter that
    /// resumes on it can read the message that caused it. Raising it first is a data race: the
    /// source completes with <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, so
    /// the waiter runs concurrently with the rest of this method and observed an empty list.
    /// </para>
    /// </remarks>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public async Task<Result<bool>> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add(new SentMail(recipient, subject, body));
        }

        // Signalled AFTER the record exists, never before. `Entered` is constructed with
        // RunContinuationsAsynchronously, so a waiter resumes on another thread the instant this is
        // set; signalling first let that waiter reach `Sent[0]` before `Sent.Add` had run, and the
        // whole contract of this signal is "a waiter may now read what was sent".
        Entered.TrySetResult();

        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, CancellationToken.None);
        }

        return Result<bool>.Ok(true);
    }
}
