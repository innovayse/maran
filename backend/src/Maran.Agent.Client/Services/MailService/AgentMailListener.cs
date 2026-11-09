namespace Maran.Agent.Client.Services.MailService;

/// <summary>One TCP address a mail daemon accepts connections on, read from the kernel.</summary>
/// <param name="Address">The bound address as the kernel reports it: an IPv4 or IPv6 literal, or <c>*</c> for every address.</param>
/// <param name="Port">The TCP port.</param>
public sealed record AgentMailListener(string Address, uint Port);
