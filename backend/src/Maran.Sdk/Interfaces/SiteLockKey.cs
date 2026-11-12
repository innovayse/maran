namespace Maran.Sdk.Interfaces;

/// <summary>
/// What an <see cref="ISiteOperationLock"/> is taken on: one site's host state, or the php-fpm pools of one
/// account. A key carries which of the two it names, so an account id can never be mistaken for a site id.
/// </summary>
/// <remarks>
/// A pool belongs to an account and is shared by every site of the account on that version, so a question
/// about it ("is this the last site on version V") cannot be answered under any one site's lock. It gets its
/// own kind of key rather than a site id made up from the account id.
/// </remarks>
/// <param name="Scope">What the key names.</param>
/// <param name="Id">The site's id for <see cref="SiteLockScope.Site"/>, the account's for <see cref="SiteLockScope.PhpPools"/>.</param>
public readonly record struct SiteLockKey(SiteLockScope Scope, Guid Id)
{
    /// <summary>The key of one site's host state.</summary>
    /// <param name="siteId">The site.</param>
    /// <returns>The key.</returns>
    public static SiteLockKey ForSite(Guid siteId)
    {
        return new SiteLockKey(SiteLockScope.Site, siteId);
    }

    /// <summary>The key of one account's php-fpm pools.</summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The key.</returns>
    public static SiteLockKey ForPhpPools(Guid accountId)
    {
        return new SiteLockKey(SiteLockScope.PhpPools, accountId);
    }

    /// <summary>A bare id names a site, the common case.</summary>
    /// <param name="siteId">The site.</param>
    /// <returns>The site's key; see <see cref="ForSite"/>.</returns>
    public static implicit operator SiteLockKey(Guid siteId)
    {
        return ForSite(siteId);
    }
}
