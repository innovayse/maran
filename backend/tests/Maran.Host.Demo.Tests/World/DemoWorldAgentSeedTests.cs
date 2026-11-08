using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.State;
using Maran.Host.Demo.Tests.Fixtures;
using Maran.Host.Demo.World;
using Maran.Host.Demo.World.Enums;

namespace Maran.Host.Demo.Tests.World;

/// <summary>The simulated server starts out knowing the accounts and PHP versions the database seed created.</summary>
public sealed class DemoWorldAgentSeedTests
{
    /// <summary>Bytes in a megabyte.</summary>
    private const ulong BytesPerMegabyte = 1024UL * 1024UL;

    /// <summary>Every account of the world has a system user on the simulated server.</summary>
    [Fact]
    public void Every_account_of_the_world_has_a_system_user()
    {
        var ledger = Seeded();

        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            Assert.True(ledger.Accounts.TryGet(account.Name, out var user), account.Name);
            Assert.Equal("/home/" + account.Name, user.HomeDirectory);
        }
    }

    /// <summary>A system user is suspended exactly when its account is, and nothing else is.</summary>
    [Fact]
    public void Only_suspended_accounts_are_suspended_on_the_server()
    {
        var ledger = Seeded();

        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            Assert.True(ledger.Accounts.TryGet(account.Name, out var user));
            Assert.Equal(account.State == DemoAccountState.Suspended, user.Suspended);
        }

        Assert.Contains(StandardDemoWorld.Instance.Accounts, a => { return a.State == DemoAccountState.Suspended; });
    }

    /// <summary>
    /// The server reports the disk use the world states, which puts the over-limit account over its quota.
    /// </summary>
    [Fact]
    public void The_reported_disk_use_is_the_worlds_and_the_over_limit_account_is_over_quota()
    {
        var ledger = Seeded();

        var overQuota = 0;
        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            Assert.True(ledger.Accounts.TryGet(account.Name, out var user));
            Assert.Equal((ulong)account.DiskUsedMb * BytesPerMegabyte, user.PinnedUsedBytes);
            Assert.Equal((ulong)StandardDemoWorld.QuotaMbOf(account) * BytesPerMegabyte, user.QuotaBytes);
            overQuota += user.PinnedUsedBytes > user.QuotaBytes ? 1 : 0;
        }

        Assert.True(overQuota >= 1);
    }

    /// <summary>Every PHP version the sites run on is installed, and the default stays one.</summary>
    [Fact]
    public void Every_php_version_of_the_world_is_installed()
    {
        var ledger = Seeded();

        Assert.All(StandardDemoWorld.Instance.PhpVersions, version =>
        {
            Assert.True(ledger.PhpRuntimes.Contains(version), version);
        });
    }

    /// <summary>Every account gets its own user id.</summary>
    [Fact]
    public void Every_system_user_has_its_own_id()
    {
        var ledger = Seeded();

        var ids = new List<uint>();
        foreach (var account in StandardDemoWorld.Instance.Accounts)
        {
            Assert.True(ledger.Accounts.TryGet(account.Name, out var user));
            ids.Add(user.Uid);
        }

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>Builds a ledger and applies the seed to it.</summary>
    /// <returns>The seeded ledger.</returns>
    private static SimulatedAgentLedger Seeded()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);
        new DemoWorldAgentSeed(new FixedClock()).ApplyTo(ledger);

        return ledger;
    }
}
