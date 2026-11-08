namespace Maran.Sdk.Interfaces;

/// <summary>
/// The one way a module loader asks the panel to restart so that a package it has staged becomes
/// active. The panel composes its modules once, at start (restart-to-activate), so a verified package
/// does nothing until the process starts again; the loader says it is ready and the panel does the rest.
/// </summary>
/// <remarks>
/// <para>
/// The request is cooperative and bounded: the panel stops accepting work, finishes what is in flight
/// and exits with a non-zero status, which is what makes the service manager start it again
/// (<c>Restart=on-failure</c>). It adds no listener, no daemon and no outbound call, and it can be made
/// as many times as the loader likes - a second request while one is pending changes nothing.
/// </para>
/// <para>
/// A restart is not a rollback. The next start re-verifies every installed package and composes only
/// those that pass; one that does not is reported as failed and the rest of the panel runs.
/// </para>
/// </remarks>
public interface IPanelRestart
{
    /// <summary>Asks the panel to restart, recording who asked and which module is waiting for it.</summary>
    /// <param name="moduleId">The module whose staged package needs the restart.</param>
    /// <param name="cancellationToken">Cancellation token for the audit write that precedes the request.</param>
    /// <returns>Resolves once the request is recorded and the panel has begun to stop.</returns>
    Task RequestAsync(string moduleId, CancellationToken cancellationToken);
}
