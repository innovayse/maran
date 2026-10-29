using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
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
/// <see cref="PlanNameDirectory"/>).
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

    /// <summary>Decides whether the requested name is already some other plan's.</summary>
    private readonly PlanNameDirectory _names;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves the answer's display name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="names">Decides whether the requested name is already some other plan's.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves the answer's display name in the request's culture.</param>
    public CreatePlanCommandHandler(
        AccountsDbContext dbContext,
        PlanNameDirectory names,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _names = names;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Creates the plan, refusing a name another plan already reads as.</summary>
    /// <param name="command">The validated plan; see <see cref="CreatePlanCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The created plan, with no accounts on it yet, or <c>PlanNameTaken</c>.</returns>
    public async Task<Result<PlanDto>> HandleAsync(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();

        if (await _names.IsTakenAsync(name, exceptPlanId: null, cancellationToken))
        {
            await _journal.RecordFailureAsync(
                AuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<PlanDto>.Fail(Error.Of(nameof(ErrorMessages.PlanNameTaken), ErrorType.Conflict));
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
            command.MaxPhpWorkersPerPool);

        _dbContext.Plans.Add(plan);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing: only 23505 means another creation won the name between the check
            // and this insert. Anything else (a dropped connection, a timeout) is not "taken".
            _dbContext.Plans.Remove(plan);

            await _journal.RecordFailureAsync(
                AuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<PlanDto>.Fail(Error.Of(nameof(ErrorMessages.PlanNameTaken), ErrorType.Conflict));
        }

        await _journal.RecordSuccessAsync(
            AuditActions.PlanCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<PlanDto>.Ok(PlanMapper.ToDto(plan, _displayNames, accountCount: 0));
    }
}
