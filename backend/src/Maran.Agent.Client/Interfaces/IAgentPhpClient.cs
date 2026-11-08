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

    /// <summary>Lists the PHP versions the server can install, newest first, installed ones included.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The installable versions each flagged as installed or not, or a typed failure. The set is what
    /// the agent's package repository offers for this host, so it is the one an install is validated
    /// against and the one an install picker is filled from.
    /// </returns>
    Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken);

    /// <summary>Installs a PHP version and its FPM runtime, reporting progress as it goes.</summary>
    /// <param name="version">Two-component version to install, e.g. <c>8.4</c>.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome — including the case
    /// where the stream ended without the agent reporting one, and the case where the caller
    /// cancelled, neither of which is a success. A failure of the transport itself (an agent that
    /// cannot be reached, a connection torn mid-stream) is a failed terminal event carrying
    /// <c>AgentUnavailable</c>, never an exception; a cancellation that interrupts a read in progress
    /// surfaces as <see cref="OperationCanceledException"/>.
    /// </returns>
    IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken);

    /// <summary>Lists the catalogue PHP extensions of one installed version, loaded or not.</summary>
    /// <param name="version">Two-component version to read, e.g. <c>8.3</c>; it must be installed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// One entry per catalogue extension, alphabetical by name, each with whether the version's
    /// php-fpm has it loaded and what this host's OS family can do with it; or a typed failure. An
    /// availability the agent did not state reads as <see cref="PhpExtensionAvailability.Unknown"/>.
    /// </returns>
    Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken);

    /// <summary>Switches one catalogue extension on or off for one PHP version, reporting progress.</summary>
    /// <param name="version">Two-component version to change, e.g. <c>8.3</c>; it must be installed.</param>
    /// <param name="extension">The catalogue name, exactly as <see cref="ListExtensionsAsync"/> returns it.</param>
    /// <param name="enabled">True to switch the extension on, false to switch it off.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome — including the case
    /// where the stream ended without the agent reporting one, and the case where the caller
    /// cancelled, neither of which is a success. A failure of the transport itself (an agent that
    /// cannot be reached, a connection torn mid-stream) is a failed terminal event carrying
    /// <c>AgentUnavailable</c>, never an exception; a cancellation that interrupts a read in progress
    /// surfaces as <see cref="OperationCanceledException"/>. The change is host-wide for the version:
    /// every account's pool is reloaded.
    /// </returns>
    IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites one account's php-fpm pool at one version with the customer's settings, touching
    /// nothing else (issue #138).
    /// </summary>
    /// <param name="accountUsername">The account's Linux system user name.</param>
    /// <param name="version">Two-component version whose pool is rewritten, e.g. <c>8.3</c>.</param>
    /// <param name="maxChildren">The plan's worker budget for one pool.</param>
    /// <param name="settingOverrides">
    /// The settings the pool carries, already clamped to the plan's bounds. The agent re-validates
    /// every name and value and refuses what it does not accept, never drops it.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success when the pool carries the settings (including when it already did); a typed failure
    /// otherwise, <c>AgentNotFound</c> among them when the account has no pool at that version. The
    /// call never creates a pool: that stays with site creation and a version switch.
    /// </returns>
    Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken);
}
