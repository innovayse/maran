using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner FTPS client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is the point, as it is for the database and SFTP recorders: only a call that never
/// returns proves the decorator applies a TIMEOUT rather than merely forwarding.
/// </remarks>
internal sealed class RecordingAgentFtpsClient : IAgentFtpsClient
{
    /// <summary>A status the ok paths return; its content is irrelevant to these tests.</summary>
    private static readonly FtpsStatusDto Observed = new(true, true, true, false, "/etc/maran/ftps/host", 30000, 30100, false, true);

    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The hostname of the last call that carried one.</summary>
    public string? LastHostname { get; private set; }

    /// <summary>The lowest passive port of the last enable call.</summary>
    public uint LastPassivePortMin { get; private set; }

    /// <summary>The highest passive port of the last enable call.</summary>
    public uint LastPassivePortMax { get; private set; }

    /// <summary>The advertised passive address of the last enable call.</summary>
    public string? LastPassiveAddress { get; private set; }

    /// <summary>The session ceiling of the last enable call.</summary>
    public uint LastMaxClients { get; private set; }

    /// <summary>The creation arguments of the last creation call.</summary>
    public CreateFtpsUserArguments? LastArguments { get; private set; }

    /// <summary>The account username of the last login call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The login suffix of the last login call.</summary>
    public string? LastFtpsUsername { get; private set; }

    /// <summary>The password of the last password-change call.</summary>
    public SensitiveString? LastPassword { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        LastHostname = hostname;
        LastPassivePortMin = passivePortMin;
        LastPassivePortMax = passivePortMax;
        LastPassiveAddress = passiveAddress;
        LastMaxClients = maxClients;

        await EnterAsync(cancellationToken);

        return Result<FtpsStatusDto>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<FtpsStatusDto>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        LastHostname = hostname;

        await EnterAsync(cancellationToken);

        return Result<FtpsStatusDto>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<FtpsStatusDto>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        LastArguments = arguments;

        await EnterAsync(cancellationToken);

        return Result<string>.Ok("acc1_files");
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastFtpsUsername = ftpsUsername;
        LastPassword = password;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastFtpsUsername = ftpsUsername;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
