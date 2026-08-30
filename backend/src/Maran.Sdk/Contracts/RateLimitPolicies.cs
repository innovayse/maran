namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the rate-limit policies the Host registers, so a module can opt an endpoint into one
/// with <c>[EnableRateLimiting]</c>.
/// </summary>
/// <remarks>
/// The names live in the Sdk because both sides need them and only one side may depend on the
/// other: the Host defines what each policy does, a module says which one applies to its endpoint,
/// and a module can never reference the Host. Before this, the one module that needed a policy
/// spelled the name as a string literal — a typo would have silently applied no limit at all,
/// because an unknown policy name is a startup error only for endpoints, and a quiet one to miss.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>The general API limit, applied to ordinary panel endpoints.</summary>
    public const string Api = "api";

    /// <summary>The authentication limit: tighter, partitioned per address and username, with a lockout.</summary>
    public const string Login = "login";
}
