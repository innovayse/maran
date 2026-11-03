using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace Maran.Modules.Accounts.Commands.CreatePlan;

/// <summary>
/// Handles <see cref="CreatePlanCommand"/>: defines a plan an administrator named, so an operator can
/// sell something other than the tiers the panel shipped.
/// </summary>
/// <remarks>
/// <para>
/// Creating a plan touches nothing but its own row. No account is on it yet, so there is no agent
/// call to make and nothing already running whose limits move; the only thing that can refuse it is
/// a name another plan already reads as (<c>PlanNameTaken</c>, decided by
/// <see cref="PlanNameDirectory"/>) — and, for a reseller, its envelope.
/// </para>
/// <para>
/// <b>A reseller's plan is the reseller's own.</b> The owner is read from the caller's token, never from
/// the request: a reseller creates plans for itself and an administrator for no reseller, so nothing a
/// body says can make a plan another reseller's. A reseller's plan must fit under every ceiling of its
/// envelope and under its plan count (<see cref="ResellerAllocationGuard.AdmitNewPlanAsync"/>); a plan
/// over a ceiling is answered <c>ResellerPlanExceedsEnvelope</c> naming the figures. The admission claims
/// the reseller's row, and a second plan admitted against the same free slot loses it at the save.
/// </para>
/// <para>
/// The name is trimmed before it is compared and stored, so " Gold" and "Gold" cannot be two plans
/// that look identical in a list. The unique index on the stored name is the backstop for two
/// administrators creating the same name in the same instant: the loser of that race is caught at the
/// save — a unique violation (<c>SqlState 23505</c> and nothing else) — and answered with the same
/// <c>PlanNameTaken</c> and the same journal entry as the check above, never a server error. Any other
/// database failure is not a taken name and propagates.
/// </para>
/// </remarks>
public sealed class CreatePlanCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Says who owns what the caller creates, and whether a suspended reseller may still write.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Holds the plan to the owner's envelope and claims the owner's row.</summary>
    private readonly ResellerAllocationGuard _allocation;

    /// <summary>Names the owning reseller in the answer.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>Decides whether the requested name is already some other plan's.</summary>
    private readonly PlanNameDirectory _names;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves the answer's display name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Says who owns what the caller creates, and whether a suspended reseller may still write.</param>
    /// <param name="allocation">Holds the plan to the owner's envelope and claims the owner's row.</param>
    /// <param name="resellerNames">Names the owning reseller in the answer.</param>
    /// <param name="names">Decides whether the requested name is already some other plan's.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves the answer's display name in the request's culture.</param>
    public CreatePlanCommandHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        ResellerAllocationGuard allocation,
        ResellerNameDirectory resellerNames,
        PlanNameDirectory names,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _scope = scope;
        _allocation = allocation;
        _resellerNames = resellerNames;
        _names = names;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Creates the plan, refusing a name another plan already reads as.</summary>
    /// <param name="command">The validated plan; see <see cref="CreatePlanCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The created plan, with no accounts on it yet, <c>PlanNameTaken</c>, <c>ResellerSuspended</c>, or
    /// for a reseller <c>ResellerPlanExceedsEnvelope</c> (carrying the offending figures) or
    /// <c>ResellerPlanLimitReached</c>.
    /// </returns>
    public async Task<Result<PlanDto>> HandleAsync(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();

        var inactive = await _scope.RequireActiveAsync(cancellationToken);
        if (inactive is not null)
        {
            return await RefuseAsync(command, name, new ResellerRefusal(inactive), cancellationToken);
        }

        var ownerId = _scope.CallerResellerId;

        if (await _names.IsTakenAsync(name, ownerId, exceptPlanId: null, cancellationToken))
        {
            await _journal.RecordFailureAsync(
                AccountAuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<PlanDto>.Fail(Error.Of(nameof(ErrorMessages.PlanNameTaken), ErrorType.Conflict));
        }

        var refusal = await _allocation.AdmitNewPlanAsync(
            ownerId, PlanLimitsMapper.ToLimits(command), cancellationToken);
        if (refusal is not null)
        {
            return await RefuseAsync(command, name, refusal, cancellationToken);
        }

        var plan = Plan.Define(
            Guid.NewGuid(),
            name,
            command.DiskQuotaMb,
            command.MaxSites,
            command.MaxDatabases,
            command.MaxSftpUsers,
            command.MaxFtpUsers,
            command.MaxCronEntries,
            command.MaxPhpWorkersPerPool,
            command.Features.ToSet(),
            resellerId: ownerId);
        plan.ChangeRuntimeLimits(RuntimeLimitsMapper.ToDomain(command));

        _dbContext.Plans.Add(plan);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner's row was claimed by another write between the admission above and this save,
            // so the plan slot the admission counted is gone.
            _dbContext.Plans.Remove(plan);

            return await RefuseAsync(
                command,
                name,
                new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerPlanLimitReached), ErrorType.Conflict)),
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing: only 23505 means another creation won the name between the check
            // and this insert. Anything else (a dropped connection, a timeout) is not "taken".
            _dbContext.Plans.Remove(plan);

            await _journal.RecordFailureAsync(
                AccountAuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<PlanDto>.Fail(Error.Of(nameof(ErrorMessages.PlanNameTaken), ErrorType.Conflict));
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<PlanDto>.Ok(
            PlanMapper.ToDto(
                plan,
                _displayNames,
                accountCount: 0,
                await _resellerNames.NameOfAsync(plan.ResellerId, cancellationToken)));
    }

    /// <summary>Journals a creation the reseller rules refused and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="name">The plan's name, the subject an operator will search for.</param>
    /// <param name="refusal">The refusal to answer with, with the offending figures when it has any.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying the refusal.</returns>
    private async Task<Result<PlanDto>> RefuseAsync(
        CreatePlanCommand command,
        string name,
        ResellerRefusal refusal,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

        return ResellerRefusalMapper.ToFailure<PlanDto>(refusal);
    }
}
