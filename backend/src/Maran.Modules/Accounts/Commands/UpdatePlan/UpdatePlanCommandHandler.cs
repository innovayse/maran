using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Commands.UpdatePlan;

/// <summary>
/// Handles <see cref="UpdatePlanCommand"/>: replaces a plan's name and limits — and with them the
/// limits of every account already on it, because an account reads its limits through its plan and
/// keeps no copy of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision: a disk quota is never lowered below what an account already uses. The edit is
/// REFUSED, whole, and nothing is changed</b> (<c>PlanQuotaBelowUsage</c>). Reasons, in the order
/// they decided it:
/// </para>
/// <list type="number">
/// <item>
/// <b>A disk quota is not a number the panel displays; it is a limit the kernel enforces.</b> It is
/// written as a hard limit (soft and hard are set equal, so there is no grace period). An account put
/// under a quota smaller than its files is therefore not "trimmed": it is over its limit, and every
/// write it makes fails at once — a customer's uploads, session files, database dumps, mail and log
/// rotation — for customers who did nothing, from one click on a form.
/// </item>
/// <item>
/// <b>The panel has no way to make the room.</b> It must not delete a customer's data, so the only
/// thing a lowered quota could do to an account that does not fit is break it.
/// </item>
/// <item>
/// <b>The alternatives were worse.</b> Applying it anyway and letting accounts sit over quota is the
/// outage above, delivered silently. Applying it to the accounts that fit and skipping the rest
/// leaves one plan with two quotas, so "the limits of this plan" stops being one answer. Grandfathering
/// the oversized accounts needs a per-account override this model does not have — a plan is the only
/// place a limit lives — and adding one to make a single edit possible is a larger decision than this
/// edit.
/// </item>
/// <item>
/// <b>A refusal is the one outcome on which the plan row, the kernel and the customer's experience
/// all agree</b>, and it is recoverable: the administrator raises the figure, asks the customers
/// concerned to free space, or moves them to another plan, and tries again.
/// </item>
/// </list>
/// <para>
/// Usage is read from the agent for EVERY account on the plan before any quota is written, so a
/// refusal never leaves some accounts changed. A usage that cannot be read is refused with the
/// agent's own typed failure rather than assumed to fit: a check that passes when it cannot look is
/// not a check. An account holding exactly the new quota still fits (<c>used &gt; new</c> is the test);
/// it is full, not over.
/// </para>
/// <para>
/// <b>When the figure moves — up, or down to one every account fits under — it is pushed to every
/// account's quota on the host</b>, agent first, row second, the order every handler in this module
/// uses. Without the push the plan would say 10 GB while the kernel went on enforcing the 5 GB it
/// was created with: a number the panel shows and the host does not keep. A failure part-way (the
/// agent refuses the fifth of nine accounts) is reported with the agent's code and the plan row stays
/// as it was; the accounts already reached hold the new figure. That is safe, because a quota the
/// accounts fit under is harmless, and it converges: repeating the same edit sets the same figures
/// again, the agent's quota call being a replacement rather than an increment. Nothing is pushed when
/// the quota is unchanged, so editing a name or a count never needs the agent.
/// </para>
/// <para>
/// <b>The count limits (sites, databases, logins, cron entries, workers) are not reconciled with
/// anything</b>, and that is the difference from disk, not an omission. They gate CREATION: an account
/// already holding more than a lowered limit keeps everything it has and is simply refused the next
/// one. Nothing existing is removed or broken, so there is no outage to refuse. Worker budgets reach
/// the host when a pool is next rendered.
/// </para>
/// <para>
/// <b>The name.</b> Sending the name the plan already shows keeps a shipped plan's translated name;
/// any other text becomes the plan's name in every language. A name another plan already reads as is
/// refused (<c>PlanNameTaken</c>); a plan may keep its own.
/// </para>
/// <para>
/// <b>The journal subject is the plan's stored identity as it was before the edit</b> — the
/// operator's text, or a shipped plan's resource key, never a translation, because the journal is
/// never deleted and an entry that reads differently per reader's language cannot be searched. A
/// rename is recorded as <c>old -&gt; new</c>.
/// </para>
/// </remarks>
public sealed class UpdatePlanCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns the disk quota of every account on the host.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Decides whether the requested name is already some other plan's.</summary>
    private readonly PlanNameDirectory _names;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves display names in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent, which owns the disk quota of every account on the host.</param>
    /// <param name="names">Decides whether the requested name is already some other plan's.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves display names in the request's culture.</param>
    public UpdatePlanCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        PlanNameDirectory names,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _agent = agent;
        _names = names;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Applies the edit, or refuses it whole.</summary>
    /// <param name="command">The validated plan; see <see cref="UpdatePlanCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The plan as it now stands, <c>PlanNotFound</c>, <c>PlanNameTaken</c>,
    /// <c>PlanQuotaBelowUsage</c> when an account already uses more than the new disk quota, or the
    /// agent's own typed failure when usage could not be read or a quota could not be applied.
    /// </returns>
    public async Task<Result<PlanDto>> HandleAsync(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans.SingleOrDefaultAsync(p => p.Id == command.PlanId, cancellationToken);
        if (plan is null)
        {
            // Nothing exists to name, so the subject is the identifier the caller supplied: a probe
            // for a plan that is not there still leaves a trace of what was probed for.
            return await FailAsync(
                command, command.PlanId.ToString("D"), Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        var subject = plan.DisplayNameKey;
        var name = command.Name.Trim();
        var renames = !string.Equals(name, PlanMapper.DisplayNameOf(plan, _displayNames), StringComparison.Ordinal);

        if (renames && await _names.IsTakenAsync(name, plan.Id, cancellationToken))
        {
            return await FailAsync(
                command, subject, Error.Of(nameof(ErrorMessages.PlanNameTaken), ErrorType.Conflict), cancellationToken);
        }

        var quotaFailure = await ReconcileDiskQuotaAsync(plan, command.DiskQuotaMb, cancellationToken);
        if (quotaFailure is not null)
        {
            return await FailAsync(command, subject, quotaFailure, cancellationToken);
        }

        if (renames)
        {
            plan.Rename(name);
        }

        plan.ChangeLimits(
            command.DiskQuotaMb,
            command.MaxSites,
            command.MaxDatabases,
            command.MaxSftpUsers,
            command.MaxFtpUsers,
            command.MaxCronEntries,
            command.MaxPhpWorkersPerPool);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.PlanUpdated,
            renames ? $"{subject} -> {name}" : subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var accountCount = await _dbContext.Accounts.AsNoTracking().CountAsync(a => a.PlanId == plan.Id, cancellationToken);

        return Result<PlanDto>.Ok(PlanMapper.ToDto(plan, _displayNames, accountCount));
    }

    /// <summary>
    /// Brings every account on the plan to <paramref name="newQuotaMb"/> on the host, after proving
    /// that a lowered figure does not put any of them over its limit. Does nothing when the figure is
    /// unchanged.
    /// </summary>
    /// <param name="plan">The plan being edited, still holding its current quota.</param>
    /// <param name="newQuotaMb">The disk quota the edit asks for, in megabytes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// <c>null</c> when every account is at the new quota (or none needed it); otherwise the failure
    /// that stopped it. When it is a refusal for usage, no quota was written to any account.
    /// </returns>
    private async Task<Error?> ReconcileDiskQuotaAsync(Plan plan, int newQuotaMb, CancellationToken cancellationToken)
    {
        if (newQuotaMb == plan.DiskQuotaMb)
        {
            return null;
        }

        var accountNames = await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.PlanId == plan.Id)
            .OrderBy(a => a.Name)
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);
        var newQuotaBytes = QuotaBytes(newQuotaMb);

        if (newQuotaMb < plan.DiskQuotaMb)
        {
            // Every account is measured BEFORE any is changed, so a refusal leaves none of them
            // touched. See the type's remarks for why a lowered quota that does not fit is refused.
            foreach (var accountName in accountNames)
            {
                var usage = await _agent.GetUsageAsync(accountName, cancellationToken);
                if (!usage.IsSuccess)
                {
                    return usage.Error!;
                }

                if (usage.Value.UsedBytes > newQuotaBytes)
                {
                    return Error.Of(nameof(ErrorMessages.PlanQuotaBelowUsage), ErrorType.Conflict);
                }
            }
        }

        foreach (var accountName in accountNames)
        {
            var applied = await _agent.SetQuotaAsync(accountName, newQuotaBytes, cancellationToken);
            if (!applied.IsSuccess)
            {
                return applied.Error!;
            }
        }

        return null;
    }

    /// <summary>Journals a refused edit and returns it as the typed failure.</summary>
    /// <param name="command">The edit that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<PlanDto>> FailAsync(
        UpdatePlanCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.PlanUpdated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<PlanDto>.Fail(error);
    }

    /// <summary>Converts a plan's disk allowance to the bytes the agent's quota calls use.</summary>
    /// <param name="quotaMb">The quota in megabytes.</param>
    /// <returns>The quota in bytes.</returns>
    private static ulong QuotaBytes(int quotaMb)
    {
        return (ulong)quotaMb * 1024UL * 1024UL;
    }
}
