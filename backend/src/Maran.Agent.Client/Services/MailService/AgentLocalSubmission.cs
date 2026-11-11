namespace Maran.Agent.Client.Services.MailService;

/// <summary>
/// What binds mail an account submits locally (sendmail, PHP <c>mail()</c>, cron) to that account: the
/// domains it may send from, and the hourly limit it shares with its authenticated mailboxes.
/// </summary>
/// <remarks>
/// The lists REPLACE the account's whole binding on the host, so the caller always sends the complete sets
/// and never a delta. Nothing here names a path or a map file; the agent derives them.
/// </remarks>
/// <param name="AccountUsername">System username of the account.</param>
/// <param name="Domains">Every mail domain the account may send from; may be empty.</param>
/// <param name="SendLimitPerHour">Messages per hour for local and authenticated submission together; at least one.</param>
/// <param name="MailboxLogins">The full address of every mailbox of the account; may be empty.</param>
public sealed record AgentLocalSubmission(
    string AccountUsername,
    IReadOnlyList<string> Domains,
    uint SendLimitPerHour,
    IReadOnlyList<string> MailboxLogins);
