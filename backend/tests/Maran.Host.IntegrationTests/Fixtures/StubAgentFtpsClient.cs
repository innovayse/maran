using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the root agent's FTPS half so the composed panel can be booted in a test.
/// </summary>
/// <remarks>
/// <para>
/// It is a substitution of the one thing that cannot be present: the agent is a separate root
/// process that installs a vsftpd, writes its configuration and creates system logins. Everything
/// else in these tests is the real panel against a real PostgreSQL.
/// </para>
/// <para>
/// It answers every call with a fixed, SUCCESSFUL observation on purpose. A stub that failed would
/// make an authorization test pass for the wrong reason — a 500 is not a 403, but a test asserting
/// only "the customer did not get 200" would accept either, and the fixture that reads the status as
/// an administrator needs a 200 to be reachable at all or its inverse control proves nothing.
/// </para>
/// <para>
/// It records nothing. Nothing here asks what the panel SENT the agent; the questions are who may
/// reach the surface and what a stranger's identifier answers, and a recorder would be a counter
/// that no assertion reads — which reads as proof and is not.
/// </para>
/// </remarks>
public sealed class StubAgentFtpsClient : IAgentFtpsClient
{
    /// <summary>The one observation every call on this stub answers with.</summary>
    /// <remarks>
    /// Deliberately a RUNNING daemon with real certificate material: it is the state in which the
    /// status endpoint has something worth withholding from a customer, which is the property the
    /// admin-only fixture below is about.
    /// </remarks>
    private static readonly FtpsStatusDto Observed = new(
        Running: true,
        ControlPortAnswered: true,
        CertificatePresent: true,
        CertificateIsSelfSigned: false,
        CertificatePath: "/etc/maran/ssl/panel.example.com/fullchain.pem",
        PassivePortMin: 30_000,
        PassivePortMax: 30_099,
        Ipv4Only: true,
        ForcedTls: true);

    /// <summary>Reports the fixed observation as though the daemon had been configured and started.</summary>
    /// <param name="hostname">The hostname the daemon would serve; ignored.</param>
    /// <param name="passivePortMin">Lowest passive port; ignored.</param>
    /// <param name="passivePortMax">Highest passive port; ignored.</param>
    /// <param name="passiveAddress">The PASV address; ignored.</param>
    /// <param name="maxClients">The session ceiling; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<FtpsStatusDto>.Ok(Observed));
    }

    /// <summary>Reports the fixed observation as though the daemon had been stopped.</summary>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<FtpsStatusDto>.Ok(Observed));
    }

    /// <summary>Reports the fixed observation.</summary>
    /// <param name="hostname">The hostname the answer would describe; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<FtpsStatusDto>.Ok(Observed));
    }

    /// <summary>Reports the fixed observation as though the daemon had been restarted.</summary>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<FtpsStatusDto>.Ok(Observed));
    }

    /// <summary>Answers with the fully-qualified login the host would have created.</summary>
    /// <param name="arguments">The account, the suffix and the minted password.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<string>.Ok($"{arguments.AccountUsername}_{arguments.FtpsUsername}"));
    }

    /// <summary>Accepts the password change.</summary>
    /// <param name="accountUsername">The owning account's system user name; ignored.</param>
    /// <param name="ftpsUsername">The login suffix; ignored.</param>
    /// <param name="password">The new password; ignored, and never recorded.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Accepts the removal.</summary>
    /// <param name="accountUsername">The owning account's system user name; ignored.</param>
    /// <param name="ftpsUsername">The login suffix; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    public Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }
}
