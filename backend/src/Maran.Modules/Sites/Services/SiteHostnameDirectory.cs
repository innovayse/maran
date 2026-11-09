using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// This module's answer to <see cref="ISiteHostnameDirectory"/>: whether a site of one account serves a given
/// host name, as its primary domain or as an alias, and nothing more about it.
/// </summary>
/// <remarks>
/// <para>
/// The read runs through <see cref="SitesDbContext"/>'s tenant filter AND names the account in its own
/// predicate. The filter alone would not do: an administrator's context shows every account's names, so
/// without the predicate "does account A claim this name" would answer yes for a name account B holds, and
/// the DNS module would let A create a zone for it. With both, a customer's context can only ever answer for
/// the account it acts for, and an administrator's answers for exactly the account named.
/// </para>
/// <para>
/// The answer is a boolean. A name another account claims and a name nobody claims are both <c>false</c>, so
/// the window cannot be used to learn which domains are hosted here by somebody else.
/// </para>
/// </remarks>
public sealed class SiteHostnameDirectory : ISiteHostnameDirectory
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the directory over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SiteHostnameDirectory(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> AccountClaimsHostnameAsync(
        Guid accountId,
        string hostname,
        CancellationToken cancellationToken)
    {
        var name = hostname.Trim().ToLowerInvariant();

        return await _dbContext.SiteHostnames
            .AsNoTracking()
            .AnyAsync(row => row.Name == name && row.Site.AccountId == accountId, cancellationToken);
    }
}
