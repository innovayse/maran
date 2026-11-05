using System.Collections.Concurrent;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An account-agent double for the administrator lifecycle over real HTTP: it answers creation,
/// suspension, reactivation, quota changes and deletion with success, records every call that reached
/// it, and can be told to refuse the next creation or to hold creations inside the agent call until a
/// test releases them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The record is what an idempotency test observes.</b> A replayed answer is only proven to be a
/// replay when the agent saw the work once, so every state-changing call is appended to a queue the
/// test reads back; the queue is concurrent because two requests are in the agent at once on purpose.
/// </para>
/// <para>
/// <b>The hold is what makes a concurrency test able to lose.</b> With creations held, a test knows
/// the first request is inside the agent (its reservation exists) at the instant it sends the second,
/// instead of hoping the scheduler interleaved them.
/// </para>
/// <para>
/// The suspension state follows the calls: after a suspension the login reads locked, after a
/// reactivation it reads usable, which is what the panel's attestation checks expect of a host that
/// did what it was asked. Calls a lifecycle never makes throw instead of answering.
/// </para>
/// </remarks>
public sealed class ScriptedAccountsAgent : IAgentAccountsClient
{
    /// <summary>The home directory every creation reports.</summary>
    private const string Home = "/home/scripted";

    /// <summary>The user id every creation reports.</summary>
    private const uint FirstUid = 1001;

    /// <summary>How many bytes a deletion reports having freed.</summary>
    private const ulong BytesFreed = 4096;

    /// <summary>The account names creations reached the agent for, in arrival order.</summary>
    private readonly ConcurrentQueue<string> _created = new();

    /// <summary>The account names suspensions reached the agent for.</summary>
    private readonly ConcurrentQueue<string> _suspended = new();

    /// <summary>The account names reactivations reached the agent for.</summary>
    private readonly ConcurrentQueue<string> _unsuspended = new();

    /// <summary>The account names deletions reached the agent for.</summary>
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>The quota changes that reached the agent, written <c>name:bytes</c>.</summary>
    private readonly ConcurrentQueue<string> _quotas = new();

    /// <summary>Completed by the first creation that enters the agent while creations are held.</summary>
    private readonly TaskCompletionSource _creationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed by the test to let held creations continue.</summary>
    private readonly TaskCompletionSource _creationsReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether creations wait for <see cref="_creationsReleased"/>.</summary>
    private volatile bool _holdCreations;

    /// <summary>Whether the next creation is refused with a server failure.</summary>
    private volatile bool _failNextCreation;

    /// <summary>What the host reports about the account's login, following the last suspend or unsuspend.</summary>
    private volatile AccountLoginPasswordState _loginState = AccountLoginPasswordState.Usable;

    /// <summary>The account names creations reached the agent for, in arrival order.</summary>
    public IReadOnlyList<string> Created
    {
        get
        {
            return _created.ToArray();
        }
    }

    /// <summary>The account names suspensions reached the agent for.</summary>
    public IReadOnlyList<string> Suspended
    {
        get
        {
            return _suspended.ToArray();
        }
    }

    /// <summary>The account names reactivations reached the agent for.</summary>
    public IReadOnlyList<string> Unsuspended
    {
        get
        {
            return _unsuspended.ToArray();
        }
    }

    /// <summary>The account names deletions reached the agent for.</summary>
    public IReadOnlyList<string> Deleted
    {
        get
        {
            return _deleted.ToArray();
        }
    }

    /// <summary>The quota changes that reached the agent, written <c>name:bytes</c>.</summary>
    public IReadOnlyList<string> Quotas
    {
        get
        {
            return _quotas.ToArray();
        }
    }

    /// <summary>Completes when a creation is inside the agent call while creations are held.</summary>
    public Task CreationEntered
    {
        get
        {
            return _creationEntered.Task;
        }
    }

    /// <summary>Makes every creation wait inside the agent call until <see cref="ReleaseCreations"/>.</summary>
    public void HoldCreations()
    {
        _holdCreations = true;
    }

    /// <summary>Lets the creations that are waiting, and every later one, continue.</summary>
    public void ReleaseCreations()
    {
        _holdCreations = false;
        _creationsReleased.TrySetResult();
    }

    /// <summary>Makes the next creation, and only that one, fail as an agent server fault would.</summary>
    public void FailNextCreation()
    {
        _failNextCreation = true;
    }

    /// <summary>Records the creation, waits when held, and answers success or the scripted failure.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">Ignored.</param>
    /// <param name="cancellationToken">Cancels the wait of a held creation.</param>
    /// <returns>The created user, or a server failure when one was scripted.</returns>
    public async Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        _created.Enqueue(username);

        if (_holdCreations)
        {
            _creationEntered.TrySetResult();
            await _creationsReleased.Task.WaitAsync(cancellationToken);
        }

        if (_failNextCreation)
        {
            _failNextCreation = false;

            return Result<CreatedAccountDto>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));
        }

        return Result<CreatedAccountDto>.Ok(new CreatedAccountDto(Home, FirstUid));
    }

    /// <summary>Records the suspension and locks the login the host reports.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        _suspended.Enqueue(username);
        _loginState = AccountLoginPasswordState.Locked;

        return Task.FromResult(Result.Ok());
    }

    /// <summary>Records the reactivation and unlocks the login the host reports.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        _unsuspended.Enqueue(username);
        _loginState = AccountLoginPasswordState.Usable;

        return Task.FromResult(Result.Ok());
    }

    /// <summary>Reports a host with no sites, no cron entries and no transfer logins, and the login as last set.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The observation.</returns>
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountSuspensionStateDto>.Ok(new AccountSuspensionStateDto(
            _loginState, true, [], 0, 0, 0, [], new MeasuredUnmanagedLogins(0))));
    }

    /// <summary>Records the deletion and reports the bytes a real one would have freed.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The bytes freed.</returns>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        _deleted.Enqueue(username);

        return Task.FromResult(Result<ulong>.Ok(BytesFreed));
    }

    /// <summary>Succeeds without recording: no idempotency test is about runtime ceilings.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="limits">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <summary>Records the quota change.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">The quota asked for.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        _quotas.Enqueue($"{username}:{quotaBytes}");

        return Task.FromResult(Result.Ok());
    }

    /// <summary>Reports an empty home directory, so any plan fits.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Zero bytes used under an enforced quota.</returns>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountUsageDto>.Ok(new AccountUsageDto(0, new AccountQuota.Enforced(4096))));
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="reportOnly">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
