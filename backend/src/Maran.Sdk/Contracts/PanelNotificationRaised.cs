using Maran.SharedKernel.Utilities.Text;

namespace Maran.Sdk.Contracts;

/// <summary>
/// Asks the Notifications module to add one entry to the administrators' feed. Published after the
/// producer's own commit on a local, non-durable queue: a lost entry is acceptable, the journal and
/// the producer's own page remain the record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Text is bounded and cleaned here, once, in the constructor.</b> A subject or argument can be
/// attacker-influenced (a task subject, a service manager's words), and a stored entry is read back
/// by every administrator. Control characters are removed and lengths capped before the message
/// exists, so no consumer has to remember to. The feed renders the result as plain text.
/// </para>
/// <para>
/// <b>The entry carries a code and arguments, not a sentence.</b> The sentence is resolved when the
/// feed is read, in the reader's language.
/// </para>
/// </remarks>
public sealed record PanelNotificationRaised
{
    /// <summary>The longest subject or argument kept, in characters.</summary>
    public const int MaxTextLength = 200;

    /// <summary>The most arguments kept.</summary>
    public const int MaxArguments = 4;

    /// <summary>Where the entry came from.</summary>
    public NotificationSource Source { get; }

    /// <summary>What happened, as a stable code the feed resolves into a sentence.</summary>
    public string Code { get; }

    /// <summary>How urgent it is.</summary>
    public NotificationSeverity Severity { get; }

    /// <summary>The thing it is about, cleaned and capped.</summary>
    public string Subject { get; }

    /// <summary>Up to <see cref="MaxArguments"/> cleaned, capped values for the sentence's placeholders.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>A name from <see cref="NotificationDeepLinks"/>, or <c>null</c> for no destination.</summary>
    public string? DeepLink { get; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Creates the message, cleaning and capping its text.</summary>
    /// <param name="source">Where the entry came from.</param>
    /// <param name="code">The stable code for what happened.</param>
    /// <param name="severity">How urgent it is.</param>
    /// <param name="subject">The thing it is about.</param>
    /// <param name="arguments">Values for the sentence's placeholders; extras beyond the fourth are dropped.</param>
    /// <param name="deepLink">A name from <see cref="NotificationDeepLinks"/>, or <c>null</c>.</param>
    /// <param name="occurredAt">When it happened.</param>
    public PanelNotificationRaised(
        NotificationSource source,
        string code,
        NotificationSeverity severity,
        string subject,
        IReadOnlyList<string> arguments,
        string? deepLink,
        DateTimeOffset occurredAt)
    {
        Source = source;
        Code = code;
        Severity = severity;
        Subject = NotificationText.Clean(subject, MaxTextLength);
        Arguments = arguments.Take(MaxArguments).Select(argument =>
        {
            return NotificationText.Clean(argument, MaxTextLength);
        }).ToList();
        DeepLink = deepLink;
        OccurredAt = occurredAt;
    }
}
