namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The complete list of routes the composed panel serves to a caller who is not signed in.
/// </summary>
/// <remarks>
/// Anonymity is the one declaration that WIDENS who may call an endpoint, so it is a decision with a
/// written reason, reviewed in a diff of this file rather than found by search. A route that turns
/// anonymous without being listed here fails <c>EndpointAuthorizationPolicyTests</c>; an entry
/// whose route no longer exists, or is no longer anonymous, fails it too, so the list cannot rot
/// into a place where a stale decision hides. The controller-action view of the same decision is
/// <c>AnonymousActionPin</c> in the architecture tests; the two lists overlap on purpose (routes here,
/// reflected actions there, plus the minimal endpoints only this one can see) and each test class
/// fails when its own list drifts from the code.
/// </remarks>
public static class AnonymousEndpointPin
{
    /// <summary>Every anonymous route of the composed panel, with its reason.</summary>
    public static IReadOnlyList<PinnedAnonymousEndpoint> All { get; } = new List<PinnedAnonymousEndpoint>
    {
        new("GET /health", "Liveness and readiness summary for the operator's monitoring; carries no tenant data."),
        new("GET /health/live", "Process liveness for a supervisor that holds no credential."),
        new("GET /health/ready", "Readiness for a supervisor that holds no credential."),
        new("GET /api/v1/modules", "Describes what this build composed; the sign-in screen needs it before anyone is signed in."),
        new("GET /api/v1/setup/state", "First run: reports whether an administrator exists; nobody can sign in yet."),
        new("POST /api/v1/setup", "First run: creates the first administrator; refuses once any user exists."),
        new("POST /api/v1/auth/login", "Sign-in: the caller has no credential yet."),
        new("POST /api/v1/auth/refresh", "Exchanges the refresh cookie; the access token has expired by definition."),
        new("POST /api/v1/auth/logout", "Clears the cookie of a caller whose access token may already have expired."),
        new("POST /api/v1/auth/two-factor", "Second step of sign-in: re-checks the pending-login token itself."),
        new("POST /api/v1/auth/forgot-password", "The caller has forgotten their password."),
        new("POST /api/v1/auth/reset-password", "Authenticated by the mailed single-use reset token."),
        new("POST /api/v1/auth/accept-invitation", "Authenticated by the mailed single-use invitation token."),
    };
}
