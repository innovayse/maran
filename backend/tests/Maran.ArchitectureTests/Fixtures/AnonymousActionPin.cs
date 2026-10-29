namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The complete list of controller actions that are open to a caller who is not signed in.
/// </summary>
/// <remarks>
/// <para>
/// Anonymity is the one declaration that WIDENS who may call an endpoint, so it is a decision with a
/// written reason, reviewed in a diff of this file rather than found by grep. A new
/// <c>[AllowAnonymous]</c> that is not listed here fails
/// <see cref="ControllerAuthorizationDeclarationTests"/>; an entry whose action no longer exists or
/// is no longer anonymous fails it too, so the list cannot rot into a place where a stale decision
/// hides.
/// </para>
/// <para>
/// Host-level endpoints (<c>/health</c>, <c>/api/v1/modules</c>) are minimal endpoints, not
/// controller actions, and are not reachable by reflection over controllers; the route-table census
/// in the Host integration tests pins those. That route-table list, <c>AnonymousEndpointPin</c>,
/// overlaps this one on purpose: it is the route view of the same decision, and each fails on its own
/// drift.
/// </para>
/// </remarks>
public static class AnonymousActionPin
{
    /// <summary>Every anonymous controller action of the composed panel, with its reason.</summary>
    public static IReadOnlyList<PinnedAnonymousAction> All { get; } = new List<PinnedAnonymousAction>
    {
        new("AuthController.LoginAsync", "Sign-in: the caller has no credential yet."),
        new("AuthController.RefreshAsync", "Exchanges the refresh cookie; the access token has expired by definition."),
        new("AuthController.LogoutAsync", "Clears the cookie of a caller whose access token may already have expired."),
        new("AuthController.VerifyTwoFactorAsync", "Second step of sign-in: re-checks the pending-login token itself."),
        new("AuthController.RequestPasswordResetAsync", "The caller has forgotten their password."),
        new("AuthController.ResetPasswordAsync", "Authenticated by the mailed single-use reset token."),
        new("AuthController.AcceptInvitationAsync", "Authenticated by the mailed single-use invitation token."),
        new("SetupController.GetStateAsync", "First-run: reports whether an administrator exists; nobody can sign in yet."),
        new("SetupController.CompleteAsync", "First-run: creates the first administrator; refuses once any user exists."),
    };
}
