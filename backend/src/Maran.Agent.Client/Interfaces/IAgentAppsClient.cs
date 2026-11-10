using Maran.Agent.Client.Services.AppsService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the host's app store: the web servers and language runtimes the panel installs,
/// removes and controls (issue #517). An app is an operating-system package set the panel runs for sites,
/// named by an id of the agent's closed catalogue and never by a package.
/// </summary>
public interface IAgentAppsClient
{
    /// <summary>Lists the apps installed on the server, with the version and service status of each.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The installed apps in catalogue order, or a typed failure. Every field is probed at the time of the
    /// call, so an app installed or removed by hand is reported as it is.
    /// </returns>
    Task<Result<IReadOnlyList<AgentApp>>> ListAppsAsync(CancellationToken cancellationToken);

    /// <summary>Lists every app of the catalogue the server's package sources can install.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The installable apps, each flagged as installed or not, or a typed failure. The set is what the
    /// agent's package metadata offers for this host, so it is the one an install is validated against and
    /// the one a picker is filled from.
    /// </returns>
    Task<Result<IReadOnlyList<AgentInstallableApp>>> ListInstallableAppsAsync(CancellationToken cancellationToken);

    /// <summary>Installs one app, reporting progress as it goes.</summary>
    /// <param name="appId">The app id of the catalogue, e.g. <c>php-8.3</c>.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome, including the case where
    /// the stream ended without the agent reporting one and the case where the caller cancelled, neither of
    /// which is a success. A failure of the transport itself is a failed terminal event carrying
    /// <c>AgentUnavailable</c>, never an exception; a cancellation that interrupts a read in progress
    /// surfaces as <see cref="OperationCanceledException"/>.
    /// </returns>
    IAsyncEnumerable<AppOperationEvent> InstallAppAsync(string appId, CancellationToken cancellationToken);

    /// <summary>Removes one app, reporting progress as it goes.</summary>
    /// <param name="appId">The app id of the catalogue.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome, with the same endings
    /// as <see cref="InstallAppAsync"/>. The agent refuses a removal with <c>AgentAppNotInstalled</c> or
    /// <c>AgentAppInUse</c> as a failed terminal event.
    /// </returns>
    IAsyncEnumerable<AppOperationEvent> UninstallAppAsync(string appId, CancellationToken cancellationToken);

    /// <summary>Starts, stops, restarts or reloads the service of one installed app.</summary>
    /// <param name="appId">The app id of the catalogue.</param>
    /// <param name="action">What to do to the service.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// What the service manager reports about the service after the action, or a typed failure
    /// (<c>AgentAppNotInstalled</c> when the app is not installed). The effect is host-wide: stopping a web
    /// server takes every site it serves off the air.
    /// </returns>
    Task<Result<AgentAppServiceStatus>> ControlServiceAsync(
        string appId,
        AgentAppServiceAction action,
        CancellationToken cancellationToken);
}
