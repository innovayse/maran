using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentAccountsClient"/> double that records what it was asked to do and
/// answers however the test needs.
///
/// The handlers' whole subject is the ORDER of two effects — the agent first, the row second —
/// so a test has to be able to say "the agent refused" and then assert the row did not move.
/// </summary>
public sealed class RecordingAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The error every call answers with, or null to succeed.</summary>
    private readonly Error? _failure;

    /// <summary>Names the agent was asked to act on, in order, prefixed with the operation.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// What this double reports the host to be doing for the account, as the attestation reads it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is settable because the attestation is the subject of most of these tests: a test says
    /// "the host still serves this vhost" or "the directory could not be read" and then asserts the
    /// handler refused. The default is a host that agrees with whatever was last asked of it — no
    /// vhosts, a readable directory — so a test that is not about the attestation does not have to
    /// arrange one.
    /// </para>
    /// <para>
    /// <see cref="SuspendAsync"/> and <see cref="UnsuspendAsync"/> move
    /// <see cref="AccountSuspensionStateDto.LoginLocked"/> on a successful call, which is what makes
    /// the default honest rather than merely convenient: a double whose observation never followed
    /// its own actions would let a handler that never calls the agent at all pass every test here.
    /// </para>
    /// <para>
    /// They move <see cref="AccountSuspensionStateDto.LoginPasswordState"/> with it, between
    /// <see cref="AccountLoginPasswordState.Locked"/> and
    /// <see cref="AccountLoginPasswordState.Usable"/>, so this double models an account with a
    /// HAND-SET password — the one kind of account whose login a suspension really locks and a
    /// resumption really unlocks. It is deliberately NOT the ordinary hosting account, which never
    /// has a password and is modelled by <see cref="FixedObservationAgentAccountsClient"/>: a double
    /// that let one type stand for both is what hid this handler's defect for the life of the
    /// feature.
    /// </para>
    /// </remarks>
    public AccountSuspensionStateDto SuspensionState { get; set; } =
        new AccountSuspensionStateDto(false, AccountLoginPasswordState.Usable, true, [], 0, 0, 0, []);

    /// <summary>Creates a client that succeeds at everything.</summary>
    public RecordingAgentAccountsClient()
    {
    }

    /// <summary>Creates a client that refuses every call with <paramref name="failure"/>.</summary>
    /// <param name="failure">The error to answer with.</param>
    public RecordingAgentAccountsClient(Error failure)
    {
        _failure = failure;
    }

    /// <inheritdoc/>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        Calls.Add($"create:{username}:{quotaBytes}");
        return Task.FromResult(_failure is null
            ? Result<CreatedAccountDto>.Ok(new CreatedAccountDto($"/home/{username}", 1001))
            : Result<CreatedAccountDto>.Fail(_failure));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"suspend:{username}");
        var answer = Answer();
        if (answer.IsSuccess)
        {
            SuspensionState = SuspensionState with
            {
                LoginLocked = true,
                LoginPasswordState = AccountLoginPasswordState.Locked,
            };
        }

        return Task.FromResult(answer);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"unsuspend:{username}");
        var answer = Answer();
        if (answer.IsSuccess)
        {
            SuspensionState = SuspensionState with
            {
                LoginLocked = false,
                LoginPasswordState = AccountLoginPasswordState.Usable,
            };
        }

        return Task.FromResult(answer);
    }

    /// <inheritdoc/>
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        Calls.Add($"observe:{username}");
        return Task.FromResult(_failure is null
            ? Result<AccountSuspensionStateDto>.Ok(SuspensionState)
            : Result<AccountSuspensionStateDto>.Fail(_failure));
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{username}");
        return Task.FromResult(_failure is null ? Result<ulong>.Ok(4096) : Result<ulong>.Fail(_failure));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        Calls.Add($"quota:{username}:{quotaBytes}");
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"usage:{username}");
        return Task.FromResult(_failure is null
            ? Result<AccountUsageDto>.Ok(new AccountUsageDto(2048, 4096))
            : Result<AccountUsageDto>.Fail(_failure));
    }

    /// <summary>How many report-only vs acting home-group repair passes were made, in order.</summary>
    public List<bool> HomeGroupRepairPasses { get; } = [];

    /// <summary>What a report-only pass answers; an empty census by default.</summary>
    public Result<HomeGroupRepairReportDto>? HomeGroupReportResult { get; set; }

    /// <summary>
    /// What an acting pass answers; an empty census by default. Separate from
    /// <see cref="HomeGroupReportResult"/> so a test can make the two passes DISAGREE, which is the
    /// only way to see whether a handler reported the pass it performed or the pass it planned.
    /// </summary>
    public Result<HomeGroupRepairReportDto>? HomeGroupRepairResult { get; set; }

    /// <inheritdoc/>
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        HomeGroupRepairPasses.Add(reportOnly);
        Calls.Add($"repair-home-groups:{reportOnly}");

        var configured = reportOnly ? HomeGroupReportResult : HomeGroupRepairResult;
        if (configured is not null)
        {
            return Task.FromResult(configured);
        }

        return Task.FromResult(_failure is null
            ? Result<HomeGroupRepairReportDto>.Ok(new HomeGroupRepairReportDto(0, 0, [], [], []))
            : Result<HomeGroupRepairReportDto>.Fail(_failure));
    }

    /// <summary>The configured answer for a call returning nothing but success.</summary>
    private Result<bool> Answer()
    {
        return _failure is null ? Result<bool>.Ok(true) : Result<bool>.Fail(_failure);
    }
}
