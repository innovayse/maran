namespace Maran.Modules.Marketplace.Interfaces;

/// <summary>
/// The marketplace's on/off switch as the client sees it: one question, answered from the stored
/// administrator decision. It is a seam of its own so the client never reads the settings table itself
/// and a test supplies either answer.
/// </summary>
public interface IMarketplaceSwitch
{
    /// <summary>Whether an administrator has switched the marketplace on. Off until someone does.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True only when the stored decision says on.</returns>
    Task<bool> IsEnabledAsync(CancellationToken ct);
}
