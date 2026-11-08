namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>
/// How fast one visitor address may request a site: a steady rate and the burst it may exceed it by.
/// </summary>
/// <remarks>
/// A site that is not limited has no <see cref="SiteRequestRate"/> at all (null), never a rate of zero:
/// one field, one meaning. The bounds are <c>SiteOptionsLimits</c>'; this type is the stored shape and
/// does not repeat them, because the entity and the validator are the two places that enforce them.
/// </remarks>
/// <param name="PerSecond">How many requests per second one visitor address may make.</param>
/// <param name="Burst">How many requests above the rate one address may send at once before being refused.</param>
public sealed record SiteRequestRate(int PerSecond, int Burst);
