using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.UpdateReseller;

/// <summary>
/// Handles <see cref="UpdateResellerCommand"/>: asks Identity to move the reseller's login to the new
/// name and address, then records them on the reseller.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is checked first, then the login is asked, then the row is written.</b> A new name is
/// refused when any account or reseller holds it (<c>ResellerNameTaken</c>, the same code as creation,
/// which says only that the name is taken and never which kind of tenant holds it). The login is asked
/// before the row is written because it is the half that can still refuse (<c>UsernameTaken</c>,
/// <c>EmailTaken</c>, from Identity), so a refusal changes nothing anywhere. A name the reseller
/// already has is not checked against the namespace: the only holder is the reseller itself.
/// </para>
/// <para>
/// <b>The two writes are two transactions</b>, as in <c>ChangeAccountOwnerEmailCommandHandler</c>.
/// When the login has moved and this row's save then fails, the login already carries the new name and
/// address, the direction the administrator asked for; repeating the change converges, because a login
/// already there is left as it is and the row is then written. The unique index on the reseller's name
/// decides two concurrent renames to one name: the loser's save raises and the request fails.
/// </para>
/// <para>
/// <b>The journal names the reseller, not the addresses.</b> The journal is never deleted and an
/// address is personal data; the subject is the name as it stood before the change, which is what an
/// operator searching for the reseller knows.
/// </para>
/// </remarks>
public sealed class UpdateResellerCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Answers whether a name is already some account's or some reseller's.</summary>
    private readonly AccountNamespaceClaims _claims;

    /// <summary>Measures what the reseller owns, for the response.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>The bus the login's change is requested on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="claims">Answers whether a name is already some account's or some reseller's.</param>
    /// <param name="usage">Measures what the reseller owns, for the response.</param>
    /// <param name="bus">The bus the login's change is requested on.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public UpdateResellerCommandHandler(
        AccountsDbContext dbContext,
        AccountNamespaceClaims claims,
        ResellerUsageReader usage,
        IMessageBus bus,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _claims = claims;
        _usage = usage;
        _bus = bus;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Renames the reseller and replaces its contact address and language, or refuses.</summary>
    /// <param name="command">The validated change; see <see cref="UpdateResellerCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The reseller as it now stands, <c>ResellerNotFound</c>, <c>ResellerNameTaken</c>, or Identity's
    /// <c>UsernameTaken</c> / <c>EmailTaken</c> when another login holds the name or the address.
    /// </returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        UpdateResellerCommand command,
        CancellationToken cancellationToken)
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

        var previousName = reseller.Name;
        var renamed = !string.Equals(previousName, command.Name, StringComparison.Ordinal);
        if (renamed && await _claims.IsNameTakenAsync(command.Name, cancellationToken))
        {
            return await FailAsync(
                command,
                previousName,
                Error.Of(nameof(ErrorMessages.ResellerNameTaken), ErrorType.Conflict),
                cancellationToken);
        }

        var login = await _bus.InvokeAsync<Result>(
            new ResellerLoginChangeRequested(reseller.Id, command.Name, command.ContactEmail), cancellationToken);
        if (!login.IsSuccess)
        {
            return await FailAsync(command, previousName, login.Error!, cancellationToken);
        }

        reseller.Rename(command.Name);
        reseller.UpdateContact(command.ContactEmail, command.Language);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerUpdated,
            previousName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ResellerDetailDto>> FailAsync(
        UpdateResellerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.ResellerUpdated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ResellerDetailDto>.Fail(error);
    }
}
