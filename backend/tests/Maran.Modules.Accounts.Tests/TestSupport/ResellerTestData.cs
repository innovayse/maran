using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// Named builders for the reseller values the tests state, each defaulting to a figure that is
/// generous enough that a test only has to name the one figure it is about.
/// </summary>
public static class ResellerTestData
{
    /// <summary>The instant every reseller built here was created.</summary>
    public static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds an envelope; every figure not named is generous.</summary>
    /// <param name="maxAccounts">The most accounts the reseller may own.</param>
    /// <param name="diskPoolMb">The disk pool, in megabytes.</param>
    /// <param name="maxPlans">The most plans the reseller may own.</param>
    /// <param name="maxDiskQuotaMb">The highest disk quota one plan may state.</param>
    /// <param name="maxSites">The highest site count one plan may state.</param>
    /// <param name="maxDatabases">The highest database count one plan may state.</param>
    /// <param name="maxSftpUsers">The highest SFTP login count one plan may state.</param>
    /// <param name="maxFtpUsers">The highest FTPS login count one plan may state.</param>
    /// <param name="maxCronEntries">The highest cron entry count one plan may state.</param>
    /// <param name="maxPhpWorkersPerPool">The highest php-fpm worker count per pool one plan may state.</param>
    /// <param name="features">The features the reseller may sell.</param>
    /// <param name="runtimeLimits">The runtime ceilings; none (unlimited) when not stated.</param>
    /// <returns>The envelope.</returns>
    public static ResellerEnvelope Envelope(
        int maxAccounts = 10,
        long diskPoolMb = 100_000,
        int maxPlans = 10,
        int maxDiskQuotaMb = 10_000,
        int maxSites = 100,
        int maxDatabases = 100,
        int maxSftpUsers = 100,
        int maxFtpUsers = 100,
        int maxCronEntries = 100,
        int maxPhpWorkersPerPool = 50,
        AccountFeature features = AccountFeature.All,
        RuntimeLimits? runtimeLimits = null)
    {
        return ResellerEnvelope.Of(
            maxAccounts,
            diskPoolMb,
            maxPlans,
            maxDiskQuotaMb,
            maxSites,
            maxDatabases,
            maxSftpUsers,
            maxFtpUsers,
            maxCronEntries,
            maxPhpWorkersPerPool,
            features,
            runtimeLimits ?? RuntimeLimits.Unlimited());
    }

    /// <summary>Builds the figures of a plan; every figure not named is small.</summary>
    /// <param name="diskQuotaMb">The disk quota, in megabytes.</param>
    /// <param name="maxSites">The site count.</param>
    /// <param name="maxDatabases">The database count.</param>
    /// <param name="maxSftpUsers">The SFTP login count.</param>
    /// <param name="maxFtpUsers">The FTPS login count.</param>
    /// <param name="maxCronEntries">The cron entry count.</param>
    /// <param name="maxPhpWorkersPerPool">The php-fpm worker count per pool.</param>
    /// <param name="features">The features the plan sells.</param>
    /// <param name="runtimeLimits">The runtime ceilings; none (unlimited) when not stated.</param>
    /// <returns>The figures.</returns>
    public static PlanLimits Limits(
        int diskQuotaMb = 1_000,
        int maxSites = 5,
        int maxDatabases = 5,
        int maxSftpUsers = 5,
        int maxFtpUsers = 5,
        int maxCronEntries = 5,
        int maxPhpWorkersPerPool = 5,
        AccountFeature features = AccountFeature.None,
        RuntimeLimits? runtimeLimits = null)
    {
        return new PlanLimits(
            diskQuotaMb,
            maxSites,
            maxDatabases,
            maxSftpUsers,
            maxFtpUsers,
            maxCronEntries,
            maxPhpWorkersPerPool,
            features,
            runtimeLimits ?? RuntimeLimits.Unlimited());
    }

    /// <summary>Builds an active reseller.</summary>
    /// <param name="name">The reseller's name.</param>
    /// <param name="envelope">What it was sold; a generous one when not stated.</param>
    /// <param name="id">Its identity; a new one when not stated.</param>
    /// <returns>The reseller.</returns>
    public static Reseller Reseller(string name = "acme", ResellerEnvelope? envelope = null, Guid? id = null)
    {
        return new Reseller(
            id ?? Guid.NewGuid(),
            name,
            $"{name}@example.com",
            "en",
            envelope ?? Envelope(),
            CreatedAt);
    }
}
