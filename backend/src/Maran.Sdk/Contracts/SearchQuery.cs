namespace Maran.Sdk.Contracts;

/// <summary>
/// What a search provider is asked: the term and how many hits it may return.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no account id, no role and no principal, and that is the point.</b> A provider scopes
/// itself through its module's filtered <c>DbContext</c> (rules/security.md item 6), so the contract has
/// no scope value that a provider could mishandle or a caller could forge.
/// </para>
/// <para>
/// <b>The term is already validated</b> by the Host: trimmed, free of control characters, between
/// <see cref="MinimumTermLength"/> and <c>PageDefaults.SearchMaxLength</c> characters. A provider still
/// escapes it with <c>LikePattern</c> before it reaches a pattern.
/// </para>
/// </remarks>
/// <param name="Term">The trimmed search text.</param>
public sealed record SearchQuery(string Term)
{
    /// <summary>The shortest term the endpoint accepts; a single letter matches too much to be a search.</summary>
    public const int MinimumTermLength = 2;

    /// <summary>The most hits one provider may return for one query.</summary>
    public const int HitLimit = 8;

    /// <summary>The most candidate rows a provider may read before ranking them.</summary>
    public const int CandidateLimit = 100;
}
