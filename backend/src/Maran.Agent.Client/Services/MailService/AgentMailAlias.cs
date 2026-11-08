namespace Maran.Agent.Client.Services.MailService;

/// <summary>One alias of a domain: mail sent to <c>LocalPart@domain</c> goes to every target.</summary>
/// <param name="LocalPart">The local part the alias answers to; the same grammar as a mailbox's.</param>
/// <param name="Targets">Where the mail goes: one to twenty addresses; an address on another host is a forwarder.</param>
public sealed record AgentMailAlias(string LocalPart, IReadOnlyList<string> Targets);
