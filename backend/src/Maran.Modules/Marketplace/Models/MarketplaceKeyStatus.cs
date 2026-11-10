namespace Maran.Modules.Marketplace.Models;

/// <summary>How many keys the panel trusts to sign marketplace answers, and where they came from.</summary>
/// <param name="PinnedKeyCount">Keys read from the pinned key file shipped with the panel.</param>
/// <param name="RotatedKeyCount">Keys accepted since start because a trusted key signed their announcement; forgotten at restart.</param>
public sealed record MarketplaceKeyStatus(int PinnedKeyCount, int RotatedKeyCount);
