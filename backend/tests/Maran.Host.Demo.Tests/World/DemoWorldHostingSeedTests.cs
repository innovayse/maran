using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.State;
using Maran.Host.Demo.Tests.Fixtures;
using Maran.Host.Demo.World;
using Maran.Host.Demo.World.Enums;

namespace Maran.Host.Demo.Tests.World;

/// <summary>
/// The simulated server starts out holding the hosting state the world defines, so server-read lists are populated.
/// </summary>
public sealed class DemoWorldHostingSeedTests
{
    /// <summary>Every site of the world has a virtual host on the simulated server, under its account.</summary>
    [Fact]
    public void Every_site_of_the_world_has_a_virtual_host()
    {
        var ledger = Seeded();

        Assert.Equal(StandardDemoWorld.Instance.Sites.Count(), ledger.Vhosts.Count);
        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            foreach (var site in account.Sites)
            {
                Assert.True(ledger.Vhosts.TryGet(site.Domain, out var vhost), site.Domain);
                Assert.Equal(account.Name, vhost.AccountUsername);
                Assert.Equal(site.Enabled, vhost.Enabled);
            }
        }
    }

    /// <summary>A site has a certificate exactly when the world gives it one, expiring as its state says.</summary>
    [Fact]
    public void Certificates_follow_the_state_of_each_site()
    {
        var ledger = Seeded();
        var now = new FixedClock().UtcNow;

        foreach (var site in StandardDemoWorld.Instance.Sites)
        {
            var held = ledger.Certificates.TryGet(site.Domain, out var certificate);
            Assert.Equal(site.Certificate != DemoCertificateState.None, held);
            if (held && site.Certificate == DemoCertificateState.RenewalFailed)
            {
                Assert.True(certificate!.ExpiresAt - now < TimeSpan.FromDays(7), site.Domain);
            }
        }

        Assert.Contains(StandardDemoWorld.Instance.Sites, site =>
        {
            return site.Certificate == DemoCertificateState.RenewalFailed;
        });
    }

    /// <summary>Every account's databases, logins, crontab entries and backups are on the server.</summary>
    [Fact]
    public void Every_account_has_its_databases_logins_cron_entries_and_backups()
    {
        var ledger = Seeded();

        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            Assert.Equal(account.Databases.Count, ledger.Databases.ListFor(account.Name).Count);
            Assert.Equal(account.CronEntries.Count, ledger.CronTables.List(account.Name).Count);
            Assert.Equal(account.BackupCount, ledger.Backups.ListFor(account.Name).Count);
            foreach (var user in account.SftpUsers)
            {
                Assert.NotNull(ledger.Logins.Find(account.Name, user));
            }

            foreach (var user in account.FtpUsers)
            {
                Assert.True(ledger.FtpsLogins.RequireLogin(account.Name, user).IsSuccess, user);
            }
        }
    }

    /// <summary>The firewall holds the allowed port rules and the bans the world defines.</summary>
    [Fact]
    public void The_firewall_holds_the_worlds_rules_and_bans()
    {
        var ledger = Seeded();
        var firewall = StandardDemoWorld.Instance.Firewall;

        var rules = ledger.FirewallRules.ListRules([22], 8443);
        Assert.True(rules.IsSuccess);
        Assert.All(
            firewall.PortRules.Where(rule => { return rule.Allow; }),
            rule => { Assert.Contains(rules.Value, listed => { return listed.Port == rule.Port; }); });
        Assert.Equal(firewall.Bans.Count, ledger.FirewallRules.ListBans(new FixedClock().UtcNow).Count);
    }

    /// <summary>The FTPS daemon is up, because accounts have FTPS logins.</summary>
    [Fact]
    public void The_ftps_daemon_is_running()
    {
        var ledger = Seeded();

        Assert.True(ledger.FtpsLogins.Status("example.com").Running);
    }

    /// <summary>
    /// Control: a ledger whose limits are smaller than the world makes the seed fail loudly, not quietly show fewer
    /// rows.
    /// </summary>
    [Fact]
    public void Control_a_ledger_too_small_for_the_world_fails_the_seed()
    {
        var ledger = new SimulatedAgentLedger(new SimulatedAgentLimits { MaxDatabases = 1 });
        new DemoWorldAgentSeed(new FixedClock()).ApplyTo(ledger);

        Assert.Throws<InvalidOperationException>(() => { new DemoWorldHostingSeed(new FixedClock()).ApplyTo(ledger); });
    }

    /// <summary>Builds a ledger and applies both seeds in the order the container does.</summary>
    /// <returns>The seeded ledger.</returns>
    private static SimulatedAgentLedger Seeded()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);
        new DemoWorldAgentSeed(new FixedClock()).ApplyTo(ledger);
        new DemoWorldHostingSeed(new FixedClock()).ApplyTo(ledger);

        return ledger;
    }
}
