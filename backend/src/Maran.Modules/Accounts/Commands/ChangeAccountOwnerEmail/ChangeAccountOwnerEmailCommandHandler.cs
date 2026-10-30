using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;

/// <summary>
/// Handles <see cref="ChangeAccountOwnerEmailCommand"/>: moves the account's login to the new
/// address, then records it as the account's owner address.
/// </summary>
/// <remarks>
/// <para>
/// <b>The login is asked first, because it is the half that can refuse.</b> Identity owns the
/// login's address and refuses one another login already holds (<c>EmailTaken</c>), through
/// <see cref="AccountLoginEmailChangeRequested"/> — Accounts may not reference Identity
/// (rules/architecture.md). Asking first means a refusal changes nothing anywhere.
/// </para>
/// <para>
/// <b>The two writes are two transactions.</b> When the login has moved and this row's save then
/// fails, the request answers with the failure and the login already mails the new address — the
/// direction the administrator asked for. Repeating the change converges: the login is already
/// there, and the row is written.
/// </para>
/// <para>
/// <b>The journal names the account, not the addresses.</b> The journal is never deleted and an
/// address is personal data; the account's name is what an operator searches for.
/// </para>
/// </remarks>
public sealed class ChangeAccountOwnerEmailCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The bus the login's change is requested on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves a shipped plan's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="bus">The bus the login's change is requested on.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    public ChangeAccountOwnerEmailCommandHandler(
        AccountsDbContext dbContext,
        IMessageBus bus,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Changes the owner address, or refuses.</summary>
    /// <param name="command">The validated change; see <see cref="ChangeAccountOwnerEmailCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The account as it now stands, <c>AccountNotFound</c>, or <c>EmailTaken</c> when another login
    /// already holds the address.
    /// </returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(
        ChangeAccountOwnerEmailCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // Nothing exists to name, so the subject is the identifier the caller supplied.
            return await FailAsync(
                command,
                command.AccountId.ToString("D"),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var login = await _bus.InvokeAsync<Result<bool>>(
            new AccountLoginEmailChangeRequested(account.Id, command.OwnerEmail), cancellationToken);
        if (!login.IsSuccess)
        {
            return await FailAsync(command, account.Name, login.Error!, cancellationToken);
        }

        account.ChangeOwnerEmail(command.OwnerEmail);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountOwnerEmailChanged, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        var plan = await _dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == account.PlanId, cancellationToken);
        return Result<AccountDetailDto>.Ok(AccountDetailMapper.ToDto(account, plan, _displayNames));
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDetailDto>> FailAsync(
        ChangeAccountOwnerEmailCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountOwnerEmailChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDetailDto>.Fail(error);
    }
}
