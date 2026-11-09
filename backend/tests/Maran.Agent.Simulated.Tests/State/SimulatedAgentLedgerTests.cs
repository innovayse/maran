using System.Collections.Concurrent;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.State;

/// <summary>The one place the simulated host's state lives.</summary>
public sealed class SimulatedAgentLedgerTests
{
    /// <summary>Each section is bounded by the cap the limits name for it.</summary>
    [Fact]
    public void Each_section_is_bounded_by_its_own_cap()
    {
        var ledger = new SimulatedAgentLedger(new SimulatedAgentLimits { MaxAccounts = 1, MaxSites = 1 });

        var first = ledger.Accounts.Add("a", Account("a"));
        var second = ledger.Accounts.Add("b", Account("b"));

        Assert.Equal(SimulatedLedgerAdd.Added, first);
        Assert.Equal(SimulatedLedgerAdd.LimitReached, second);
    }

    /// <summary>A user id is never handed out twice, even when many threads ask at once.</summary>
    [Fact]
    public void User_ids_are_unique_under_concurrent_requests()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);
        var seen = new ConcurrentBag<uint>();

        Parallel.For(0, 500, _ => { seen.Add(ledger.NextUid()); });

        Assert.Equal(500, seen.Distinct().Count());
        Assert.True(seen.Min() >= 10000);
    }

    /// <summary>An atomic operation sees the sections it reads and writes as one.</summary>
    [Fact]
    public void An_atomic_operation_returns_what_the_operation_returns()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        var count = ledger.Atomically(
            () =>
            {
                ledger.Accounts.Add("a", Account("a"));
                ledger.Accounts.Add("b", Account("b"));

                return ledger.Accounts.Count;
            });

        Assert.Equal(2, count);
    }

    /// <summary>
    /// A read of the count and the write keyed by it, made atomically, give every thread its own key; if
    /// another thread could slip between them, two would pick the same key and one entry would be lost.
    /// </summary>
    [Fact]
    public void A_read_and_the_write_that_depends_on_it_are_not_torn_by_another_thread()
    {
        var ledger = new SimulatedAgentLedger(new SimulatedAgentLimits { MaxAccounts = 1000 });

        Parallel.For(
            0,
            400,
            _ =>
            {
                ledger.Atomically(
                    () =>
                    {
                        var next = ledger.Accounts.Count;
                        ledger.Accounts.Add("acct-" + next, Account("x"));

                        return next;
                    });
            });

        Assert.Equal(400, ledger.Accounts.Count);
    }

    /// <summary>A fresh ledger holds the baseline host facts, so the monitor has something to report.</summary>
    [Fact]
    public void A_fresh_ledger_holds_the_baseline_host_facts_and_no_panel_certificate()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        Assert.Equal(SimulatedHostFacts.Baseline.MachineId, ledger.HostFacts.Value!.MachineId);
        Assert.Null(ledger.PanelCertificate.Value);
    }

    /// <summary>A ledger without limits is a defect.</summary>
    [Fact]
    public void A_ledger_without_limits_is_a_defect()
    {
        Assert.Throws<ArgumentNullException>(() => { return new SimulatedAgentLedger(null!); });
    }

    /// <summary>Builds an account record for tests that only need an entry.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <returns>The record.</returns>
    private static SimulatedAccount Account(string username)
    {
        return new SimulatedAccount(
            username,
            10000,
            "/home/" + username,
            0,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            false,
            SimulatedAccount.UnlimitedSlices,
            null);
    }
}
