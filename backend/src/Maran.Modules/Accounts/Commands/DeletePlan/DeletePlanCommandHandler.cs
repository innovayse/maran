using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Commands.DeletePlan;

/// <summary>
/// Handles <see cref="DeletePlanCommand"/>: removes a plan nothing is on, and REFUSES one that
/// accounts still reference.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision: a plan with accounts on it is never deleted, and the answer is a sentence, not a
/// database error</b> (<c>PlanInUse</c>, a conflict). <c>Accounts.PlanId</c> is a foreign key with
/// <c>ON DELETE RESTRICT</c>, so the database would refuse the delete anyway — but with a
/// constraint-violation exception that reaches the administrator as a generic server error, naming
/// no plan and no remedy. This handler asks first and answers in the administrator's language.
/// </para>
/// <para>
/// It does not offer to reassign the accounts, and that is deliberate: moving a customer to another
/// plan changes what they are sold and, if the disk figure differs, what the kernel enforces on their
/// files. That is a decision about specific customers, made per account, and not a side effect of
/// tidying up a plan. The administrator moves them (or deletes them) and then deletes the plan.
/// </para>
/// <para>
/// The check and the delete are two statements, so an account created onto the plan in the instant
/// between them is caught by the foreign key and surfaces as the database's refusal. That window is
/// the length of one round trip and the consequence is a refused delete, never a plan removed from
/// under an account; closing it would need a lock on the accounts table for a form nobody submits in
/// a race.
/// </para>
/// <para>
/// A shipped plan can be deleted like any other. The seeder inserts the standard plans only into a
/// panel that has none (see <see cref="Seeders.PlanSeeder"/>), so a deletion is not undone at the next
/// restart.
/// </para>
/// </remarks>
public sealed class DeletePlanCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeletePlanCommandHandler(AccountsDbContext dbContext, AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Deletes the plan, or refuses because accounts are still on it.</summary>
    /// <param name="command">The plan to delete.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Success, <c>PlanNotFound</c>, or <c>PlanInUse</c> while any account references the plan.</returns>
    public async Task<Result<bool>> HandleAsync(DeletePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans.SingleOrDefaultAsync(p => p.Id == command.PlanId, cancellationToken);
        if (plan is null)
        {
            // Nothing exists to name, so the subject is the identifier the caller supplied.
            return await FailAsync(
                command, command.PlanId.ToString("D"), Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        var inUse = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PlanId == plan.Id, cancellationToken);
        if (inUse)
        {
            return await FailAsync(
                command, plan.DisplayNameKey, Error.Of(nameof(ErrorMessages.PlanInUse), ErrorType.Conflict), cancellationToken);
        }

        _dbContext.Plans.Remove(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.PlanDeleted, plan.DisplayNameKey, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DeletePlanCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.PlanDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
