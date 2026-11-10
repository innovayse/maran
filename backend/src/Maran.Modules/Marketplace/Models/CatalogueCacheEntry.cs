namespace Maran.Modules.Marketplace.Models;

/// <summary>A verified catalogue page held in memory, with the validator the next request may present.</summary>
/// <param name="Page">The verified page.</param>
/// <param name="ETag">The ETag the service sent with it, or null.</param>
/// <param name="StoredAt">When the entry was stored or last revalidated; the time-to-live counts from here.</param>
public sealed record CatalogueCacheEntry(CataloguePage Page, string? ETag, DateTimeOffset StoredAt);
