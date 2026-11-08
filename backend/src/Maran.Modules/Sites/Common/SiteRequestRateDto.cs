namespace Maran.Modules.Sites.Common;

/// <summary>A site's request limit, as a request states it and an answer shows it.</summary>
/// <param name="PerSecond">How many requests per second one visitor address may make.</param>
/// <param name="Burst">How many requests above the rate one address may send at once before being refused.</param>
public sealed record SiteRequestRateDto(int PerSecond, int Burst);
