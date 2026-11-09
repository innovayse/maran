namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers one question about the Sites module's rows for a module that may not read them: does a given
/// account's site claim a given host name. The contract lives in the Sdk and its implementation in the
/// module that owns the sites table, the same shape <see cref="ISiteDirectory"/> established
/// (rules/architecture.md "A shared facility's contract lives in Maran.Sdk").
/// </summary>
/// <remarks>
/// It is a window of its own and not a member of <see cref="ISiteDirectory"/> because that interface has
/// many implementers and doubles, and a question only the DNS module asks should not widen all of them. It
/// returns a bare boolean, the narrowest value that answers the question: the DNS module needs to know
/// whether the host name is the account's, never the site row it came from.
/// </remarks>
public interface ISiteHostnameDirectory
{
    /// <summary>
    /// Tells whether a site of <paramref name="accountId"/> serves <paramref name="hostname"/>, as its
    /// primary domain or as an alias.
    /// </summary>
    /// <param name="accountId">The account whose sites are asked.</param>
    /// <param name="hostname">The host name, compared case-insensitively.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <c>true</c> only when the account owns a site claiming exactly that host name. A host name another
    /// account claims, and one nobody claims, both answer <c>false</c>: telling them apart would let a
    /// caller confirm which domains are hosted here by somebody else.
    /// </returns>
    Task<bool> AccountClaimsHostnameAsync(Guid accountId, string hostname, CancellationToken cancellationToken);
}
