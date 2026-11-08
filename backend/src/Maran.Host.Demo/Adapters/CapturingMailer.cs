using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// The demo's <see cref="IMailer"/>: it accepts every message and sends none. It opens no connection,
/// reads no mail setting and keeps only a bounded list of who was written to and about what.
/// </summary>
/// <remarks>
/// <para>
/// <b>The body is dropped.</b> A body can carry a token (a reset link, an invitation) and rules/security.md
/// item 8 keeps anything that acts as a secret out of storage that outlives the request. The list is in
/// memory and capped, so a script cannot grow it (threat note T3, T6).
/// </para>
/// <para>
/// It always answers success: the demo has no mail server to be missing, and answering
/// <c>SmtpNotConfigured</c> would make every flow that sends mail look broken to a visitor.
/// </para>
/// </remarks>
public sealed class CapturingMailer : IMailer
{
    /// <summary>Messages remembered; the oldest is dropped beyond this.</summary>
    public const int Capacity = 100;

    /// <summary>Longest recipient or subject kept, in characters.</summary>
    public const int MaxFieldLength = 200;

    /// <summary>Guards <see cref="_captured"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The remembered messages, oldest first.</summary>
    private readonly Queue<CapturedMail> _captured = new();

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>The remembered messages, oldest first, as a snapshot.</summary>
    public IReadOnlyList<CapturedMail> Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured.ToList();
            }
        }
    }

    /// <summary>Creates the mailer.</summary>
    /// <param name="clock">The time source.</param>
    public CapturingMailer(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>Remembers the message's recipient and subject and reports it as sent.</summary>
    /// <param name="recipient">The address to deliver to.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">The plain-text body, which is never kept.</param>
    /// <param name="cancellationToken">Cancellation token; unused, nothing here waits.</param>
    /// <returns>Always success.</returns>
    public Task<Result> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var mail = new CapturedMail(Cap(recipient), Cap(subject), _clock.UtcNow);

        lock (_gate)
        {
            _captured.Enqueue(mail);
            while (_captured.Count > Capacity)
            {
                _captured.Dequeue();
            }
        }

        return Task.FromResult(Result.Ok());
    }

    /// <summary>Cuts a value to <see cref="MaxFieldLength"/> characters.</summary>
    /// <param name="value">The value, possibly null.</param>
    /// <returns>The value or its first characters; empty for null.</returns>
    private static string Cap(string? value)
    {
        value ??= string.Empty;
        return value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];
    }
}
