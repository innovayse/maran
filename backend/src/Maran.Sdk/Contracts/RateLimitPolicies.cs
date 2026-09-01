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

    /// <summary>
    /// The site-log stream limit: a CONCURRENCY limit on how many tails one account may hold
    /// open at once, which is a different question from how fast it may open them.
    /// </summary>
    /// <remarks>
    /// A fixed-window limiter cannot answer it. Its lease returns no permit when the request ends —
    /// permits come back on the window timer — so it bounds the RATE of opening and says nothing
    /// about how many are open. A concurrency limiter's lease IS returned on disposal, which is the
    /// whole reason this is a separate policy rather than a bigger number on <see cref="Api"/>.
    /// </remarks>
    public const string SiteLogs = "site-logs";
}
