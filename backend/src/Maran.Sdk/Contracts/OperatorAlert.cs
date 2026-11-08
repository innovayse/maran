namespace Maran.Sdk.Contracts;

/// <summary>
/// One alert to the server's operator, described without words: what it is, which thing it is about
/// and the figure that goes with it. Every output of the dispatcher (mail, webhook, the notification
/// feed) is built from this plus the localized text the caller composed.
/// </summary>
/// <remarks>
/// Language-neutral on purpose: a webhook receiver and a stored feed entry must not depend on the
/// language the raising code happened to run in.
/// </remarks>
/// <param name="Source">The module that raised it.</param>
/// <param name="Code">
/// The kind of alert, spelled as the raising module's own enum member name (<c>DiskUsage</c>,
/// <c>ScheduledRunFailed</c>). Stable: receivers and the feed branch on it.
/// </param>
/// <param name="Transition">Whether the condition began, ended or was a one-off event.</param>
/// <param name="Subject">The thing it is about: a mount, a service, an account name, a domain.</param>
/// <param name="Detail">
/// The figure or the closed words the mail body already uses. Never a path, a URL or tool output
/// (rules/security.md item 8); consumers cap its length.
/// </param>
public sealed record OperatorAlert(
    AlertSource Source,
    string Code,
    AlertTransition Transition,
    string Subject,
    string Detail);
