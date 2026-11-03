using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.CreateReseller;

/// <summary>
/// Handles <see cref="CreateResellerCommand"/>: records the reseller and what it was sold, then asks
/// Identity, through <see cref="ResellerCreated"/>, to issue the login it signs in with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row first, the login second.</b> A reseller has no state on the host, so unlike an account
/// there is nothing to provision before the row exists. The login is the part that can fail after the
/// commit; when it does the reseller is reported with its own code (<c>ResellerLoginIssuanceFailed</c>)
/// and is NOT rolled back, exactly as <c>CreateAccountCommandHandler</c> treats an owner's login: the
/// success entry already in the journal stands, and creating the reseller again would only meet
/// <c>ResellerNameTaken</c>. The recovery is to delete the reseller, which owns nothing yet, and create
/// it again once the cause in the server log is fixed.
/// </para>
/// <para>
/// <b>One namespace with account names.</b> The reseller's name is also its login name, so a name an
/// account already holds is refused with <c>ResellerNameTaken</c>, the same code as a name another
/// reseller holds: the answer says only that the name is taken, never which kind of tenant holds it.
/// The unique index on the reseller's name decides two concurrent creations of one name; a concurrent
/// ACCOUNT of the same name is decided by Identity's unique login name, which fails the login issuance
/// and so reports <c>ResellerLoginIssuanceFailed</c>.
/// </para>
/// </remarks>
public sealed class CreateResellerCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for an Identity subscriber that failed to issue the reseller's login.
    /// The subscriber's own exception text has no place in a customer-facing message
    /// (rules/security.md item 8): it is logged here, for the operator, and never returned.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogLoginIssuanceFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateResellerCommandHandler)),
            "Reseller {ResellerName} was created, but its login could not be issued.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The bus <see cref="ResellerCreated"/> is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's failure to issue the reseller's login is recorded.</summary>
    private readonly ILogger<CreateResellerCommandHandler> _logger;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="clock">The injected time source used to stamp the reseller's creation time.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="bus">The bus <see cref="ResellerCreated"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure to issue the login is recorded.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public CreateResellerCommandHandler(
        AccountsDbContext dbContext,
        IClock clock,
        AccountAuditJournal journal,
        IMessageBus bus,
        ILogger<CreateResellerCommandHandler> logger,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _bus = bus;
        _logger = logger;
        _displayNames = displayNames;
    }

    /// <summary>Creates the reseller, guarding against a name another reseller or an account holds.</summary>
    /// <param name="command">The validated reseller parameters; see <see cref="CreateResellerCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The created reseller, <c>ResellerNameTaken</c>, or <c>ResellerLoginIssuanceFailed</c> when the
    /// reseller was created but its login was not.
    /// </returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        CreateResellerCommand command,
        CancellationToken cancellationToken)
    {
        var resellerTaken = await _dbContext.Resellers
            .AsNoTracking()
            .AnyAsync(reseller => reseller.Name == command.Name, cancellationToken);
        var accountTaken = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(account => account.Name == command.Name, cancellationToken);
        if (resellerTaken || accountTaken)
        {
            return await FailAsync(command, cancellationToken);
        }

        var reseller = new Reseller(
            Guid.NewGuid(),
            command.Name,
            command.ContactEmail,
            command.Language,
            ResellerMapper.ToDomain(command.Envelope),
            _clock.UtcNow);

        _dbContext.Resellers.Add(reseller);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing: only 23505 means another creation won the name between the check and
            // this insert. Anything else (a dropped connection, a timeout) is not "taken".
            _dbContext.Resellers.Remove(reseller);

            return await FailAsync(command, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerCreated, reseller.Name, command.IpAddress, command.UserAgent, cancellationToken);

        // INVOKED, not published: a failure to issue the login must reach the caller. The reseller
        // already exists and is not rolled back (see the type's remarks), so the failure is reported
        // with its own code rather than left to reach the caller as a generic 500 that reads as
        // "creation failed".
        try
        {
            await _bus.InvokeAsync(
                new ResellerCreated(reseller.Id, reseller.Name, reseller.ContactEmail, reseller.Language),
                cancellationToken);
        }
        catch (Exception exception)
        {
            // The subscriber's own text is never customer-facing (rules/security.md item 8). The
            // success entry above stands: the reseller WAS created, so it is not re-journalled as a
            // failed creation.
            LogLoginIssuanceFailed(_logger, reseller.Name, exception);

            return Result<ResellerDetailDto>.Fail(
                Error.Of(nameof(ErrorMessages.ResellerLoginIssuanceFailed), ErrorType.Failure));
        }

        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, ResellerUsage.None, _displayNames));
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <remarks>
    /// The subject is the name the caller asked for, the only thing there is to name. A refusal is worth
    /// journalling because "that name is taken" is how one caller learns what another tenant holds, so
    /// a run of them from one address is exactly the pattern the journal exists to make visible.
    /// </remarks>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <c>ResellerNameTaken</c>.</returns>
    private async Task<Result<ResellerDetailDto>> FailAsync(
        CreateResellerCommand command,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.ResellerCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNameTaken), ErrorType.Conflict));
    }
}
