namespace Maran.Host.Demo.World;

/// <summary>
/// The single definition of what the public demonstration contains. The database seed is built from
/// it, and the verifier asserts the seeded database against it, so the two cannot disagree.
/// </summary>
/// <param name="FeaturedCustomerAccountName">
/// The account whose owner login is the published customer credential; one of <see cref="Accounts"/>.
/// </param>
/// <param name="Plans">Plans the administrator adds to the three standard ones.</param>
/// <param name="Resellers">Resellers, each with its own plans.</param>
/// <param name="Accounts">Every hosting account, reseller-owned ones included.</param>
/// <param name="Firewall">The firewall state.</param>
/// <param name="DiskAlertPercent">The disk alert threshold.</param>
/// <param name="CpuAlertPercent">The CPU alert threshold.</param>
/// <param name="MemoryAlertPercent">The memory alert threshold.</param>
public sealed record DemoWorld(
    string FeaturedCustomerAccountName,
    IReadOnlyList<DemoPlanDefinition> Plans,
    IReadOnlyList<DemoResellerDefinition> Resellers,
    IReadOnlyList<DemoAccountDefinition> Accounts,
    DemoFirewallDefinition Firewall,
    int DiskAlertPercent,
    int CpuAlertPercent,
    int MemoryAlertPercent)
{
    /// <summary>Every site of every account.</summary>
    public IEnumerable<DemoSiteDefinition> Sites
    {
        get { return Accounts.SelectMany(account => { return account.Sites; }); }
    }

    /// <summary>The distinct PHP versions the world's PHP sites need installed.</summary>
    public IReadOnlyList<string> PhpVersions
    {
        get
        {
            return Sites
                .Where(site => { return site.PhpVersion.Length > 0; })
                .Select(site => { return site.PhpVersion; })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
    }
}
