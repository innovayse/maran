using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SftpService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An SFTP agent double for the account lifecycle: it accepts the lock and unlock that a suspension
/// and a reactivation cascade to the Sftp module, and refuses to answer anything else.
/// </summary>
/// <remarks>
/// A suspension is announced to every module that runs something for the account, and the Sftp module
/// answers by locking the account's transfer logins through the agent. Without a stand-in the real
/// client reaches a socket that is not there, the cascade is refused, and the lifecycle answers 500.
/// The calls the lifecycle never makes throw, so a handler that started making them would fail loudly
/// instead of being quietly satisfied.
/// </remarks>
public sealed class LockingSftpAgent : IAgentSftpClient
{
    /// <summary>Answers that no session was ended, as a host with no open transfer session would.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="locked">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Success with zero sessions ended.</returns>
    public Task<Result<AccountLoginLockOutcomeDto>> SetAccountLoginsLockedAsync(
        string accountUsername,
        bool locked,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountLoginLockOutcomeDto>.Ok(new AccountLoginLockOutcomeDto(0)));
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the account lock only.");
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the account lock only.");
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the account lock only.");
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<int>> EndSessionsAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the account lock only.");
    }

    /// <summary>Not exercised by an account lifecycle.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="publicKeys">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> SetAuthorizedKeysAsync(
        string accountUsername,
        string sftpUsername,
        IReadOnlyList<string> publicKeys,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the account lock only.");
    }
}
