using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An agent double whose calls all succeed and whose attestation always answers the one observation
/// it was given, whatever was last asked of the host.
/// </summary>
/// <remarks>
/// <para>
/// It is a separate type from <see cref="RecordingAgentAccountsClient"/> because that double MOVES
/// its observation with the call it was last given — <c>LoginLocked</c> true after a suspend, false
/// after an unsuspend — which models an account with a hand-set password and nothing else. A real
/// host does not answer that way for a login with no password hash: <c>passwd -S</c> reports
/// <c>L</c> (Debian) / <c>LK</c> (RHEL) for such a login before a suspension, during it and after
/// it, and every hosting account is that kind, because the agent sets no password on an account's
/// own entry.
/// </para>
/// <para>
/// A double that could not hold an observation still is why the reactivation handler refused every
/// hosting account for the life of the feature while every test here stayed green.
/// </para>
/// <para>
/// Only the three methods suspension and reactivation reach are implemented; the rest throw, so a
/// test that starts using this double for something else is told rather than quietly answered.
/// </para>
/// </remarks>
public sealed class FixedObservationAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The observation every attestation answers with.</summary>
    private readonly AccountSuspensionStateDto _observation;

    /// <summary>Creates a double answering <paramref name="observation"/> for every attestation.</summary>
    /// <param name="observation">What the host reports it is doing for the account.</param>
    public FixedObservationAgentAccountsClient(AccountSuspensionStateDto observation)
    {
        _observation = observation;
    }

    /// <summary>A double for the ordinary hosting account: a login that never had a password.</summary>
    /// <returns>A double answering with a readable directory, no vhosts, and no password hash.</returns>
    /// <remarks>
    /// <c>LoginLocked</c> is <c>true</c> AND the field is
    /// <see cref="AccountLoginPasswordState.Absent"/> at the same time, and that pair is not a
    /// contradiction — it is the whole defect. <c>passwd -S</c> collapses "locked over a password"
    /// and "no password at all" into one answer; the shadow field does not.
    /// </remarks>
    public static FixedObservationAgentAccountsClient ForPasswordlessAccount()
    {
        return new FixedObservationAgentAccountsClient(
            new AccountSuspensionStateDto(true, AccountLoginPasswordState.Absent, true, [], 0, 0, 0, []));
    }

    /// <summary>Succeeds, as a suspension of such an account does on both families.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Succeeds, as the repaired agent's unsuspend now does on both families.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Answers the one observation this double was given.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The fixed observation.</returns>
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountSuspensionStateDto>.Ok(_observation));
    }

    /// <summary>Not exercised by these tests; a call would be a test asking the wrong question.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
