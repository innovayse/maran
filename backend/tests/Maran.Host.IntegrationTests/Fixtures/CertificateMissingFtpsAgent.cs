using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The agent's FTPS half on a host with no certificate material for the hostname: enabling is
/// refused with the agent client's <c>AgentNotFound</c>, exactly as the real translator answers the
/// agent's NOT_FOUND, and every other call is the ordinary <see cref="StubAgentFtpsClient"/>.
/// </summary>
public sealed class CertificateMissingFtpsAgent : IAgentFtpsClient
{
    /// <summary>The ordinary stub every call other than enabling is passed to.</summary>
    private readonly StubAgentFtpsClient _inner = new();

    /// <summary>Refuses as the agent does when no certificate exists for the hostname.</summary>
    /// <param name="hostname">The hostname; ignored.</param>
    /// <param name="passivePortMin">Lowest passive port; ignored.</param>
    /// <param name="passivePortMax">Highest passive port; ignored.</param>
    /// <param name="passiveAddress">The PASV address; ignored.</param>
    /// <param name="maxClients">The session ceiling; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    /// <returns>The agent's not-found refusal.</returns>
    public Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<FtpsStatusDto>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound)));
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return _inner.DisableAsync(cancellationToken);
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="hostname">The hostname the answer would describe.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        return _inner.GetStatusAsync(hostname, cancellationToken);
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return _inner.ReloadTlsAsync(cancellationToken);
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="arguments">The account, the suffix and the minted password.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        return _inner.CreateUserAsync(arguments, cancellationToken);
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="ftpsUsername">The login suffix.</param>
    /// <param name="password">The new password, never recorded.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return _inner.SetPasswordAsync(accountUsername, ftpsUsername, password, cancellationToken);
    }

    /// <summary>Passes the call to the ordinary stub.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="ftpsUsername">The login suffix.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The stub's answer.</returns>
    public Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        return _inner.DeleteUserAsync(accountUsername, ftpsUsername, cancellationToken);
    }
}
