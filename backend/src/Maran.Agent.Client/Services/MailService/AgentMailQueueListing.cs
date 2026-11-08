namespace Maran.Agent.Client.Services.MailService;

/// <summary>A bounded listing of the host's mail queues.</summary>
/// <param name="Entries">The entries, newest first, at most the requested limit.</param>
/// <param name="TotalMessages">How many messages the queues hold in all.</param>
/// <param name="Truncated">True when <paramref name="Entries"/> stops short of <paramref name="TotalMessages"/>.</param>
public sealed record AgentMailQueueListing(
    IReadOnlyList<AgentMailQueueEntry> Entries,
    uint TotalMessages,
    bool Truncated);
