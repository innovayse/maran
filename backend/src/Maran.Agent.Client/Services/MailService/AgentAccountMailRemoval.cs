namespace Maran.Agent.Client.Services.MailService;

/// <summary>What the removal of everything mail of one account took off the host.</summary>
/// <param name="MailboxesRemoved">How many mailboxes were removed.</param>
/// <param name="DomainsRemoved">How many domains were withdrawn.</param>
public sealed record AgentAccountMailRemoval(uint MailboxesRemoved, uint DomainsRemoved);
