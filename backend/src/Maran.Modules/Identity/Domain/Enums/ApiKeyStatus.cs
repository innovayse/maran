namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// Where an API key stands at a given instant. Derived from the key's revocation and expiry instants
/// by <see cref="Entities.ApiKey.StatusAt"/> and never stored: a stored status would have to be kept in
/// step with the clock by something, and a key whose expiry passed overnight would read as active
/// until it was.
/// </summary>
public enum ApiKeyStatus
{
    /// <summary>Neither revoked nor expired: the key authenticates.</summary>
    Active,

    /// <summary>An administrator revoked the key. Wins over expiry when both hold.</summary>
    Revoked,

    /// <summary>The key's expiry instant has passed and it was never revoked.</summary>
    Expired,
}
