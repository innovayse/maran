namespace Maran.Modules.Notifications.Controllers.Requests;

/// <summary>The body of a request to send one test message.</summary>
/// <param name="Recipient">
/// Where to send it. Stated by the administrator rather than derived from their account: panel users
/// belong to the Identity module, which this one may not reference (rules/architecture.md), and
/// checking that mail reaches a particular mailbox is usually the point of a test anyway.
/// </param>
public sealed record SendTestMailRequest(string Recipient);
