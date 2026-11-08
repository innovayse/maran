namespace Maran.Modules.Sites.Services;

/// <summary>
/// Lets one site's log maintenance run at a time in this process, so two passes over the same site can
/// never read the same cursor and add the same lines to a day twice.
/// </summary>
/// <remarks>
/// <para>
/// Two things start maintenance: the hourly pass and the message published when a customer changes a
/// site's retention, and Wolverine runs handlers of different messages in parallel. Both read the stored
/// cursor, ask the agent, then write the counts and move the cursor; run side by side on one site they
/// would both start from the same cursor and count the same lines twice. The cursor is committed in the
/// same transaction as the counts (<see cref="SiteTrafficIngestor"/>), which protects against a failed
/// write but not against a second writer, and this gate is what protects against that.
/// </para>
/// <para>
/// One gate for all sites rather than one per site: the passes are short, a site's pass is dominated by
/// one agent round trip, and the agent itself admits only two summarising reads at a time. The panel is
/// one process (rules/architecture.md), so an in-process gate is a complete answer; it would not be on a
/// second instance, which this architecture does not have.
/// </para>
/// </remarks>
public sealed class SiteLogMaintenanceGate : IDisposable
{
    /// <summary>The single permit.</summary>
    private readonly SemaphoreSlim _permit = new(1, 1);

    /// <summary>Waits for the permit.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Resolves once the caller holds the permit; the caller must <see cref="Release"/> it.</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _permit.WaitAsync(cancellationToken);
    }

    /// <summary>Gives the permit back.</summary>
    public void Release()
    {
        _permit.Release();
    }

    /// <summary>Releases the semaphore when the container disposes the gate at shutdown.</summary>
    public void Dispose()
    {
        _permit.Dispose();
    }
}
