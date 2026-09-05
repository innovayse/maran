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
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>Requires an authenticated administrator.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Requires only that the caller is signed in.</summary>
    public const string AnyAuthenticated = "AnyAuthenticated";
}
