namespace Maran.Agent.Client.Services.SitesService;

/// <summary>A site's request limit, as the agent is told to render it.</summary>
/// <param name="PerSecond">How many requests per second one visitor address may make, 1 to 1000. The agent refuses any other value.</param>
/// <param name="Burst">How many requests above the rate one address may send at once, 0 to 1000. The agent refuses any other value.</param>
public sealed record RequestRateDescriptor(uint PerSecond, uint Burst);
