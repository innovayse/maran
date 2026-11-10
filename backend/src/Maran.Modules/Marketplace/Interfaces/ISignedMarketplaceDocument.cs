namespace Maran.Modules.Marketplace.Interfaces;

/// <summary>
/// A marketplace answer the service signs and dates: it carries the moment it was signed and the moment
/// it stops being good, so the panel can refuse a replayed old catalogue even though its signature is
/// genuine.
/// </summary>
public interface ISignedMarketplaceDocument
{
    /// <summary>When the service signed this document.</summary>
    DateTimeOffset SignedAt { get; }

    /// <summary>The last moment the service stands behind this document.</summary>
    DateTimeOffset ValidUntil { get; }
}
