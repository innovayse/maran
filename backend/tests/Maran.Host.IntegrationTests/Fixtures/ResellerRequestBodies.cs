namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The request bodies the reseller endpoint tests post: a plan, an account and a reseller, each with
/// every figure at a value inside <see cref="ResellerEnvelopeSpec"/>'s defaults, so a test changes only
/// the one figure it is about.
/// </summary>
public static class ResellerRequestBodies
{
    /// <summary>The body of a plan that sits inside the default envelope, one figure at a time overridable.</summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="diskQuotaMb">The disk quota, in MB.</param>
    /// <param name="maxSites">How many sites the plan allows.</param>
    /// <param name="maxDatabases">How many databases the plan allows.</param>
    /// <param name="maxSftpUsers">How many SFTP logins the plan allows.</param>
    /// <param name="maxFtpUsers">How many FTPS logins the plan allows.</param>
    /// <param name="maxCronEntries">How many cron entries the plan allows.</param>
    /// <param name="maxPhpWorkersPerPool">How many PHP workers per pool the plan allows.</param>
    /// <param name="cpuQuotaPercent">The CPU ceiling, or <c>null</c> for a plan that states none.</param>
    /// <param name="maxProcesses">The process ceiling, or <c>null</c> for a plan that states none.</param>
    /// <param name="features">The wire names of the features the plan includes; the envelope's own when <c>null</c>.</param>
    /// <returns>An object the JSON serializer writes as a create or update plan request.</returns>
    public static object Plan(
        string name,
        int diskQuotaMb = 5_120,
        int maxSites = 5,
        int maxDatabases = 2,
        int maxSftpUsers = 3,
        int maxFtpUsers = 1,
        int maxCronEntries = 2,
        int maxPhpWorkersPerPool = 4,
        int? cpuQuotaPercent = 100,
        int? maxProcesses = 64,
        string[]? features = null)
    {
        return new
        {
            Name = name,
            DiskQuotaMb = diskQuotaMb,
            MaxSites = maxSites,
            MaxDatabases = maxDatabases,
            MaxSftpUsers = maxSftpUsers,
            MaxFtpUsers = maxFtpUsers,
            MaxCronEntries = maxCronEntries,
            MaxPhpWorkersPerPool = maxPhpWorkersPerPool,
            CpuQuotaPercent = Ceiling(cpuQuotaPercent),
            MemoryMaxMb = new { Kind = "limited", Maximum = 512 },
            MaxProcesses = Ceiling(maxProcesses),
            Features = features ?? ResellerEnvelopeSpec.SoldFeatureNames,
        };
    }

    /// <summary>The body of an account creation, whose owner address is derived from its name so it is unique.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="planId">The plan to put the account on.</param>
    /// <param name="resellerId">The owner an administrator names, or <c>null</c> to leave it to the server.</param>
    /// <returns>An object the JSON serializer writes as a create account request.</returns>
    public static object Account(string name, Guid planId, Guid? resellerId = null)
    {
        return new
        {
            Name = name,
            PrimaryDomain = $"{name}.example.com",
            PlanId = planId,
            OwnerEmail = $"{name}@example.com",
            ResellerId = resellerId,
        };
    }

    /// <summary>The body of a reseller creation.</summary>
    /// <param name="name">The reseller's name, which is also its login name.</param>
    /// <param name="language">The contact's language.</param>
    /// <param name="envelope">What the reseller is sold, or the default envelope when <c>null</c>.</param>
    /// <returns>An object the JSON serializer writes as a create reseller request.</returns>
    public static object Reseller(string name, string language = "en", ResellerEnvelopeSpec? envelope = null)
    {
        return new
        {
            Name = name,
            ContactEmail = $"{name}@example.com",
            Language = language,
            Envelope = (envelope ?? new ResellerEnvelopeSpec()).ToBody(),
        };
    }

    /// <summary>A runtime ceiling as the wire writes it: limited at a figure, or unlimited.</summary>
    /// <param name="maximum">The figure, or <c>null</c> for no cap.</param>
    /// <returns>An object the JSON serializer writes as a resource ceiling.</returns>
    private static object Ceiling(int? maximum)
    {
        if (maximum is { } figure)
        {
            return new { Kind = "limited", Maximum = figure };
        }

        return new { Kind = "unlimited" };
    }
}
