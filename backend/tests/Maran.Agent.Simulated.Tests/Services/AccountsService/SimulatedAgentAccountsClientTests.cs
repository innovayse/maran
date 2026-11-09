using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.AccountsService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Simulated.Tests.Services.AccountsService;

/// <summary>The identity behind an account: idempotency, caps and the shapes it answers with.</summary>
public sealed class SimulatedAgentAccountsClientTests
{
    /// <summary>Bytes in a mebibyte.</summary>
    private const ulong Mebibyte = 1024UL * 1024;

    /// <summary>The code of a failure for something the host does not hold.</summary>
    private const string NotFoundCode = "AgentNotFound";

    /// <summary>The provisioning most tests create accounts with: no inode ceiling, no login shell.</summary>
    private static readonly AccountProvisioningDto Provisioning = new(null, AccountLoginShell.Nologin);

    /// <summary>The source of the current moment.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>A new account gets its home under /home and a user id above the range the host reserves.</summary>
    [Fact]
    public async Task Creating_an_account_returns_its_home_and_a_user_id_above_the_reserved_range()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var created = await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("/home/acme", created.Value.HomeDirectory);
        Assert.True(created.Value.Uid >= 10000);
    }

    /// <summary>Two accounts never share a user id.</summary>
    [Fact]
    public async Task Creating_two_accounts_gives_them_distinct_user_ids()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var first = await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        var second = await client.CreateAsync("globex", 0, Provisioning, CancellationToken.None);

        Assert.NotEqual(first.Value.Uid, second.Value.Uid);
    }

    /// <summary>The second create of a name is a conflict, exactly as on the real agent.</summary>
    [Fact]
    public async Task Creating_the_same_account_twice_reports_already_exists()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        var again = await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        Assert.False(again.IsSuccess);
        Assert.Equal("AgentAlreadyExists", again.Error!.Code);
        Assert.Equal(ErrorType.Conflict, again.Error.Type);
    }

    /// <summary>A name outside the plain alphabet, or longer than the cap, is stored nowhere.</summary>
    /// <param name="name">The refused name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("9acme")]
    [InlineData("ac me")]
    [InlineData("a;b")]
    [InlineData("a/b")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public async Task A_name_outside_the_alphabet_or_the_length_cap_is_refused_as_invalid_input(string name)
    {
        var ledger = SimulatedHosts.NewLedger();

        var created = await NewClient(ledger).CreateAsync(name, 0, Provisioning, CancellationToken.None);

        Assert.Equal("AgentInvalidInput", created.Error!.Code);
        Assert.Equal(ErrorType.Validation, created.Error.Type);
        Assert.Equal(0, ledger.Accounts.Count);
    }

    /// <summary>The ledger holds no more accounts than its cap, so memory cannot grow without bound.</summary>
    [Fact]
    public async Task Creating_more_accounts_than_the_cap_reports_limit_reached()
    {
        var ledger = SimulatedHosts.NewLedger(new SimulatedAgentLimits { MaxAccounts = 2 });
        var client = NewClient(ledger);
        await client.CreateAsync("one", 0, Provisioning, CancellationToken.None);
        await client.CreateAsync("two", 0, Provisioning, CancellationToken.None);

        var third = await client.CreateAsync("three", 0, Provisioning, CancellationToken.None);

        Assert.Equal("AgentLimitReached", third.Error!.Code);
        Assert.Equal(2, ledger.Accounts.Count);
    }

    /// <summary>Every operation that names an account that is not there reports not found.</summary>
    [Fact]
    public async Task Every_operation_on_an_unknown_account_reports_not_found()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        var token = CancellationToken.None;
        var limits = new AccountSliceLimitsDto(
            SliceCeiling.Unlimited(),
            SliceCeiling.Unlimited(),
            SliceCeiling.Unlimited());

        Assert.Equal(NotFoundCode, (await client.SuspendAsync("ghost", token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.UnsuspendAsync("ghost", AccountLoginShell.Bash, token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.SetQuotaAsync("ghost", 1, null, token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.SetResourceLimitsAsync("ghost", limits, token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.GetSuspensionStateAsync("ghost", token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.DeleteAsync("ghost", token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.GetUsageAsync("ghost", token)).Error!.Code);
        Assert.Equal(NotFoundCode, (await client.SetOpenFilesLimitAsync("ghost", 1024, token)).Error!.Code);
    }

    /// <summary>Suspending locks the account's own login and unsuspending restores it; both may be repeated.</summary>
    [Fact]
    public async Task Suspending_locks_the_login_and_unsuspending_restores_it()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        var before = await client.GetSuspensionStateAsync("acme", CancellationToken.None);
        await client.SuspendAsync("acme", CancellationToken.None);
        var suspendedTwice = await client.SuspendAsync("acme", CancellationToken.None);
        var during = await client.GetSuspensionStateAsync("acme", CancellationToken.None);
        await client.UnsuspendAsync("acme", AccountLoginShell.Bash, CancellationToken.None);
        var after = await client.GetSuspensionStateAsync("acme", CancellationToken.None);

        Assert.True(suspendedTwice.IsSuccess);
        Assert.Equal(AccountLoginPasswordState.Usable, before.Value.LoginPasswordState);
        Assert.Equal(AccountLoginPasswordState.Locked, during.Value.LoginPasswordState);
        Assert.Equal(AccountLoginPasswordState.Usable, after.Value.LoginPasswordState);
    }

    /// <summary>
    /// A site serves the suspension page exactly when it has been disabled, so a suspension is never
    /// certified over a live site.
    /// </summary>
    [Fact]
    public async Task The_suspension_state_reports_a_site_as_stub_only_once_it_is_disabled()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        ledger.Vhosts.Add("shop.example.test", Vhost("acme", "shop.example.test", true));

        var live = await client.GetSuspensionStateAsync("acme", CancellationToken.None);
        ledger.Vhosts.Replace("shop.example.test", vhost => { return vhost with { Enabled = false }; });
        var disabled = await client.GetSuspensionStateAsync("acme", CancellationToken.None);

        Assert.Equal(SiteServing.NotStub, Assert.Single(live.Value.Sites).Serving);
        Assert.Equal(SiteServing.Stub, Assert.Single(disabled.Value.Sites).Serving);
        Assert.True(live.Value.SitesDirectoryReadable);
    }

    /// <summary>Another account's site is never listed under this account.</summary>
    [Fact]
    public async Task The_suspension_state_lists_only_the_accounts_own_sites()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        await client.CreateAsync("globex", 0, Provisioning, CancellationToken.None);
        ledger.Vhosts.Add("shop.example.test", Vhost("globex", "shop.example.test", true));

        var state = await client.GetSuspensionStateAsync("acme", CancellationToken.None);

        Assert.Empty(state.Value.Sites);
    }

    /// <summary>The client owns no cron entries or transfer logins, so it reports none rather than guessing.</summary>
    [Fact]
    public async Task The_suspension_state_reports_no_cron_entries_and_no_transfer_logins()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        var state = (await client.GetSuspensionStateAsync("acme", CancellationToken.None)).Value;

        Assert.Equal(0u, state.CronEntriesTotal);
        Assert.Equal(0u, state.CronEntriesSuspended);
        Assert.Empty(state.FileTransferLogins);
        Assert.Equal(0u, Assert.IsType<MeasuredUnmanagedLogins>(state.UnmanagedLogins).Count);
    }

    /// <summary>Deleting frees what the account used and takes its sites and their certificates with it.</summary>
    [Fact]
    public async Task Deleting_an_account_returns_the_freed_bytes_and_removes_its_sites_and_certificates()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        await client.CreateAsync("globex", 0, Provisioning, CancellationToken.None);
        ledger.Vhosts.Add("shop.example.test", Vhost("acme", "shop.example.test", true));
        ledger.Vhosts.Add("other.example.test", Vhost("globex", "other.example.test", true));
        ledger.Certificates.Add("shop.example.test", Certificate("acme", "shop.example.test"));
        var used = (await client.GetUsageAsync("acme", CancellationToken.None)).Value.UsedBytes;

        var freed = await client.DeleteAsync("acme", CancellationToken.None);

        Assert.Equal(used, freed.Value);
        Assert.False(ledger.Accounts.Contains("acme"));
        Assert.False(ledger.Vhosts.Contains("shop.example.test"));
        Assert.False(ledger.Certificates.Contains("shop.example.test"));
        Assert.True(ledger.Vhosts.Contains("other.example.test"));
        Assert.True(ledger.Accounts.Contains("globex"));
    }

    /// <summary>A set quota is reported as enforced, and zero removes the limit.</summary>
    [Fact]
    public async Task A_quota_is_reported_as_enforced_and_zero_removes_it()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        var unset = await client.GetUsageAsync("acme", CancellationToken.None);
        await client.SetQuotaAsync("acme", 500 * Mebibyte, null, CancellationToken.None);
        var enforced = await client.GetUsageAsync("acme", CancellationToken.None);
        await client.SetQuotaAsync("acme", 0, null, CancellationToken.None);
        var removed = await client.GetUsageAsync("acme", CancellationToken.None);

        Assert.IsType<AccountQuota.EnforceableButUnset>(unset.Value.Quota);
        Assert.Equal(500 * Mebibyte, Assert.IsType<AccountQuota.Enforced>(enforced.Value.Quota).Bytes);
        Assert.IsType<AccountQuota.EnforceableButUnset>(removed.Value.Quota);
    }

    /// <summary>Usage is a function of the account and the moment, so two reads at one moment agree.</summary>
    [Fact]
    public async Task Usage_is_the_same_on_every_read_at_the_same_moment()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        var first = await client.GetUsageAsync("acme", CancellationToken.None);
        var second = await client.GetUsageAsync("acme", CancellationToken.None);

        Assert.Equal(first.Value.UsedBytes, second.Value.UsedBytes);
        Assert.True(first.Value.UsedBytes >= 8 * Mebibyte);
    }

    /// <summary>Usage grows as the injected clock moves forward, never backward.</summary>
    [Fact]
    public async Task Usage_grows_as_the_clock_moves_forward()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        var today = (await client.GetUsageAsync("acme", CancellationToken.None)).Value.UsedBytes;

        _clock.UtcNow = _clock.UtcNow.AddDays(30);
        var later = (await client.GetUsageAsync("acme", CancellationToken.None)).Value.UsedBytes;

        Assert.True(later > today);
    }

    /// <summary>A quota stops writes, so reported usage never passes it.</summary>
    [Fact]
    public async Task Usage_never_exceeds_a_quota_that_stops_writes()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", Mebibyte, Provisioning, CancellationToken.None);

        var usage = await client.GetUsageAsync("acme", CancellationToken.None);

        Assert.Equal(Mebibyte, usage.Value.UsedBytes);
    }

    /// <summary>A seed can pin a figure to put an account over its quota; it is reported verbatim.</summary>
    [Fact]
    public async Task A_pinned_usage_is_reported_verbatim_even_above_the_quota()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", Mebibyte, Provisioning, CancellationToken.None);
        ledger.Accounts.Replace("acme", account => { return account with { PinnedUsedBytes = 5 * Mebibyte }; });

        var usage = await client.GetUsageAsync("acme", CancellationToken.None);

        Assert.Equal(5 * Mebibyte, usage.Value.UsedBytes);
    }

    /// <summary>The slice ceilings are replaced as a whole.</summary>
    [Fact]
    public async Task Setting_resource_limits_replaces_the_ceilings()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        var limits = new AccountSliceLimitsDto(
            SliceCeiling.Limited(50),
            SliceCeiling.Unlimited(),
            SliceCeiling.Limited(200));

        var set = await client.SetResourceLimitsAsync("acme", limits, CancellationToken.None);

        Assert.True(set.IsSuccess);
        Assert.True(ledger.Accounts.TryGet("acme", out var account));
        Assert.NotNull(account);
        Assert.Equal(limits, account.Limits);
    }

    /// <summary>Every simulated home has the right group, so the census repairs none.</summary>
    [Fact]
    public async Task Repairing_home_groups_examines_every_account_and_repairs_none()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        await client.CreateAsync("globex", 0, Provisioning, CancellationToken.None);

        var report = await client.RepairHomeGroupsAsync(false, CancellationToken.None);

        Assert.Equal(2u, report.Value.Examined);
        Assert.Equal(2u, report.Value.AlreadyCorrect);
        Assert.Empty(report.Value.Repaired);
        Assert.Empty(report.Value.WouldRepair);
        Assert.Empty(report.Value.Refused);
    }

    /// <summary>The inode ceiling and the login shell an account is created with are kept.</summary>
    [Fact]
    public async Task Creating_an_account_keeps_the_inode_limit_and_the_login_shell()
    {
        var ledger = SimulatedHosts.NewLedger();

        await NewClient(ledger).CreateAsync(
            "acme", 0, new AccountProvisioningDto(5000, AccountLoginShell.Bash), CancellationToken.None);

        Assert.True(ledger.Accounts.TryGet("acme", out var account));
        Assert.NotNull(account);
        Assert.Equal(5000UL, account.InodeLimit);
        Assert.Equal(AccountLoginShell.Bash, account.Shell);
    }

    /// <summary>A shell the contract does not name is refused and no account is created.</summary>
    [Fact]
    public async Task Creating_an_account_with_an_unknown_shell_is_refused()
    {
        var ledger = SimulatedHosts.NewLedger();

        var created = await NewClient(ledger).CreateAsync(
            "acme", 0, new AccountProvisioningDto(null, (AccountLoginShell)9), CancellationToken.None);

        Assert.Equal("AgentInvalidInput", created.Error!.Code);
        Assert.Equal(0, ledger.Accounts.Count);
    }

    /// <summary>Suspension takes the shell away and reactivation gives back the one the caller states.</summary>
    [Fact]
    public async Task Suspending_takes_the_shell_away_and_unsuspending_gives_back_the_stated_one()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync(
            "acme", 0, new AccountProvisioningDto(null, AccountLoginShell.Bash), CancellationToken.None);

        await client.SuspendAsync("acme", CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var suspended));
        Assert.NotNull(suspended);
        await client.UnsuspendAsync("acme", AccountLoginShell.Bash, CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var active));
        Assert.NotNull(active);

        Assert.Equal(AccountLoginShell.Nologin, suspended.Shell);
        Assert.Equal(AccountLoginShell.Bash, active.Shell);
    }

    /// <summary>A shell the contract does not name is refused and the account stays suspended.</summary>
    [Fact]
    public async Task Unsuspending_with_an_unknown_shell_is_refused_and_changes_nothing()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);
        await client.SuspendAsync("acme", CancellationToken.None);

        var refused = await client.UnsuspendAsync("acme", (AccountLoginShell)9, CancellationToken.None);
        var state = await client.GetSuspensionStateAsync("acme", CancellationToken.None);

        Assert.Equal("AgentInvalidInput", refused.Error!.Code);
        Assert.Equal(AccountLoginPasswordState.Locked, state.Value.LoginPasswordState);
    }

    /// <summary>A quota change states the inode limit again; stating none removes it.</summary>
    [Fact]
    public async Task Setting_the_quota_replaces_the_inode_limit_and_null_removes_it()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync(
            "acme", 0, new AccountProvisioningDto(5000, AccountLoginShell.Nologin), CancellationToken.None);

        await client.SetQuotaAsync("acme", Mebibyte, 9000, CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var changed));
        Assert.NotNull(changed);
        await client.SetQuotaAsync("acme", Mebibyte, null, CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var removed));
        Assert.NotNull(removed);

        Assert.Equal(9000UL, changed.InodeLimit);
        Assert.Null(removed.InodeLimit);
    }

    /// <summary>The open-files ceiling is stored and can be removed; stating it twice changes nothing.</summary>
    [Fact]
    public async Task The_open_files_limit_is_stored_and_removed()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        await client.CreateAsync("acme", 0, Provisioning, CancellationToken.None);

        await client.SetOpenFilesLimitAsync("acme", 4096, CancellationToken.None);
        var twice = await client.SetOpenFilesLimitAsync("acme", 4096, CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var limited));
        Assert.NotNull(limited);
        await client.SetOpenFilesLimitAsync("acme", null, CancellationToken.None);
        Assert.True(ledger.Accounts.TryGet("acme", out var unlimited));
        Assert.NotNull(unlimited);

        Assert.True(twice.IsSuccess);
        Assert.Equal(4096UL, limited.OpenFilesLimit);
        Assert.Null(unlimited.OpenFilesLimit);
    }

    /// <summary>A caller that has already given up is told so, as with a real transport.</summary>
    [Fact]
    public async Task A_cancelled_call_is_not_answered()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var client = NewClient(SimulatedHosts.NewLedger());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => { return client.CreateAsync("acme", 0, Provisioning, cancelled.Token); });
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => { return client.SuspendAsync("acme", cancelled.Token); });
    }

    /// <summary>Builds a client over <paramref name="ledger"/> and this test's clock.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <returns>The client.</returns>
    private SimulatedAgentAccountsClient NewClient(SimulatedAgentLedger ledger)
    {
        return new SimulatedAgentAccountsClient(ledger, _clock);
    }

    /// <summary>Builds a PHP-less static vhost record.</summary>
    /// <param name="account">The owning account.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="enabled">Whether the vhost serves its content.</param>
    /// <returns>The record.</returns>
    private static SimulatedVhost Vhost(string account, string domain, bool enabled)
    {
        return new SimulatedVhost(
            account,
            domain,
            [],
            SiteBackendKind.Static,
            string.Empty,
            string.Empty,
            0,
            [],
            enabled,
            "/home/" + account + "/sites/" + domain);
    }

    /// <summary>Builds a certificate record.</summary>
    /// <param name="account">The owning account.</param>
    /// <param name="domain">The domain it was installed for.</param>
    /// <returns>The record.</returns>
    private SimulatedCertificate Certificate(string account, string domain)
    {
        return new SimulatedCertificate(account, domain, _clock.UtcNow.AddDays(90), _clock.UtcNow);
    }
}
