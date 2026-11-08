namespace Maran.Agent.Client.Services.MailService;

/// <summary>What the agent could observe about one mail daemon.</summary>
/// <param name="Daemon">Which daemon this describes.</param>
/// <param name="Installed">Whether the daemon's package is installed.</param>
/// <param name="Version">
/// The daemon's version as it reports it, for display only; empty when not observed. Never compared as
/// a string.
/// </param>
/// <param name="Active">Whether the service manager reports the daemon's unit active.</param>
/// <param name="Listeners">
/// The TCP addresses the daemon listens on right now, read from the kernel. Empty means it listens on
/// none OR that listeners were not read: read <paramref name="ListenersObserved"/> to tell which.
/// </param>
/// <param name="ListenersObserved">
/// Whether <paramref name="Listeners"/> was actually read. <see cref="AgentMailObservation.No"/> or
/// <see cref="AgentMailObservation.NotObserved"/> with an empty list never means "listens on nothing".
/// </param>
public sealed record AgentMailDaemonStatus(
    AgentMailDaemon Daemon,
    AgentMailObservation Installed,
    string Version,
    AgentMailObservation Active,
    IReadOnlyList<AgentMailListener> Listeners,
    AgentMailObservation ListenersObserved);
