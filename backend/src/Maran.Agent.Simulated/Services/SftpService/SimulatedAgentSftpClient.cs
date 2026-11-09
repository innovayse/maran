using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SftpService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Services.SftpService;

/// <summary>
/// The agent's SFTP login operations against the simulated server's memory: no jail, mount or system
/// account exists, and nothing authenticates.
/// </summary>
/// <remarks>
/// <see cref="SetAccountLoginsLockedAsync"/> keeps the real call's reach — it is the one member that
/// turns both daemons' logins — by asking the FTPS state as well, so a suspension through this client
/// reports the sessions of every file-transfer login the account holds. No lock marker is stored:
/// the only thing a marker does on a host is refuse an authentication, and nothing here
/// authenticates, so the one observable effect is the sessions ended.
/// </remarks>
public sealed class SimulatedAgentSftpClient : IAgentSftpClient
{
    /// <summary>The SFTP logins of the simulated server.</summary>
    private readonly SimulatedSftpLoginStore _logins;

    /// <summary>The FTPS logins of the simulated server, turned by the same account-wide lock.</summary>
    private readonly SimulatedFtpsStore _ftpsLogins;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    public SimulatedAgentSftpClient(SimulatedAgentLedger ledger)
    {
        _logins = ledger.Logins;
        _ftpsLogins = ledger.FtpsLogins;
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_logins.Create(accountUsername, sftpUsername, 0));
    }

    /// <inheritdoc/>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_logins.Require(accountUsername, sftpUsername));
    }

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(string accountUsername, string sftpUsername, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_logins.Delete(accountUsername, sftpUsername));
    }

    /// <inheritdoc/>
    public Task<Result<AccountLoginLockOutcomeDto>> SetAccountLoginsLockedAsync(
        string accountUsername,
        bool locked,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The unlock direction ends no session, so it states no count rather than a zero.
        uint? sessionsEnded = locked
            ? _logins.EndAllSessions(accountUsername) + _ftpsLogins.EndAllSessions(accountUsername)
            : null;

        return Task.FromResult(Result<AccountLoginLockOutcomeDto>.Ok(new AccountLoginLockOutcomeDto(sessionsEnded)));
    }

    /// <inheritdoc/>
    public Task<Result<int>> EndSessionsAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_logins.EndSessions(accountUsername, sftpUsername));
    }

    /// <inheritdoc/>
    public Task<Result> SetAuthorizedKeysAsync(
        string accountUsername,
        string sftpUsername,
        IReadOnlyList<string> publicKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_logins.ReplaceKeys(accountUsername, sftpUsername, publicKeys));
    }
}
