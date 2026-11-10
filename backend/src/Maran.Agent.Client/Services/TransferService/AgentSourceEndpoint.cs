namespace Maran.Agent.Client.Services.TransferService;

/// <summary>How to reach one registered source: the three things an administrator typed.</summary>
/// <remarks>
/// Nothing here is trusted. The agent revalidates all three, refuses a loopback, link-local,
/// unspecified, multicast or own-address host, and connects to one resolved address only.
/// </remarks>
/// <param name="Host">DNS name or IP address of the source.</param>
/// <param name="Port">TCP port of the source's SSH daemon, 1 to 65535. The agent refuses any other value.</param>
/// <param name="User">The login on the source.</param>
public sealed record AgentSourceEndpoint(string Host, int Port, string User);
