using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner PHP client that records its arguments and can fail on demand.</summary>
internal sealed class RecordingAgentPhpClient : IAgentPhpClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times the listing method was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The version of the last install request.</summary>
    public string? LastVersion { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<IReadOnlyList<PhpVersionDto>>.Ok([new PhpVersionDto("8.3", "/run/php", null)]);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
        string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastVersion = version;
        await Task.Yield();

        yield return new PhpInstallEvent(PhpInstallEventKind.Installed, 100, string.Empty, version, null);
    }
}
