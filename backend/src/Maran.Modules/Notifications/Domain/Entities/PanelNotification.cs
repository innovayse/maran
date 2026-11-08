using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Notifications.Domain.Entities;

/// <summary>
/// One entry in the administrators' notification feed: something the panel wants an operator to
/// know, stored as a code and a few words and resolved into a sentence only when it is read.
/// </summary>
/// <remarks>
/// <para>
/// <b>No account id, on purpose.</b> The feed is the operator's, not a customer's. A column named
/// <c>AccountId</c> would pull this table into the tenant census and make an account undeletable
/// while rows named it (<c>ModuleAccountResidueAuditor</c>). The account a task concerns is text in
/// <see cref="Subject"/>, and the row leaves the table by retention (<c>NotificationRetentionHandler</c>)
/// rather than by an account's deletion.
/// </para>
/// <para>
/// <b>A code and arguments, never a sentence.</b> The sentence is resolved per reader, in the
/// reader's language, by <c>NotificationTextResolver</c>; storing it would freeze it in the language
/// of whoever happened to be signed in when the row was written (the rule rules/csharp.md gives for
/// a stored failure).
/// </para>
/// <para>
/// <b>Immutable once written.</b> A notification is an event that happened. Whether one person has
/// dismissed it is a separate row, <see cref="NotificationDismissal"/>, because dismissal is
/// per administrator and a flag on this row could only mean "dismissed by somebody".
/// </para>
/// <para>
/// <b>The deep link is a name from a closed set.</b> A name that is not in
/// <see cref="NotificationDeepLinks"/> is stored as no link, rather than refused: the notification is
/// still worth reading, and a link that was wrong is not worth losing the entry for.
/// </para>
/// </remarks>
public sealed class PanelNotification
{
    /// <summary>The width of <see cref="Code"/>: a machine-stable identifier, never a sentence.</summary>
    public const int CodeMaxLength = 100;

    /// <summary>The width of <see cref="Subject"/>: what the notification is about, as one short line.</summary>
    public const int SubjectMaxLength = 200;

    /// <summary>The width of <see cref="DeepLink"/>: the longest route name in <see cref="NotificationDeepLinks"/> with room.</summary>
    public const int DeepLinkMaxLength = 64;

    /// <summary>The row's identity, and the only identifier a request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>Which part of the panel raised it.</summary>
    public NotificationSource Source { get; private set; }

    /// <summary>The machine-stable code naming what happened; with <see cref="Source"/> and <see cref="Severity"/> it picks the sentence.</summary>
    public string Code { get; private set; }

    /// <summary>How much it matters.</summary>
    public NotificationSeverity Severity { get; private set; }

    /// <summary>What it is about — a task's subject, an alert's subject — as one short line of plain text.</summary>
    public string Subject { get; private set; }

    /// <summary>The few words the sentence is built around.</summary>
    public PanelNotificationArguments Arguments { get; private set; }

    /// <summary>The route name the entry points at, from <see cref="NotificationDeepLinks"/>, or <c>null</c>.</summary>
    public string? DeepLink { get; private set; }

    /// <summary>When the thing happened (not when the row was written).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Creates a notification, bounding every text to what its column holds.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="source">Which part of the panel raised it.</param>
    /// <param name="code">What happened, as a machine-stable identifier.</param>
    /// <param name="severity">How much it matters.</param>
    /// <param name="subject">What it is about; control characters are removed and the line is cut to fit.</param>
    /// <param name="arguments">The words the sentence is built around.</param>
    /// <param name="deepLink">A route name from <see cref="NotificationDeepLinks"/>; anything else is stored as none.</param>
    /// <param name="occurredAt">When it happened.</param>
    public PanelNotification(
        Guid id,
        NotificationSource source,
        string code,
        NotificationSeverity severity,
        string subject,
        PanelNotificationArguments arguments,
        string? deepLink,
        DateTimeOffset occurredAt)
    {
        Id = id;
        Source = source;
        Code = ColumnText.Fit(code, CodeMaxLength);
        Severity = severity;
        Subject = NotificationText.Clean(subject, SubjectMaxLength);
        Arguments = arguments;
        DeepLink = NotificationDeepLinks.IsKnown(deepLink) ? deepLink : null;
        OccurredAt = occurredAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PanelNotification()
    {
        Code = string.Empty;
        Subject = string.Empty;
        Arguments = PanelNotificationArguments.None;
    }
}
