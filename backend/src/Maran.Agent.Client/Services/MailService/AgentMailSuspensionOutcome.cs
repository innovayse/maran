namespace Maran.Agent.Client.Services.MailService;

/// <summary>What the suspending or resuming of an account's mail found and did on the host.</summary>
/// <param name="Mailboxes">How many mailboxes the account holds, found on the host and not from a list the panel supplied.</param>
/// <param name="SessionsEnded">How many open sessions were ended; always zero on a resume.</param>
public sealed record AgentMailSuspensionOutcome(uint Mailboxes, uint SessionsEnded);
