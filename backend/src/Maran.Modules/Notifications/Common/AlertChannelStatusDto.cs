namespace Maran.Modules.Notifications.Common;

/// <summary>How the last operator alerts fared on one channel.</summary>
/// <param name="State">
/// <c>ok</c> when the latest outcome was a delivery, <c>failing</c> when it was a failure, and
/// <c>unknown</c> when no alert has been sent on the channel yet.
/// </param>
/// <param name="LastFailureAt">When the latest alert failed; <c>null</c> unless <paramref name="State"/> is <c>failing</c>.</param>
/// <param name="FailureMessage">
/// Why it failed, in the reader's language, resolved from the stored code at read time; <c>null</c>
/// unless <paramref name="State"/> is <c>failing</c>. Never the mail server's own words.
/// </param>
public sealed record AlertChannelStatusDto(string State, DateTimeOffset? LastFailureAt, string? FailureMessage);
