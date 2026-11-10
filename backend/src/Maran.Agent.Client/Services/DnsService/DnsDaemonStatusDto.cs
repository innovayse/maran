namespace Maran.Agent.Client.Services.DnsService;

/// <summary>What the agent observed of the name server, read from the daemon and never from a file the panel wrote.</summary>
/// <param name="Running">Whether the daemon's process is up.</param>
/// <param name="Listening">Whether port 53 actually answers.</param>
/// <param name="EnabledAtBoot">Whether the host will start the daemon at boot.</param>
/// <param name="RecursionRefused">Whether the running daemon was seen to refuse recursion for a foreign name.</param>
/// <param name="ResponseRateLimiting">Whether the running daemon was seen to have response rate limiting in force.</param>
/// <param name="DnssecSupported">
/// Whether the daemon build was seen to accept a signing policy, observed by the agent and never read off a
/// version table. False when it does not, and false when the agent could not tell: a screen hides signing on
/// anything but a positive answer.
/// </param>
public sealed record DnsDaemonStatusDto(
    bool Running,
    bool Listening,
    bool EnabledAtBoot,
    DnsPolicyState RecursionRefused,
    DnsPolicyState ResponseRateLimiting,
    bool DnssecSupported = false);
