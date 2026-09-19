using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Databases.Commands.RepairDatabaseGrants;

/// <summary>
/// Handles <see cref="RepairDatabaseGrantsCommand"/>: classifies the database server's grant table,
/// refuses if what it would change is not what the operator confirmed, and only then acts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dry run is inside the write path, and that is the whole design.</b> The agent's rpc has a
/// report-only mode so that an operation which rewrites live database access can be inspected before
/// it acts; a panel that merely offered the report beside a button would leave the button reachable
/// without it. So this handler runs the report-only pass itself and compares
/// <see cref="RepairDatabaseGrantsCommand.ExpectedRepairCount"/> against what the server says NOW. The
/// consequences are two, and both were wanted: an operator cannot repair without having read a report,
/// and a report that has gone stale between reading and acting is refused instead of acted on.
/// </para>
/// <para>
/// <b>The cost of that choice, stated rather than discovered.</b> The grant table is read twice, so the
/// repair is not atomic with its own check: a row could change in the window between the two passes.
/// That is not a hole this design pretends to close — the agent's repair re-validates every row against
/// the same four conditions before it touches it, and the comparison here is about what the OPERATOR
/// was shown, not about locking the server. A refusal is the cheap outcome; the expensive one, acting
/// on a list that no longer describes the host, is what it prevents.
/// </para>
/// <para>
/// <b>Why the comparison is on the count and not on the rows.</b> A row-by-row match would refuse on a
/// change that does not matter — an unrelated database created between the two passes moves an exposure
/// list without changing what will be rewritten — and would put another tenant's names into a request
/// body. The count is the figure the operator actually decided on.
/// </para>
/// <para>
/// <b>What the journal entry records, and why a refusal gets one too.</b> Both outcomes are written
/// through <c>DatabaseAuditJournal</c> under <c>AuditActions.DatabaseGrantsRepaired</c>. The success
/// entry is the panel's only attributable record of who narrowed live access for every customer on
/// this host at once — the agent logs the rows it touched, but the host's log does not know which
/// operator asked. The refusal entry records that the figure the caller confirmed did not describe
/// the server, which is either an ordinary race or an attempt to write without having read a report,
/// and only the journal makes the second visible as a pattern.
/// </para>
/// <para>
/// <b>The subject is a count, never a name.</b> A row is refused precisely because this panel did not
/// write it, so its <c>Db</c> and <c>User</c> columns hold another tenant's identifiers or the
/// operator's own credential — and this journal is never deleted (rules/security.md). The counts say
/// everything an operator reading the trail needs and disclose nothing the trail should not keep.
/// </para>
/// <para>
/// <b>And the subject is machine-readable, not a sentence — which it was, once.</b> The first version
/// wrote <c>narrowed 1 of 1 grants, refused 0</c>, and a live check found it sitting in the audit
/// screen's subject column in english on a russian page. That column is not translated anywhere in the
/// panel, because every other subject in it is an identifier: a username, a database name, a site's
/// hostname. English prose there is neither a name nor a translated sentence, so it is the one form
/// that is wrong in every language. The <c>key=value;key=value</c> shape a caller can parse and an
/// operator can still read is the resolution; what must NEVER be done instead is to localize it, since
/// the journal outlives the locale the writer happened to be using.
/// </para>
/// </remarks>
public sealed class RepairDatabaseGrantsCommandHandler
{
    /// <summary>The agent client that owns the database server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>Resolver for the two sentences each refused row carries.</summary>
    private readonly GrantRepairRefusalDisplayNames _refusalText;

    /// <summary>The module's audit journal, written on the repair and on the refusal alike.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads and rewrites the grant table.</param>
    /// <param name="refusalText">Resolver for the refusal name and the advice beside it.</param>
    /// <param name="journal">The module's audit journal.</param>
    public RepairDatabaseGrantsCommandHandler(
        IAgentDbClient agent,
        GrantRepairRefusalDisplayNames refusalText,
        DatabaseAuditJournal journal)
    {
        _agent = agent;
        _refusalText = refusalText;
        _journal = journal;
    }

    /// <summary>Repairs the host's grants, or refuses because the report the caller read has moved.</summary>
    /// <param name="command">The confirmed figure, and who is asking.</param>
    /// <param name="cancellationToken">Cancels either agent call.</param>
    /// <returns>
    /// The census of what was actually changed; the agent's own typed failure; or
    /// <c>DatabaseGrantRepairPlanChanged</c> as a conflict when the server no longer matches the report.
    /// </returns>
    public async Task<Result<GrantRepairReportDto>> HandleAsync(
        RepairDatabaseGrantsCommand command,
        CancellationToken cancellationToken)
    {
        var planned = await _agent.RepairGrantsAsync(reportOnly: true, cancellationToken);
        if (!planned.IsSuccess)
        {
            return Result<GrantRepairReportDto>.Fail(planned.Error!);
        }

        // The gate. Nothing below this line runs for a caller who has not read the same figures the
        // server is reporting right now.
        if (planned.Value.WouldRepair.Count != command.ExpectedRepairCount)
        {
            await RecordAsync(
                command,
                succeeded: false,
                $"confirmed={command.ExpectedRepairCount};hostReports={planned.Value.WouldRepair.Count}",
                cancellationToken);

            return Result<GrantRepairReportDto>.Fail(
                Error.Of(nameof(ErrorMessages.DatabaseGrantRepairPlanChanged), ErrorType.Conflict));
        }

        var repaired = await _agent.RepairGrantsAsync(reportOnly: false, cancellationToken);
        if (!repaired.IsSuccess)
        {
            await RecordAsync(command, succeeded: false, repaired.Error!.Code, cancellationToken);

            return Result<GrantRepairReportDto>.Fail(repaired.Error!);
        }

        await RecordAsync(
            command,
            succeeded: true,
            $"examined={repaired.Value.ExaminedGrants};narrowed={repaired.Value.Repaired.Count}"
            + $";refused={repaired.Value.Refused.Count}",
            cancellationToken);

        return Result<GrantRepairReportDto>.Ok(
            GrantRepairReportMapper.ToDto(repaired.Value, isReportOnly: false, _refusalText));
    }

    /// <summary>Writes the one journal entry this operation produces, in either outcome.</summary>
    /// <param name="command">The caller, for the address and user agent the entry is stamped with.</param>
    /// <param name="succeeded">Whether the repair took effect.</param>
    /// <param name="subject">
    /// The counts, or the machine code of the failure. Never a <c>Db</c> or <c>User</c> column: see
    /// this type's remarks.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task RecordAsync(
        RepairDatabaseGrantsCommand command,
        bool succeeded,
        string subject,
        CancellationToken cancellationToken)
    {
        if (succeeded)
        {
            await _journal.RecordSuccessAsync(
                AuditActions.DatabaseGrantsRepaired,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return;
        }

        await _journal.RecordFailureAsync(
            AuditActions.DatabaseGrantsRepaired,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
