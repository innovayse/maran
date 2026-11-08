using System.Diagnostics.CodeAnalysis;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.ValueObjects;
using Maran.Modules.Monitoring.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Records how a scheduled, unattended panel update ended: one audit-journal entry and, for a failure,
/// an alert to the operator (issue #383). Called by the root-only <c>maran</c> command at the end of
/// <c>maran update --security-only --scheduled</c>, as the service account; it is not a verb an
/// operator types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Arguments.</b> <c>report-update &lt;outcome&gt; &lt;from&gt; &lt;to&gt;</c>, exactly three, where the
/// outcome is one of <c>applied</c>, <c>rolled-back</c>, <c>rollback-failed</c>, <c>interrupted</c>. They
/// are re-validated here (rules/security.md item 1): an unknown outcome word, a missing or extra
/// argument, or a release name outside the plain alphabet is a usage error and writes nothing.
/// </para>
/// <para>
/// <b>Nothing to leak.</b> The verb prints nothing on success and one fixed line on a usage error. The
/// decision of what to journal and whom to tell is the Monitoring module's
/// (<see cref="PanelUpdateReporter"/>); this verb carries the request in.
/// </para>
/// <para>
/// <b>The caller is trusted as root is trusted.</b> Like <c>admin-reset-password</c>, the proof that the
/// caller is root is the wrapper's check, not this process's; the verb can only ever add a journal entry
/// and a mail about a release update, never change one.
/// </para>
/// </remarks>
public sealed class ReportUpdateCliCommand : ICliCommand
{
    /// <summary>The number of arguments the verb takes: the outcome and the two release names.</summary>
    private const int ArgumentCount = 3;

    /// <summary>The wire word for each outcome, as the shell library spells it.</summary>
    private static readonly Dictionary<string, PanelUpdateOutcome> OutcomeWords =
        new Dictionary<string, PanelUpdateOutcome>(StringComparer.Ordinal)
        {
            ["applied"] = PanelUpdateOutcome.Applied,
            ["rolled-back"] = PanelUpdateOutcome.RolledBack,
            ["rollback-failed"] = PanelUpdateOutcome.RollbackFailed,
            ["interrupted"] = PanelUpdateOutcome.Interrupted,
        };

    /// <summary>Opens the scope the reporter is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per run, because the reporter holds a scoped context.</param>
    public ReportUpdateCliCommand(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "report-update"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (!TryParse(invocation.Arguments, out var report))
        {
            await console.Error.WriteLineAsync(
                "usage: report-update <applied|rolled-back|rollback-failed|interrupted> <from-release> <to-release>");
            return new CliResult(CliExitCode.Usage);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<PanelUpdateReporter>();
        await reporter.ReportAsync(report, cancellationToken);
        return new CliResult(CliExitCode.Success);
    }

    /// <summary>Reads the arguments into a report, or says they are not a valid request.</summary>
    /// <param name="arguments">Everything after the verb.</param>
    /// <param name="report">The report, when the arguments were acceptable.</param>
    /// <returns>True when a report was built.</returns>
    private static bool TryParse(IReadOnlyList<string> arguments, [NotNullWhen(true)] out PanelUpdateReport? report)
    {
        report = null;
        if (arguments.Count != ArgumentCount || !OutcomeWords.TryGetValue(arguments[0], out var outcome))
        {
            return false;
        }

        return PanelUpdateReport.TryCreate(outcome, arguments[1], arguments[2], out report);
    }
}
