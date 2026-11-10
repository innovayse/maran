using Maran.Agent.Client.Services.DnsService;

namespace Maran.Modules.Dns.Common;

/// <summary>
/// What the name server on this host is actually doing, read from the agent on every call, next to what
/// the administrator asked for.
/// </summary>
/// <param name="Enabled">What was asked: the stored intention.</param>
/// <param name="Running">Whether the daemon's process is up, as the agent observed it.</param>
/// <param name="Listening">Whether the name server's port is bound, as the agent observed it.</param>
/// <param name="Port">
/// The port the name server answers on, for TCP and UDP alike. Turning the server on does not open it in the
/// firewall, so this is the number a screen asks the administrator to open there.
/// </param>
/// <param name="Version">
/// The daemon's version when it is known, else an empty string: the agent does not report one, so it is empty
/// today and a screen must treat empty as unknown.
/// </param>
/// <param name="ZoneCount">How many zones the daemon serves, from the agent's list; zero when it was not asked.</param>
/// <param name="RecursionRefused">Whether the running daemon was seen to refuse recursion, as the agent observed it.</param>
/// <param name="ResponseRateLimiting">Whether the running daemon was seen to rate-limit responses, as the agent observed it.</param>
/// <param name="AgentReachable">
/// False when the agent could not be asked, in which case the observed fields carry nothing and a screen
/// must not read them as "stopped".
/// </param>
public sealed record DnsStatusDto(
    bool Enabled,
    bool Running,
    bool Listening,
    string Version,
    int ZoneCount,
    bool AgentReachable,
    int Port,
    DnsPolicyState RecursionRefused = DnsPolicyState.NotObserved,
    DnsPolicyState ResponseRateLimiting = DnsPolicyState.NotObserved);
