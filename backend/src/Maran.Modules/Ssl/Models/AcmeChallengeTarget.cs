namespace Maran.Modules.Ssl.Models;

/// <summary>
/// Where the file answering an HTTP-01 challenge is placed: one of exactly two places, each its own
/// type, so a caller states which rather than passing an account name that one case does not have.
/// </summary>
/// <remarks>
/// A site's challenge lives in the site's document root and is written as the owning account
/// (<see cref="SiteDocumentRootChallenge"/>); the panel's own challenge lives in the agent's
/// directory served by the panel's port-80 block and belongs to no account
/// (<see cref="PanelChallengeDirectory"/>, issue #132). The constructor is private to this assembly,
/// so those two are the whole set and a switch over them is exhaustive by construction.
/// </remarks>
public abstract record AcmeChallengeTarget
{
    /// <summary>Closes the hierarchy to the two targets this module declares.</summary>
    private protected AcmeChallengeTarget()
    {
    }
}
