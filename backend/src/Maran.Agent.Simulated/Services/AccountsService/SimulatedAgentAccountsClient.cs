using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Usage;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.AccountsService;

/// <summary>
/// The operating-system identity behind a hosting account, kept in memory: no user is created, no
/// home directory exists, and nothing is locked on any machine.
/// </summary>
/// <remarks>
/// Answers exactly like the real agent: a second create reports <c>AgentAlreadyExists</c> (it is not a
/// success) and an operation on an unknown account reports <c>AgentNotFound</c>. Only suspending,
/// reactivating or quota-setting twice is idempotent: a success. Suspending takes the login shell away,
/// and reactivating gives back the shell the caller states, as the real agent keeps no record of the old
/// one. Deleting an account also removes its sites and their certificates, because a home that is gone
/// cannot go on serving a vhost.
/// </remarks>
public sealed class SimulatedAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of the current moment.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <param name="clock">The source of the moment usage is reported for.</param>
    public SimulatedAgentAccountsClient(SimulatedAgentLedger ledger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);

        _ledger = ledger;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        AccountProvisioningDto provisioning,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(provisioning);

        var wellFormed = SimulatedInput.IsAccountName(username, _ledger.Limits.MaxAccountNameLength)
            && Enum.IsDefined(provisioning.Shell);
        if (!wellFormed)
        {
            return Task.FromResult(Result<CreatedAccountDto>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                if (_ledger.Accounts.Contains(username))
                {
                    return Result<CreatedAccountDto>.Fail(SimulatedAgentErrors.AlreadyExists());
                }

                var account = new SimulatedAccount(
                    username,
                    _ledger.NextUid(),
                    "/home/" + username,
                    quotaBytes,
                    _clock.UtcNow,
                    false,
                    SimulatedAccount.UnlimitedSlices,
                    null,
                    provisioning.InodeLimit,
                    provisioning.Shell);

                return _ledger.Accounts.Add(username, account) switch
                {
                    SimulatedLedgerAdd.Added => Result<CreatedAccountDto>.Ok(
                        new CreatedAccountDto(account.HomeDirectory, account.Uid)),
                    SimulatedLedgerAdd.AlreadyExists => Result<CreatedAccountDto>.Fail(
                        SimulatedAgentErrors.AlreadyExists()),
                    _ => Result<CreatedAccountDto>.Fail(SimulatedAgentErrors.LimitReached()),
                };
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Change(
                username,
                account => { return account with { Suspended = true, Shell = AccountLoginShell.Nologin }; },
                cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result> UnsuspendAsync(
        string username,
        AccountLoginShell shell,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(shell))
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        return Task.FromResult(
            Change(
                username,
                account => { return account with { Suspended = false, Shell = shell }; },
                cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _ledger.Atomically(
            () =>
            {
                if (!_ledger.Accounts.TryGet(username, out var account))
                {
                    return Result<AccountSuspensionStateDto>.Fail(SimulatedAgentErrors.NotFound());
                }

                // A vhost serves the suspension page exactly when it is disabled, which is what the
                // sites client records; cron entries and transfer logins are other services' state
                // and are reported as none, so a suspension is never certified over something this
                // client cannot see.
                var sites = _ledger.Vhosts.Snapshot()
                    .Where(
                        vhost => { return string.Equals(vhost.AccountUsername, username, StringComparison.Ordinal); })
                    .Select(
                        vhost =>
                        {
                            return new SiteSuspensionFactDto(
                                vhost.Domain,
                                vhost.Enabled ? SiteServing.NotStub : SiteServing.Stub);
                        })
                    .ToList();

                return Result<AccountSuspensionStateDto>.Ok(
                    new AccountSuspensionStateDto(
                        account.Suspended ? AccountLoginPasswordState.Locked : AccountLoginPasswordState.Usable,
                        true,
                        sites,
                        0,
                        0,
                        0,
                        [],
                        new MeasuredUnmanagedLogins(0)));
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _ledger.Atomically(
            () =>
            {
                if (!_ledger.Accounts.TryGet(username, out var account))
                {
                    return Result<ulong>.Fail(SimulatedAgentErrors.NotFound());
                }

                var freed = SimulatedUsageCurve.AccountUsedBytes(account, _clock.UtcNow);
                foreach (var vhost in _ledger.Vhosts.Snapshot())
                {
                    if (string.Equals(vhost.AccountUsername, username, StringComparison.Ordinal))
                    {
                        _ledger.Vhosts.Remove(vhost.Domain);
                        _ledger.Certificates.Remove(vhost.Domain);
                    }
                }

                _ledger.Accounts.Remove(username);

                return Result<ulong>.Ok(freed);
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        ulong? inodeLimit,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Change(
                username,
                account => { return account with { QuotaBytes = quotaBytes, InodeLimit = inodeLimit }; },
                cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result> SetOpenFilesLimitAsync(string username, ulong? openFiles, CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Change(username, account => { return account with { OpenFilesLimit = openFiles }; }, cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return Task.FromResult(
            Change(username, account => { return account with { Limits = limits }; }, cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_ledger.Accounts.TryGet(username, out var account))
        {
            return Task.FromResult(Result<AccountUsageDto>.Fail(SimulatedAgentErrors.NotFound()));
        }

        AccountQuota quota = account.QuotaBytes == 0
            ? new AccountQuota.EnforceableButUnset()
            : new AccountQuota.Enforced(account.QuotaBytes);

        return Task.FromResult(
            Result<AccountUsageDto>.Ok(
                new AccountUsageDto(SimulatedUsageCurve.AccountUsedBytes(account, _clock.UtcNow), quota)));
    }

    /// <inheritdoc />
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every simulated home is created with the right group, so the census finds nothing to repair
        // whether or not the caller asked for a report only.
        var examined = (uint)_ledger.Accounts.Count;

        return Task.FromResult(
            Result<HomeGroupRepairReportDto>.Ok(new HomeGroupRepairReportDto(examined, examined, [], [], [])));
    }

    /// <summary>Replaces one account with the result of <paramref name="change"/>.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="change">Builds the new account from the current one.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or <c>AgentNotFound</c> when no such account is held.</returns>
    private Result Change(
        string username,
        Func<SimulatedAccount, SimulatedAccount> change,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _ledger.Accounts.Replace(username, change)
            ? Result.Ok()
            : Result.Fail(SimulatedAgentErrors.NotFound());
    }
}
