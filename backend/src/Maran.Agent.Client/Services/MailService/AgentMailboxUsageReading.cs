namespace Maran.Agent.Client.Services.MailService;

/// <summary>A measurement of one mailbox.</summary>
/// <param name="BytesUsed">Bytes the mailbox stores.</param>
/// <param name="MessageCount">Messages the mailbox holds.</param>
public sealed record AgentMailboxUsageReading(ulong BytesUsed, ulong MessageCount);
