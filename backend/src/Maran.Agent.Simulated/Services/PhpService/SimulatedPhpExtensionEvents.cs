using System.Runtime.CompilerServices;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.PhpService;

/// <summary>
/// The events of switching one PHP extension on or off for one version: a short progress sequence that
/// ends with the ledger changed, and exactly one terminal event whatever happens.
/// </summary>
/// <remarks>
/// Switching an extension to the state it is already in still ends in <c>Changed</c>, as repeating the
/// real command would. A name outside the catalogue is refused as invalid input and a version that is
/// not installed as <c>AgentValidationFailed</c>; a caller that stops watching is told so with a
/// <c>Cancelled</c> event, and then nothing has changed.
/// </remarks>
public sealed class SimulatedPhpExtensionEvents
{
    /// <summary>The stage names a switch reports, in order; each is one progress event.</summary>
    private static readonly string[] Stages =
    [
        "Updating the extension configuration",
        "Reloading php-fpm",
    ];

    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>Creates a stream source over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    public SimulatedPhpExtensionEvents(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _ledger = ledger;
    }

    /// <summary>Produces the switch of <paramref name="extension"/> for <paramref name="version"/>.</summary>
    /// <param name="version">The version to change.</param>
    /// <param name="extension">The catalogue name of the extension.</param>
    /// <param name="enabled">Whether to switch it on or off.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events, then exactly one terminal event.</returns>
    public async IAsyncEnumerable<PhpExtensionEvent> EventsAsync(
        string version,
        string extension,
        bool enabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // As for an install, the first event comes after a real asynchronous boundary.
        await Task.Yield();

        if (!SimulatedInput.IsPhpVersion(version) || !SimulatedPhpCatalog.Extensions.Contains(extension))
        {
            yield return Failed(SimulatedAgentErrors.InvalidInput());
            yield break;
        }

        if (!_ledger.PhpRuntimes.Contains(version))
        {
            yield return Failed(SimulatedAgentErrors.ValidationFailed());
            yield break;
        }

        for (var step = 0; step < Stages.Length; step++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return Ended(PhpExtensionEventKind.Cancelled, version, extension, enabled);
                yield break;
            }

            yield return new PhpExtensionEvent(
                PhpExtensionEventKind.Progress,
                (uint)(30 + (step * 40)),
                Stages[step],
                version,
                extension,
                enabled,
                null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return Ended(PhpExtensionEventKind.Cancelled, version, extension, enabled);
            yield break;
        }

        _ledger.PhpRuntimes.Replace(version, runtime => { return Switched(runtime, extension, enabled); });

        yield return Ended(PhpExtensionEventKind.Changed, version, extension, enabled);
    }

    /// <summary>Returns <paramref name="runtime"/> with one extension switched.</summary>
    /// <param name="runtime">The runtime as it is.</param>
    /// <param name="extension">The catalogue name of the extension.</param>
    /// <param name="enabled">Whether it is to be loaded.</param>
    /// <returns>The runtime with the extension loaded or not, its list kept alphabetical.</returns>
    private static SimulatedPhpRuntime Switched(SimulatedPhpRuntime runtime, string extension, bool enabled)
    {
        var others = runtime.EnabledExtensions
            .Where(name => { return !string.Equals(name, extension, StringComparison.Ordinal); });
        var loaded = enabled ? others.Append(extension) : others;

        return runtime with { EnabledExtensions = loaded.Order(StringComparer.Ordinal).ToList() };
    }

    /// <summary>Builds a terminal event that is not a failure.</summary>
    /// <param name="kind">How the stream ended.</param>
    /// <param name="version">The version that was being changed.</param>
    /// <param name="extension">The extension that was being switched.</param>
    /// <param name="enabled">The state it was being switched to.</param>
    /// <returns>The event.</returns>
    private static PhpExtensionEvent Ended(PhpExtensionEventKind kind, string version, string extension, bool enabled)
    {
        var percent = kind == PhpExtensionEventKind.Changed ? 100u : 0u;

        return new PhpExtensionEvent(kind, percent, string.Empty, version, extension, enabled, null);
    }

    /// <summary>Builds the terminal event of a switch that failed.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The failed event.</returns>
    private static PhpExtensionEvent Failed(Error error)
    {
        return new PhpExtensionEvent(
            PhpExtensionEventKind.Failed,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            error);
    }
}
