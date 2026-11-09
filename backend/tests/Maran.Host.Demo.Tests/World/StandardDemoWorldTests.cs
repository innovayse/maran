using System.Text.RegularExpressions;
using Maran.Host.Demo.World;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Host.Demo.Tests.World;

/// <summary>
/// The demonstration world is data, and these tests assert it by value: that it is consistent, that it
/// stays inside what the real validators and plans allow, and that it carries every state the demo
/// promises its visitors.
/// </summary>
public sealed partial class StandardDemoWorldTests
{
    /// <summary>The standard plans' quotas and limits, as the plan seeder states them.</summary>
    private static readonly Dictionary<string, (int DiskMb, int Sites, int Databases, int Sftp, int Ftp, int Cron)>
        Standard = new(StringComparer.Ordinal)
        {
            [StandardDemoWorld.StarterPlanName] = (5_120, 5, 2, 3, 3, 5),
            [StandardDemoWorld.BusinessPlanName] = (25_600, 25, 10, 10, 10, 20),
            [StandardDemoWorld.UnlimitedPlanName] = (1_048_576, 500, 500, 100, 100, 200),
        };

    /// <summary>The world under test.</summary>
    private static DemoWorld World
    {
        get { return StandardDemoWorld.Instance; }
    }

    /// <summary>Account names, account domains and site domains are each unique across the world.</summary>
    [Fact]
    public void Names_and_domains_are_unique()
    {
        Assert.Equal(World.Accounts.Count, World.Accounts.Select(a =>
        {
            return a.Name;
        }).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            World.Accounts.Count,
            World.Accounts.Select(a => { return a.PrimaryDomain; }).Distinct(StringComparer.Ordinal).Count());

        var sites = World.Sites.Select(site => { return site.Domain; }).ToList();
        Assert.Equal(sites.Count, sites.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The name check is able to fail: a bad name and a good one are told apart.</summary>
    [Fact]
    public void The_account_name_check_distinguishes_a_good_name_from_a_bad_one()
    {
        Assert.Matches(AccountName(), "acmebakery");
        Assert.DoesNotMatch(AccountName(), "Acme-Bakery");
    }

    /// <summary>Every account name and every database and login suffix passes the validators' pattern.</summary>
    [Fact]
    public void Every_name_and_suffix_is_one_the_validators_accept()
    {
        foreach (var account in World.Accounts)
        {
            Assert.Matches(AccountName(), account.Name);
            Assert.All(account.Databases, d => { Assert.Matches(Suffix(), d.Suffix); });
            Assert.All(account.Databases, d => { Assert.Matches(Suffix(), d.UserSuffix); });
            Assert.All(account.SftpUsers, name => { Assert.Matches(Suffix(), name); });
            Assert.All(account.FtpUsers, name => { Assert.Matches(Suffix(), name); });
        }

        Assert.All(World.Resellers, reseller => { Assert.Matches(AccountName(), reseller.Name); });
    }

    /// <summary>The featured customer is one of the accounts, active, and the world's only published login.</summary>
    [Fact]
    public void The_featured_customer_is_an_active_account_of_the_world()
    {
        var featured = Assert.Single(World.Accounts, a => { return a.Name == World.FeaturedCustomerAccountName; });

        Assert.Equal(DemoAccountState.Active, featured.State);
        Assert.Null(featured.ResellerName);
    }

    /// <summary>
    /// Every account's plan is a standard plan, one the administrator adds, or one its reseller owns.
    /// </summary>
    [Fact]
    public void Every_account_names_a_plan_that_exists_for_it()
    {
        foreach (var account in World.Accounts)
        {
            if (account.ResellerName is { } resellerName)
            {
                var reseller = Assert.Single(World.Resellers, r => { return r.Name == resellerName; });
                Assert.Contains(reseller.Plans, plan => { return plan.Name == account.PlanName; });
                continue;
            }

            Assert.True(
                Standard.ContainsKey(account.PlanName) || World.Plans.Any(plan =>
                {
                    return plan.Name == account.PlanName;
                }),
                $"account '{account.Name}' names plan '{account.PlanName}' that does not exist");
        }
    }

    /// <summary>No account holds more than its plan allows, so the real handlers accept every seeded row.</summary>
    [Fact]
    public void No_account_exceeds_its_plan()
    {
        foreach (var account in World.Accounts)
        {
            var (sites, databases, sftp, ftp, cron) = LimitsOf(account);

            Assert.True(account.Sites.Count <= sites, $"{account.Name}: sites");
            Assert.True(account.Databases.Count <= databases, $"{account.Name}: databases");
            Assert.True(account.SftpUsers.Count <= sftp, $"{account.Name}: SFTP logins");
            Assert.True(account.FtpUsers.Count <= ftp, $"{account.Name}: FTPS logins");
            Assert.True(account.CronEntries.Count <= cron, $"{account.Name}: scheduled jobs");
        }
    }

    /// <summary>The world carries a suspended account and an account whose disk use is above its quota.</summary>
    [Fact]
    public void The_world_has_a_suspended_account_and_an_over_limit_one()
    {
        Assert.Contains(World.Accounts, account => { return account.State == DemoAccountState.Suspended; });

        var overLimit = World.Accounts.Where(account =>
        {
            return account.DiskUsedMb > StandardDemoWorld.QuotaMbOf(account);
        });
        Assert.NotEmpty(overLimit);
    }

    /// <summary>The world carries all three certificate states and every site backend.</summary>
    [Fact]
    public void The_world_has_every_certificate_state_and_backend()
    {
        Assert.Contains(World.Sites, site => { return site.Certificate == DemoCertificateState.Valid; });
        Assert.Contains(World.Sites, site => { return site.Certificate == DemoCertificateState.ExpiringSoon; });
        Assert.Contains(World.Sites, site => { return site.Certificate == DemoCertificateState.RenewalFailed; });
        Assert.Contains(World.Sites, site => { return site.Certificate == DemoCertificateState.None; });

        Assert.Contains(World.Sites, site => { return site.BackendType == SiteBackendType.Static; });
        Assert.Contains(World.Sites, site => { return site.BackendType == SiteBackendType.Php; });
        Assert.Contains(World.Sites, site => { return site.BackendType == SiteBackendType.ReverseProxy; });
        Assert.Contains(World.Sites, site => { return !site.Enabled; });
    }

    /// <summary>
    /// A certificate is only ever planned for a site that is enabled, because only those can be issued one.
    /// </summary>
    [Fact]
    public void Only_enabled_sites_carry_certificates()
    {
        Assert.All(
            World.Sites.Where(site => { return !site.Enabled; }),
            site => { Assert.Equal(DemoCertificateState.None, site.Certificate); });
    }

    /// <summary>Sites run on at least three PHP versions, and only PHP sites name one.</summary>
    [Fact]
    public void Sites_run_on_several_php_versions()
    {
        Assert.True(World.PhpVersions.Count >= 3);
        Assert.All(
            World.Sites.Where(site => { return site.BackendType != SiteBackendType.Php; }),
            site => { Assert.Equal(string.Empty, site.PhpVersion); });
        Assert.All(
            World.Sites.Where(site => { return site.BackendType == SiteBackendType.Php; }),
            site => { Assert.NotEqual(string.Empty, site.PhpVersion); });
    }

    /// <summary>Every list screen has at least one row to show, for the administrator and the customer.</summary>
    [Fact]
    public void Every_resource_list_has_a_row_in_the_world()
    {
        Assert.NotEmpty(World.Accounts);
        Assert.NotEmpty(World.Resellers);
        Assert.NotEmpty(World.Plans);
        Assert.Contains(World.Accounts, a => { return a.Databases.Count > 0; });
        Assert.Contains(World.Accounts, a => { return a.SftpUsers.Count > 0; });
        Assert.Contains(World.Accounts, a => { return a.FtpUsers.Count > 0; });
        Assert.Contains(World.Accounts, a => { return a.CronEntries.Any(c => { return c.RunOnce; }); });
        Assert.Contains(World.Accounts, a => { return a.CronEntries.Any(c => { return !c.Enabled; }); });
        Assert.Contains(World.Accounts, a => { return a.BackupCount > 0 && a.BackupSchedule is not null; });
        Assert.Contains(World.Firewall.PortRules, rule => { return rule.Allow; });
        Assert.Contains(World.Firewall.PortRules, rule => { return !rule.Allow; });
        Assert.NotEmpty(World.Firewall.Bans);
        Assert.NotEmpty(World.Firewall.Whitelist);

        // The customer area is populated for the one published customer, not only the administrator's.
        var featured = World.Accounts.Single(a => { return a.Name == World.FeaturedCustomerAccountName; });
        Assert.NotEmpty(featured.Sites);
        Assert.NotEmpty(featured.Databases);
        Assert.NotEmpty(featured.SftpUsers);
        Assert.NotEmpty(featured.FtpUsers);
        Assert.NotEmpty(featured.CronEntries);
        Assert.True(featured.BackupCount > 0);
        Assert.NotNull(featured.BackupSchedule);
    }

    /// <summary>The reseller owns accounts, and nothing in the world names a real host or address.</summary>
    [Fact]
    public void The_reseller_owns_accounts_and_every_name_is_reserved_for_documentation()
    {
        Assert.Contains(World.Accounts, account => { return account.ResellerName == World.Resellers[0].Name; });

        var domains = World.Accounts.SelectMany(a =>
        {
            return a.Sites.Select(s => { return s.Domain; }).Append(a.PrimaryDomain);
        });
        Assert.All(domains, domain => { Assert.EndsWith(".example.com", domain, StringComparison.Ordinal); });

        var documentationRanges = new[] { "198.51.100.", "203.0.113." };
        Assert.All(
            World.Firewall.Bans,
            ban =>
            {
                Assert.Contains(documentationRanges, range =>
                {
                    return ban.Address.StartsWith(range, StringComparison.Ordinal);
                });
            });
    }

    /// <summary>The sites an account's plan limits are read from the standard table or its own plan.</summary>
    /// <param name="account">The account.</param>
    /// <returns>Its plan's limits.</returns>
    private static (int Sites, int Databases, int Sftp, int Ftp, int Cron) LimitsOf(DemoAccountDefinition account)
    {
        if (Standard.TryGetValue(account.PlanName, out var standard))
        {
            return (standard.Sites, standard.Databases, standard.Sftp, standard.Ftp, standard.Cron);
        }

        var plan = World.Plans.Concat(World.Resellers.SelectMany(r => { return r.Plans; })).Single(p =>
        {
            return p.Name == account.PlanName;
        });
        return (plan.MaxSites, plan.MaxDatabases, plan.MaxSftpUsers, plan.MaxFtpUsers, plan.MaxCronEntries);
    }

    /// <summary>The account-name rule the create-account validator applies.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"\A[a-z][a-z0-9_]{2,29}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AccountName();

    /// <summary>The database and login suffix rule the create validators apply.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"\A[a-z0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Suffix();
}
