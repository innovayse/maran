using System.Runtime.CompilerServices;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Seeds;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.PhpService;

/// <summary>
/// The events of a simulated PHP install: a canned progress sequence that ends with the version in the
/// ledger, and exactly one terminal event whatever happens.
/// </summary>
/// <remarks>
/// Idempotent like the real agent: installing a version that is already there ends in the same
/// <c>Installed</c> event without progress. A version the repository does not offer ends in a failed
/// event carrying <c>AgentValidationFailed</c>. A caller that stops watching is told so with a
/// <c>Cancelled</c> event rather than a silent end, and then nothing is installed.
/// </remarks>
public sealed class SimulatedPhpInstallEvents
{
    /// <summary>The stage names an install reports, in order; each is one progress event.</summary>
    private static readonly string[] Stages =
    [
        "Refreshing the package index",
        "Downloading the packages",
        "Unpacking the packages",
        "Starting php-fpm",
    ];

    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>Creates a stream source over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    public SimulatedPhpInstallEvents(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _ledger = ledger;
    }

    /// <summary>Produces the install of <paramref name="version"/>.</summary>
    /// <param name="version">The version to install.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events, then exactly one terminal event.</returns>
    public async IAsyncEnumerable<PhpInstallEvent> EventsAsync(
        string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The first event is produced after a real asynchronous boundary, as a read from a socket
        // would be, so a caller that cancels between starting and reading is observed, not skipped.
        await Task.Yield();

        if (!SimulatedInput.IsPhpVersion(version))
        {
            yield return Failed(SimulatedAgentErrors.InvalidInput());
            yield break;
        }

        if (!SimulatedPhpCatalog.Installable.Contains(version))
        {
            yield return Failed(SimulatedAgentErrors.ValidationFailed());
            yield break;
        }

        if (!_ledger.PhpRuntimes.Contains(version))
        {
            for (var step = 0; step < Stages.Length; step++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return Cancelled();
                    yield break;
                }

                yield return new PhpInstallEvent(
                    PhpInstallEventKind.Progress,
                    (uint)(10 + (step * 25)),
                    Stages[step],
                    string.Empty,
                    null);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield return Cancelled();
                yield break;
            }

            var added = _ledger.PhpRuntimes.Add(
                version,
                new SimulatedPhpRuntime(
                    version,
                    SimulatedBaselineSeed.FpmSocketDirectory,
                    false,
                    SimulatedPhpCatalog.DefaultEnabledExtensions));
            if (added == SimulatedLedgerAdd.LimitReached)
            {
                yield return Failed(SimulatedAgentErrors.LimitReached());
                yield break;
            }
        }

        yield return new PhpInstallEvent(PhpInstallEventKind.Installed, 100, string.Empty, version, null);
    }

    /// <summary>Builds the terminal event of an install whose caller stopped watching.</summary>
    /// <returns>The cancelled event.</returns>
    private static PhpInstallEvent Cancelled()
    {
        return new PhpInstallEvent(PhpInstallEventKind.Cancelled, 0, string.Empty, string.Empty, null);
    }

    /// <summary>Builds the terminal event of an install that failed.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The failed event.</returns>
    private static PhpInstallEvent Failed(Error error)
    {
        return new PhpInstallEvent(PhpInstallEventKind.Failed, 0, string.Empty, string.Empty, error);
    }
}
