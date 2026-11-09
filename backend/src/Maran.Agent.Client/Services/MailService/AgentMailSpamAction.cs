namespace Maran.Agent.Client.Services.MailService;

/// <summary>What the spam filter does with a message it scores as spam, chosen per mailbox.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailSpamAction</c>. The wire's UNSPECIFIED ("the panel did not
/// choose") has no member: the panel always states the action, so a mailbox never silently inherits an
/// agent default.
/// </remarks>
public enum AgentMailSpamAction
{
    /// <summary>Deliver the message into the mailbox's Junk folder.</summary>
    FileToJunk = 1,

    /// <summary>Deliver into the inbox with the spam headers added, for a customer who sorts it themselves.</summary>
    KeepInInbox = 2,
}
