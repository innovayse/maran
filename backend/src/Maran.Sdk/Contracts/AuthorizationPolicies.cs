namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the authorization policies the Host registers, so a module can gate an endpoint with
/// <c>[Authorize(Policy = …)]</c>.
/// </summary>
/// <remarks>
/// The names live in the Sdk for the same reason the rate-limit policy names do: both sides need
/// them and only one side may depend on the other. The Host defines what each policy requires, a
/// module says which one applies, and a module can never reference the Host. Spelling the name as
/// a string literal in a module would make a typo an endpoint with an unknown policy — which fails
/// the request rather than opening it, but fails it in a way nobody can read.
///
/// Name one on every endpoint. An endpoint that names nothing is evaluated against the Host's
/// fallback, which is <see cref="AdminOnly"/>: a customer-facing endpoint that forgets its attribute
/// refuses its customers with 403. A bare <c>[Authorize]</c> evaluates the default instead, which is
/// <see cref="AnyAuthenticated"/> — so the bare form is customer-level, and writing it on an
/// administrator endpoint opens that endpoint to every customer.
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires a signed-in administrator who has completed any forced two-factor enrolment. Also what
    /// an endpoint that carries no authorization metadata is held to.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Requires a signed-in caller of either role who has completed any forced two-factor enrolment; a
    /// customer passes it. Also what a bare <c>[Authorize]</c> is held to.
    /// </summary>
    public const string AnyAuthenticated = "AnyAuthenticated";
}
