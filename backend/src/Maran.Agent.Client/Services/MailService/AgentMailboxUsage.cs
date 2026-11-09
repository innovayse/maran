namespace Maran.Agent.Client.Services.MailService;

/// <summary>One mailbox of an account and, when it could be measured, its usage.</summary>
/// <param name="Domain">The mail domain of the mailbox.</param>
/// <param name="LocalPart">The local part of the mailbox.</param>
/// <param name="Reading">The measurement; <c>null</c> when the agent could not measure this mailbox. Null is not an empty mailbox.</param>
public sealed record AgentMailboxUsage(string Domain, string LocalPart, AgentMailboxUsageReading? Reading);
