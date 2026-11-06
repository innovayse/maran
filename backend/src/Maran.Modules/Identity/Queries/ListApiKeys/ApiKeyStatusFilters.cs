namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>
/// The values the key list's <c>status</c> filter accepts — the lower-case spellings of
/// <c>ApiKeyStatus</c>. Written once and read by <see cref="ListApiKeysQueryValidator"/>,
/// <see cref="ListApiKeysQueryHandler"/> and the tests, so the validator and the handler cannot
/// disagree about what a value means.
/// </summary>
public static class ApiKeyStatusFilters
{
    /// <summary>Keys that are not revoked and have not expired.</summary>
    public const string Active = "active";

    /// <summary>Keys an administrator revoked, whether or not they had also expired.</summary>
    public const string Revoked = "revoked";

    /// <summary>Keys that are not revoked and whose expiry has passed.</summary>
    public const string Expired = "expired";

    /// <summary>Every value the filter accepts.</summary>
    public static readonly IReadOnlyList<string> All = [Active, Revoked, Expired];
}
