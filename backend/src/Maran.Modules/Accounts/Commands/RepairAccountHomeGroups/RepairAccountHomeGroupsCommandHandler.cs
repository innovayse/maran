using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;

/// <summary>
/// Handles <see cref="RepairAccountHomeGroupsCommand"/>: classifies every hosting account's home
/// group, refuses if what it would change is not what the operator confirmed, and only then acts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dry run is inside the write path, and that is the whole design.</b> Mirrors
/// <c>Databases.Commands.RepairDatabaseGrants.RepairDatabaseGrantsCommandHandler</c> exactly, because
/// it is the same shape a week ago: an operation that rewrites something live for every customer on
/// the host, gated behind a report-only pass the caller must confirm the count of before anything
/// changes. This handler runs the report-only pass itself and compares
/// <see cref="RepairAccountHomeGroupsCommand.ExpectedRepairCount"/> against what the host says NOW.
/// </para>
/// <para>
/// <b>Why the comparison is on the count and not on the rows.</b> A row-by-row match would refuse on a
/// change that does not matter and would put another tenant's account name into a request body. The
/// count is the figure the operator actually decided on.
/// </para>
/// <para>
/// <b>What the journal entry records, and why a refusal gets one too.</b> Both outcomes are written
/// through <see cref="AccountAuditJournal"/> under <c>AuditActions.AccountHomeGroupsRepaired</c>. The
/// success entry is the panel's only attributable record of who re-grouped every customer's home on
/// this host at once — the agent's own log does not know which operator asked. The refusal entry
/// records that the figure the caller confirmed did not describe the host, which is either an
/// ordinary race or an attempt to write without having read a report.
/// </para>
/// <para>
/// <b>The subject is a count, never an account name — this is the one deliberate difference from
/// <see cref="AccountAuditJournal"/>'s usual shape, and it is stated rather than silently taken.</b>
/// <see cref="AccountAuditJournal"/>'s own remarks say the subject is normally the account's name,
/// because most of that journal's operations act on ONE account a caller named. This operation acts on
/// every hosting account on the host at once, and several of its refusal reasons
/// (<c>OwnerMismatch</c>, <c>DifferentMount</c>) exist precisely because the row at a given path may
/// not be the account the panel's own naming expects — so a name in this trail could be wrong in a way
/// the panel cannot detect, and would look like an attributable fact when it is not one. Counts are
/// current, verifiable, and — per <c>rules/security.md</c>'s "the journal outlives whatever locale its
/// writer used" — this handler writes them in the same
/// <c>key=value;key=value</c> shape <c>RepairDatabaseGrantsCommandHandler</c> uses, unlocalized, so the
/// audit screen's subject column stays an identifier in every language rather than an English
/// sentence on a Russian page (the mistake that shape exists to prevent, per that handler's own
/// remarks).
/// </para>
/// </remarks>
public sealed class RepairAccountHomeGroupsCommandHandler
{
    /// <summary>The agent client that owns the host's accounts.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Resolver for the two sentences each refused home carries.</summary>
    private readonly HomeGroupRepairRefusalDisplayNames _refusalText;

    /// <summary>The module's audit journal, written on the repair and on the refusal alike.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads and re-groups the host's accounts.</param>
    /// <param name="refusalText">Resolver for the refusal name and the advice beside it.</param>
    /// <param name="journal">The module's audit journal.</param>
    public RepairAccountHomeGroupsCommandHandler(
        IAgentAccountsClient agent,
        HomeGroupRepairRefusalDisplayNames refusalText,
        AccountAuditJournal journal)
    {
        _agent = agent;
        _refusalText = refusalText;
        _journal = journal;
    }

    /// <summary>Repairs the host's home groups, or refuses because the report the caller read has moved.</summary>
    /// <param name="command">The confirmed figure, and who is asking.</param>
    /// <param name="cancellationToken">Cancels either agent call.</param>
    /// <returns>
    /// The census of what was actually changed; the agent's own typed failure; or
    /// <c>AccountHomeGroupRepairPlanChanged</c> as a conflict when the host no longer matches the
    /// report.
    /// </returns>
    public async Task<Result<HomeGroupRepairReportDto>> HandleAsync(
        RepairAccountHomeGroupsCommand command,
        CancellationToken cancellationToken)
    {
        var planned = await _agent.RepairHomeGroupsAsync(reportOnly: true, cancellationToken);
        if (!planned.IsSuccess)
        {
            return Result<HomeGroupRepairReportDto>.Fail(planned.Error!);
        }

        // The gate. Nothing below this line runs for a caller who has not read the same figures the
        // host is reporting right now.
        if (planned.Value.WouldRepair.Count != command.ExpectedRepairCount)
        {
            await RecordAsync(
                command,
                succeeded: false,
                $"confirmed={command.ExpectedRepairCount};hostReports={planned.Value.WouldRepair.Count}",
                cancellationToken);

            return Result<HomeGroupRepairReportDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountHomeGroupRepairPlanChanged), ErrorType.Conflict));
        }

        var repaired = await _agent.RepairHomeGroupsAsync(reportOnly: false, cancellationToken);
        if (!repaired.IsSuccess)
        {
            await RecordAsync(command, succeeded: false, repaired.Error!.Code, cancellationToken);

            return Result<HomeGroupRepairReportDto>.Fail(repaired.Error!);
        }

        await RecordAsync(
            command,
            succeeded: true,
            $"examined={repaired.Value.Examined};repaired={repaired.Value.Repaired.Count}"
            + $";refused={repaired.Value.Refused.Count}",
            cancellationToken);

        return Result<HomeGroupRepairReportDto>.Ok(
            HomeGroupRepairReportMapper.ToDto(repaired.Value, isReportOnly: false, _refusalText));
    }

    /// <summary>Writes the one journal entry this operation produces, in either outcome.</summary>
    /// <param name="command">The caller, for the address and user agent the entry is stamped with.</param>
    /// <param name="succeeded">Whether the repair took effect.</param>
    /// <param name="subject">
    /// The counts, or the machine code of the failure — never an account name; see this type's
    /// remarks.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task RecordAsync(
        RepairAccountHomeGroupsCommand command,
        bool succeeded,
        string subject,
        CancellationToken cancellationToken)
    {
        if (succeeded)
        {
            await _journal.RecordSuccessAsync(
                AuditActions.AccountHomeGroupsRepaired,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return;
        }

        await _journal.RecordFailureAsync(
            AuditActions.AccountHomeGroupsRepaired,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
