using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Handles <see cref="CreateAccountCommand"/>: provisions the account's Linux system user through
/// the agent, then records the account row (spec §8 — an Account IS a system user, and the
/// isolation between customers is the operating system's, not the panel's).
/// </summary>
/// <remarks>
/// The agent runs first and the row is written only if it succeeded, the same order the suspend,
/// reactivate and delete handlers use, and for the same reason: the two stores can still disagree
/// if the database write fails afterwards, and this order decides WHICH way. A system user with no
/// row is invisible but harmless, and creating the account again converges because the agent's
/// operations are idempotent. The reverse — a row the panel shows as a live account with no user,
/// no home directory and no quota behind it — is a customer who has been sold something that does
/// not exist, and every later operation on it fails.
///
/// The quota travels with the creation call rather than as a second round trip: the plan's
/// <see cref="Plan.DiskQuotaMb"/> is part of what the account IS, and an account that briefly
/// exists without one is an account that briefly has the whole disk.
/// </remarks>
public sealed class CreateAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything about the account that lives on the host.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler with the module's own database context, the agent and the clock.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that provisions the system user.</param>
    /// <param name="clock">The injected time source used to stamp the new account's creation time.</param>
    /// <param name="journal">This module's audit journal.</param>
    public CreateAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        IClock clock,
        AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>
    /// Creates the account row, guarding against a duplicate name or domain.
    /// </summary>
    /// <param name="command">The validated account parameters; see <see cref="CreateAccountCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The created account, <c>AccountNameTaken</c>/<c>AccountDomainTaken</c>/<c>PlanNotFound</c>,
    /// or the agent's own typed failure when the system user could not be provisioned.
    /// </returns>
    public async Task<Result<AccountDto>> HandleAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var nameTaken = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Name == command.Name, cancellationToken);
        if (nameTaken)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNameTaken), ErrorType.Conflict), cancellationToken);
        }

        var domainTaken = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PrimaryDomain == command.PrimaryDomain, cancellationToken);
        if (domainTaken)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountDomainTaken), ErrorType.Conflict), cancellationToken);
        }

        var plan = await _dbContext.Plans
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == command.PlanId, cancellationToken);
        if (plan is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        var provisioned = await _agent.CreateAsync(command.Name, QuotaBytes(plan), cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!, cancellationToken);
        }

        var account = new Account(Guid.NewGuid(), command.Name, command.PrimaryDomain, command.PlanId, _clock.UtcNow);

        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountCreated, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Ok(
            new AccountDto(account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <remarks>
    /// The subject is the name the caller asked for, which is the only thing that exists to name:
    /// no account was created. A refusal is worth journalling here because "that name is taken" and
    /// "that domain is taken" are how one caller learns what another customer holds, so a run of
    /// them from one address is exactly the pattern the journal exists to make visible.
    /// </remarks>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        CreateAccountCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }

    /// <summary>Converts a plan's disk allowance to the bytes the agent's quota call expects.</summary>
    /// <param name="plan">The plan the account is created against.</param>
    /// <returns>The quota in bytes.</returns>
    private static ulong QuotaBytes(Plan plan)
    {
        return (ulong)plan.DiskQuotaMb * 1024UL * 1024UL;
    }
}
