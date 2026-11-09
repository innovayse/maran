namespace Maran.Agent.Client.Services.MailService;

/// <summary>What one creating or updating of a mailbox changed on the host.</summary>
/// <param name="Created">True when the mailbox did not exist and was created.</param>
/// <param name="PasswordChanged">True when the stored password hash was replaced by this call.</param>
public sealed record AgentMailboxChange(bool Created, bool PasswordChanged);
