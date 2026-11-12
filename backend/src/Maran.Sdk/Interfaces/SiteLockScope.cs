namespace Maran.Sdk.Interfaces;

/// <summary>What a <see cref="SiteLockKey"/> names.</summary>
public enum SiteLockScope
{
    /// <summary>One site's vhost, pool, enabled flag and certificate.</summary>
    Site = 0,

    /// <summary>The php-fpm pools of one account, shared by its sites.</summary>
    PhpPools = 1,
}
