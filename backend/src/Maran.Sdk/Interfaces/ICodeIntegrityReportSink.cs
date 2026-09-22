using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Receives a <see cref="CodeIntegrityReport"/> from the closed PluginLoader, the one point where a
/// finding produced outside this monorepo becomes available inside the panel process.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one interface in <c>Maran.Sdk.Interfaces</c> that is called FROM outside this repository,
/// not by it.</b> Every other interface here — <see cref="IAccountResidueAuditor"/>,
/// <see cref="IAlertRecipientDirectory"/> — is called BY this repo's own code asking a module a
/// question. This one is called by the closed <c>PluginLoader</c> (spec §13), loaded into the same
/// process via <c>AssemblyLoadContext</c>, on a schedule this repository does not own and cannot see
/// into. That difference drives every requirement below.
/// </para>
/// <para>
/// <b>Idempotent per <c>(InstalledVersion, ObservedAt)</c>.</b> A <c>PluginLoader</c> retrying a
/// report after a transient failure on its own side (a database hiccup, a restart mid-call) must not
/// cause the implementation to double-raise an alert for the same observation. The alert machine's
/// own debounce (<c>AlertState.Observe</c>) already collapses repeated identical observations into
/// one transition, but an implementation must not rely on that alone without also treating a repeat
/// of the same <c>(InstalledVersion, ObservedAt)</c> pair as the same event rather than a new one.
/// </para>
/// <para>
/// <b>Must never throw for a caller that cannot itself retry meaningfully.</b> The closed side is a
/// foreign component this repository does not control; an implementation that lets an exception
/// escape hands a foreign, closed caller a failure mode it has no documented way to react to. An
/// implementation should catch what it can and record its own failure through the panel's own
/// journal instead.
/// </para>
/// <para>
/// <b>Must return promptly.</b> The caller runs on an unknown schedule this repository cannot see
/// into (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 2's own open question about
/// polling cadence); an implementation must not become a bottleneck the closed side has no way to
/// diagnose.
/// </para>
/// <para>
/// <b>Not decided here, and stated as such:</b> whether the closed side calls this once per polling
/// cycle or only on a transition. This contract accepts either — it is not itself the debounce; the
/// Monitoring module's <c>AlertEvaluator</c>/<c>AlertState</c> machine is.
/// </para>
/// </remarks>
public interface ICodeIntegrityReportSink
{
    /// <summary>Records one code-integrity finding from the closed PluginLoader.</summary>
    /// <param name="report">The finding to record.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task ReportAsync(CodeIntegrityReport report, CancellationToken cancellationToken);
}
