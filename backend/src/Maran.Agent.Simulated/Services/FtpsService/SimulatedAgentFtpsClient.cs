using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Services.FtpsService;

/// <summary>
/// The agent's FTPS operations against the simulated server's memory: no daemon is installed, no
/// certificate is read and nothing listens.
/// </summary>
/// <remarks>
/// The status answers from the stored configuration, so it agrees with what was enabled — the one
/// property the real status refuses to have, because there the daemon can differ from the panel's
/// intention. Here nothing can differ, and the status says so by being exactly what was set.
/// </remarks>
public sealed class SimulatedAgentFtpsClient : IAgentFtpsClient
{
    /// <summary>The FTPS daemon and logins of the simulated server.</summary>
    private readonly SimulatedFtpsStore _ftps;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    public SimulatedAgentFtpsClient(SimulatedAgentLedger ledger)
    {
        _ftps = ledger.FtpsLogins;
    }

    /// <inheritdoc/>
    public Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_ftps.Enable(hostname, passivePortMin, passivePortMax, passiveAddress, maxClients));
    }

    /// <inheritdoc/>
    public Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<FtpsStatusDto>.Ok(_ftps.Disable()));
    }

    /// <inheritdoc/>
    public Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<FtpsStatusDto>.Ok(_ftps.Status(hostname)));
    }

    /// <inheritdoc/>
    public Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<FtpsStatusDto>.Ok(_ftps.ReloadTls()));
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_ftps.CreateLogin(arguments.AccountUsername, arguments.FtpsUsername, 0));
    }

    /// <inheritdoc/>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_ftps.RequireLogin(accountUsername, ftpsUsername));
    }

    /// <inheritdoc/>
    public Task<Result> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_ftps.DeleteLogin(accountUsername, ftpsUsername));
    }
}
