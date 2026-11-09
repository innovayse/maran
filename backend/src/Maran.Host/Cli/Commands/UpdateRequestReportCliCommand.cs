using System.Diagnostics.CodeAnalysis;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.ValueObjects;
using Maran.Modules.Updates.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Tells the panel how a panel-requested update stands or ended, so its task in the task list moves
/// along and closes (issue #382). Called by the root-only <c>maran</c> command at the start and the end of
/// <c>maran update --requested</c>, as the service account; it is not a verb an operator types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Arguments.</b> <c>update-request-report &lt;word&gt; &lt;from&gt; [&lt;to&gt;]</c> where the word is one
/// of <c>installing</c>, <c>applied</c>, <c>unchanged</c>, <c>refused</c>, <c>rolled-back</c>,
/// <c>rollback-failed</c>, <c>interrupted</c>. They are re-validated here (rules/security.md item 1): an
/// unknown word, a missing or extra argument, a missing second release name where one is required, or a
/// release name outside the plain alphabet is a usage error and writes nothing.
/// </para>
/// <para>
/// <b>Nothing to leak and nothing to steer.</b> The verb prints nothing on success and one fixed line on
/// a usage error. It cannot name a task: the one it acts on is the open request in the panel's own
/// ledger, so the most a caller can do is advance or close that task and add a journal entry.
/// </para>
/// </remarks>
public sealed class UpdateRequestReportCliCommand : ICliCommand
{
    /// <summary>The fewest arguments the verb takes: the word and the release left.</summary>
    private const int MinimumArgumentCount = 2;

    /// <summary>The most arguments the verb takes: the word and both release names.</summary>
    private const int MaximumArgumentCount = 3;

    /// <summary>The wire word for each report, as the shell library spells it.</summary>
    private static readonly Dictionary<string, PanelUpdateRunWord> Words =
        new Dictionary<string, PanelUpdateRunWord>(StringComparer.Ordinal)
        {
            ["installing"] = PanelUpdateRunWord.Installing,
            ["applied"] = PanelUpdateRunWord.Applied,
            ["unchanged"] = PanelUpdateRunWord.Unchanged,
            ["refused"] = PanelUpdateRunWord.Refused,
            ["rolled-back"] = PanelUpdateRunWord.RolledBack,
            ["rollback-failed"] = PanelUpdateRunWord.RollbackFailed,
            ["interrupted"] = PanelUpdateRunWord.Interrupted,
        };

    /// <summary>Opens the scope the reporter is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per run, because the reporter holds scoped services.</param>
    public UpdateRequestReportCliCommand(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Verb
    {
        get
        {
            return "update-request-report";
        }
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
                "usage: update-request-report <installing|applied|unchanged|refused|rolled-back|rollback-failed|interrupted> <from-release> [<to-release>]");
            return new CliResult(CliExitCode.Usage);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<PanelUpdateRequestReporter>();
        await reporter.ReportAsync(report, cancellationToken);
        return new CliResult(CliExitCode.Success);
    }

    /// <summary>Reads the arguments into a report, or says they are not a valid request.</summary>
    /// <param name="arguments">Everything after the verb.</param>
    /// <param name="report">The report, when the arguments were acceptable.</param>
    /// <returns>True when a report was built.</returns>
    private static bool TryParse(IReadOnlyList<string> arguments, [NotNullWhen(true)] out UpdateRequestReport? report)
    {
        report = null;
        if (arguments.Count is < MinimumArgumentCount or > MaximumArgumentCount
            || !Words.TryGetValue(arguments[0], out var word))
        {
            return false;
        }

        return UpdateRequestReport.TryCreate(
            word,
            arguments[1],
            arguments.Count == MaximumArgumentCount ? arguments[2] : null,
            out report);
    }
}
