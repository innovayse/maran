using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A PHP runtime client with a scripted list of installed versions and a scripted install stream, for
/// a test that drives the install endpoint over real HTTP.
/// </summary>
/// <remarks>
/// A successful install adds the requested version to the list, so the handler's read-back after the
/// install sees what the real agent would report; a failed one leaves the list untouched.
/// </remarks>
public sealed class ScriptedAgentPhpClient : IAgentPhpClient
{
    /// <summary>The versions the host reports, with a recognisable FPM socket directory on each.</summary>
    private readonly List<PhpVersionDto> _installed;

    /// <summary>The events the install stream yields, in order.</summary>
    private readonly IReadOnlyList<PhpInstallEvent> _events;

    /// <summary>Creates the client.</summary>
    /// <param name="installed">The versions already installed.</param>
    /// <param name="events">The install stream to replay.</param>
    public ScriptedAgentPhpClient(IEnumerable<PhpVersionDto> installed, IReadOnlyList<PhpInstallEvent> events)
    {
        _installed = installed.ToList();
        _events = events;
    }

    /// <summary>How many times an install was requested.</summary>
    public int InstallCalls { get; private set; }

    /// <summary>Answers the scripted list.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The installed versions.</returns>
    public Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<PhpVersionDto>>.Ok(_installed.ToList()));
    }

    /// <summary>Replays the scripted stream.</summary>
    /// <param name="version">The version asked for.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
        string version,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        InstallCalls++;
        foreach (var change in _events)
        {
            if (change.Kind == PhpInstallEventKind.Installed)
            {
                _installed.Add(new PhpVersionDto(version, $"/run/php/{version}", null));
            }

            yield return change;
            await Task.Yield();
        }
    }
}
