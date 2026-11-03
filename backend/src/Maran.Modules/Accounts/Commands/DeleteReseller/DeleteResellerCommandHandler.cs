using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.DeleteReseller;

/// <summary>
/// Handles <see cref="DeleteResellerCommand"/>: refuses while the reseller owns anything, asks Identity
/// to remove its login, then removes the row.
/// </summary>
/// <remarks>
/// <para>
/// <b>A reseller that still owns accounts or plans is never deleted</b> (<c>ResellerHasAccounts</c>).
/// The panel does not decide what becomes of a reseller's customers: moving an account between owners
/// does not exist, and deleting accounts as a side effect of removing their seller would be the one
/// destructive cascade nobody asked for. The administrator deletes them, deliberately and one at a time
/// through the operation that takes the final backup, and then deletes the reseller.
/// </para>
/// <para>
/// <b>Ordering.</b> The check, then <see cref="ResellerDeleting"/> INVOKED inline so the login and its
/// sessions go first and a subscriber that throws aborts the deletion with the reseller intact (the
/// exception reaches the caller as a failed request), then the row. The foreign keys from accounts and
/// plans to the reseller are RESTRICT, so the database refuses a deletion over anything the check did
/// not see.
/// </para>
/// <para>
/// <b>The one window, stated.</b> If the reseller creates an account in the moments between the check
/// and the delete, the database refuses the row and this handler answers <c>ResellerHasAccounts</c>,
/// but the login is already gone. The reseller then exists without a login and an administrator
/// deletes it after removing what it created. Nothing a customer owns is lost either way, which is why
/// the window is accepted rather than closed by reordering: the event is announced BEFORE the removal so
/// that a subscriber's failure can still stop it.
/// </para>
/// </remarks>
public sealed class DeleteResellerCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The bus <see cref="ResellerDeleting"/> is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="bus">The bus <see cref="ResellerDeleting"/> is invoked on.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteResellerCommandHandler(AccountsDbContext dbContext, IMessageBus bus, AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
    }

    /// <summary>Removes the reseller the command names.</summary>
    /// <param name="command">Which reseller to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, <c>ResellerNotFound</c>, or <c>ResellerHasAccounts</c>.</returns>
    public async Task<Result> HandleAsync(DeleteResellerCommand command, CancellationToken cancellationToken)
    {
        var reseller = await _dbContext.Resellers
            .SingleOrDefaultAsync(r => r.Id == command.ResellerId, cancellationToken);
        if (reseller is null)
        {
            // Nothing exists to name, so the subject is the identifier the caller supplied.
            return await FailAsync(
                command,
                command.ResellerId.ToString("D"),
                Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var ownsAccounts = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.ResellerId == reseller.Id, cancellationToken);
        var ownsPlans = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(p => p.ResellerId == reseller.Id, cancellationToken);
        if (ownsAccounts || ownsPlans)
        {
            return await FailAsync(
                command,
                reseller.Name,
                Error.Of(nameof(ErrorMessages.ResellerHasAccounts), ErrorType.Conflict),
                cancellationToken);
        }

        await _bus.InvokeAsync(new ResellerDeleting(reseller.Id), cancellationToken);

        _dbContext.Resellers.Remove(reseller);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The ONE narrowing: only 23503 means an account or a plan was created for the reseller
            // between the check and the delete. The row is still there, so the refusal names it.
            return await FailAsync(
                command,
                reseller.Name,
                Error.Of(nameof(ErrorMessages.ResellerHasAccounts), ErrorType.Conflict),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerDeleted, reseller.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The reseller's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteResellerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.ResellerDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
