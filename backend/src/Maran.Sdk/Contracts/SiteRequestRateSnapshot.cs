namespace Maran.Sdk.Contracts;

/// <summary>A site's request limit, as one module reports it to another.</summary>
/// <param name="PerSecond">How many requests per second one visitor address may make, 1 to 1000.</param>
/// <param name="Burst">How many requests above the rate one address may send at once, 0 to 1000.</param>
public sealed record SiteRequestRateSnapshot(int PerSecond, int Burst);
