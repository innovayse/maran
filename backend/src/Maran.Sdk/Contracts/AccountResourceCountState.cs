namespace Maran.Sdk.Contracts;

/// <summary>
/// Whether a module could count what an account owns. Stated as a value of its own so "could not
/// count" is never read off a sentinel number: zero is a real answer, and an unreachable agent is not
/// zero cron entries.
/// </summary>
public enum AccountResourceCountState
{
    /// <summary>The module counted the account's resources, and the count is the answer.</summary>
    Counted,

    /// <summary>
    /// The module could not count them — the agent holding the truth did not answer — so there is no
    /// number to show.
    /// </summary>
    Unavailable,
}
