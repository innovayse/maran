using System.Collections.Concurrent;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The host's account agent as the reseller tests need it: it creates, suspends, resumes, deletes and
/// re-sizes accounts successfully, records each call, can hold every creation at the door until a
/// chosen number of requests have arrived, so a test can make two creations contend for one slot, and
/// answers the two reads a suspension and a plan change make of the host: whether an account's login is
/// locked (it is, from the suspension to the resumption) and how much disk it uses
/// (<see cref="UsedBytes"/>, which a test sets to put an account over a smaller plan's quota).
/// </summary>
/// <remarks>
/// <para>
/// Safe to call from several requests at once, which is the point of it: the recording lists of the
/// other account doubles are plain <see cref="List{T}"/>s and would corrupt under the concurrent
/// creations the allocation race test sends.
/// </para>
/// <para>
/// UNOBSERVED HERE: the host itself. Nothing is created on disk; the agent's own behaviour has its own
/// tests, and the quota and ceilings this double is told to apply are only recorded.
/// </para>
/// </remarks>
public sealed class ResellerAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The home directory every created account is reported to have.</summary>
    private const string Home = "/home/reseller-test";

    /// <summary>The user id every created account is reported to have.</summary>
    private const uint Uid = 2001;

    /// <summary>The bytes a deletion reports freeing.</summary>
    private const ulong BytesFreed = 4096;

    /// <summary>The quota the host reports on every account: 100 GiB, far above any plan the tests sell.</summary>
    private const ulong ReportedQuotaBytes = 100UL * 1024 * 1024 * 1024;

    /// <summary>The disk every account is reported to use until a test says otherwise: 50 MiB.</summary>
    private const long DefaultUsedBytes = 50L * 1024 * 1024;

    /// <summary>How long a held creation waits for the others before it fails the test instead of hanging it.</summary>
    private static readonly TimeSpan BarrierWait = TimeSpan.FromSeconds(30);

    /// <summary>Opens once the expected number of creations have arrived.</summary>
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many creations must be inside the door before any is let through.</summary>
    private readonly int _expectedCreations;

    /// <summary>The names handed to <see cref="CreateAsync"/>, in arrival order.</summary>
    private readonly ConcurrentQueue<string> _created = new();

    /// <summary>The names handed to <see cref="DeleteAsync"/>, in arrival order.</summary>
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>The quotas pushed with <see cref="SetQuotaAsync"/>, in arrival order.</summary>
    private readonly ConcurrentQueue<(string Username, ulong QuotaBytes)> _quotas = new();

    /// <summary>How many creations have arrived at the door.</summary>
    private int _arrived;

    /// <summary>The disk every account is reported to use, in bytes; read and written across threads.</summary>
    private long _usedBytes = DefaultUsedBytes;

    /// <summary>Whether the accounts' logins are locked, which a suspension sets and a resumption clears.</summary>
    private volatile AccountLoginPasswordState _loginState = AccountLoginPasswordState.Usable;

    /// <summary>
    /// Run while a quota push is in flight, before it answers, or <c>null</c> for none. A test uses it to do
    /// something at the one moment an ownership change is half-done: the agent has been told, the row is
    /// not yet written (issue #460).
    /// </summary>
    public Func<Task>? DuringQuotaPush { get; set; }

    /// <summary>Every account name the panel asked this agent to create.</summary>
    public IReadOnlyList<string> Created
    {
        get { return [.. _created]; }
    }

    /// <summary>Every account name the panel asked this agent to delete.</summary>
    public IReadOnlyList<string> Deleted
    {
        get { return [.. _deleted]; }
    }

    /// <summary>Every quota the panel pushed, with the account it was pushed for.</summary>
    public IReadOnlyList<(string Username, ulong QuotaBytes)> Quotas
    {
        get { return [.. _quotas]; }
    }

    /// <summary>
    /// The disk, in bytes, the host reports every account as using. A test raises it above a plan's quota to
    /// make a move to that plan refuse, and puts it back to <c>50 MiB</c> afterwards.
    /// </summary>
    public ulong UsedBytes
    {
        get { return (ulong)Interlocked.Read(ref _usedBytes); }
        set { Interlocked.Exchange(ref _usedBytes, (long)value); }
    }

    /// <summary>Creates the agent.</summary>
    /// <param name="expectedCreations">
    /// How many creations must have arrived before any of them is answered; <c>1</c>, the default, holds
    /// nothing back.
    /// </param>
    public ResellerAgentAccountsClient(int expectedCreations = 1)
    {
        _expectedCreations = expectedCreations;
    }

    /// <inheritdoc />
    public async Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        AccountProvisioningDto provisioning,
        CancellationToken cancellationToken)
    {
        _created.Enqueue(username);

        if (Interlocked.Increment(ref _arrived) >= _expectedCreations)
        {
            _released.TrySetResult();
        }

        await _released.Task.WaitAsync(BarrierWait, cancellationToken);

        return Result<CreatedAccountDto>.Ok(new CreatedAccountDto(Home, Uid));
    }

    /// <inheritdoc />
    public Task<Result> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        _loginState = AccountLoginPasswordState.Locked;

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> UnsuspendAsync(string username, AccountLoginShell shell, CancellationToken cancellationToken)
    {
        _loginState = AccountLoginPasswordState.Usable;

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountSuspensionStateDto>.Ok(new AccountSuspensionStateDto(
            _loginState, true, [], 0, 0, 0, [], new MeasuredUnmanagedLogins(0))));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        _deleted.Enqueue(username);

        return Task.FromResult(Result<ulong>.Ok(BytesFreed));
    }

    /// <inheritdoc />
    public async Task<Result> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        ulong? inodeLimit,
        CancellationToken cancellationToken)
    {
        _quotas.Enqueue((username, quotaBytes));

        if (DuringQuotaPush is { } during)
        {
            await during();
        }

        return Result.Ok();
    }

    /// <inheritdoc />
    public Task<Result> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        var usage = new AccountUsageDto(UsedBytes, new AccountQuota.Enforced(ReportedQuotaBytes));

        return Task.FromResult(Result<AccountUsageDto>.Ok(usage));
    }

    /// <inheritdoc />
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not repair home groups.");
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="openFiles">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> SetOpenFilesLimitAsync(string username, ulong? openFiles, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
