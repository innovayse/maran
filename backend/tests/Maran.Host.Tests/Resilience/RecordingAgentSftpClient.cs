using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner SFTP client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is the point, as it is for the database recorder: only a call that never returns proves
/// the decorator applies a TIMEOUT rather than merely forwarding. Deletion is the method this
/// repository already caught running with no timeout while every test stayed green.
/// </remarks>
internal sealed class RecordingAgentSftpClient : IAgentSftpClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The account username of the last call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The login suffix of the last call.</summary>
    public string? LastSftpUsername { get; private set; }

    /// <summary>The password of the last call.</summary>
    public SensitiveString? LastPassword { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastSftpUsername = sftpUsername;
        LastPassword = password;

        await EnterAsync(cancellationToken);

        return Result<string>.Ok("acc1_web");
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastSftpUsername = sftpUsername;
        LastPassword = password;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastSftpUsername = sftpUsername;

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
