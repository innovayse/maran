using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Domain.Policies;

/// <summary>
/// Tells a catalogue that could not be TRUSTED from one that could not be REACHED. An answer that arrived
/// and failed its checks (unsigned, stale, unreadable, no key to check it with) is reported to the screen
/// as an unverified catalogue and journalled; every other failure is the request failing.
/// </summary>
public static class CatalogueVerificationPolicy
{
    /// <summary>Tells whether a failure of the marketplace client is a verification failure.</summary>
    /// <param name="error">The client's failure.</param>
    /// <returns>True when the answer arrived and could not be trusted.</returns>
    public static bool IsVerificationFailure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code is nameof(ErrorMessages.CatalogueUnsigned)
            or nameof(ErrorMessages.CatalogueStale)
            or nameof(ErrorMessages.CatalogueKeyMissing)
            or nameof(ErrorMessages.MarketplaceResponseInvalid);
    }
}
