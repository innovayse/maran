using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the host's PHP runtimes. Multi-PHP is host-level: a version is installed once
/// and then bound to any number of sites.
/// </summary>
public interface IAgentPhpClient
{
    /// <summary>Lists the PHP versions installed on the server, newest first.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The installed versions, or a typed failure. The panel offers a site only what this returns:
    /// binding a site to a version that is not installed is refused by the agent.
    /// </returns>
    Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Installs a PHP version and its FPM runtime, reporting progress as it goes.</summary>
    /// <param name="version">Two-component version to install, e.g. <c>8.4</c>.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome — including the case
    /// where the stream ended without the agent reporting one, and the case where the caller
    /// cancelled, neither of which is a success.
    /// </returns>
    IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken);
}
