using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Models;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace Maran.Modules.Mail.Commands.CreateMailAlias;

/// <summary>
/// Handles <see cref="CreateMailAliasCommand"/>: refuses what the domain, the account, the host's
/// switches or the targets do not allow, sends the agent the domain's COMPLETE alias set including the new
/// address, and only then records the row — putting the previous set back if it cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: checks first, agent second, row third</b>, so an address the plan or the host refuses never
/// reaches the host. The agent's alias call replaces every alias of the domain, so what is sent is rebuilt
/// from the database plus the new address (<see cref="MailAliasSetPublisher"/>), never just the new address.
/// </para>
/// <para>
/// <b>One address space per domain.</b> A mailbox and an alias may not share a local part, and no index
/// can say so across two tables, so both are asked here; the catch-all is the same question with its own
/// answer, because "the domain already has one" is a different sentence from "that address is taken". The
/// unique index on the domain and the local part still decides a race.
/// </para>
/// <para>
/// <b>Where the targets go decides what the address is.</b> A target on a domain this ACCOUNT does not own
/// leaves it, needs the host's external-forwarding switch, and makes the address a forwarder; the stated
/// kind must agree (<see cref="MailAliasTargetPolicy.Judge"/>). The account's own domains are read through
/// the tenant-filtered context, narrowed to the owner explicitly for an administrator, who is not narrowed.
/// </para>
/// <para>
/// <b>A row that cannot be written leaves the host ahead of the panel</b>, and the compensation is to send
/// the set the host held before. When the failure is a duplicate (SqlState 23505) a concurrent creation won
/// the address and its row is committed, so the set to restore is read again from the database; any other
/// failure restores the set read before the change. Either way the customer is told which of the two happened.
/// </para>
/// </remarks>
public sealed class CreateMailAliasCommandHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Whether the administrator wants mail on, and the host's switches.</summary>
    private readonly MailServerReader _server;

    /// <summary>The window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Reads the domain's alias set and sends it to the agent.</summary>
    private readonly MailAliasSetPublisher _publisher;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The module's localized names, for the kind shown in the answer.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Reads the host's mail settings.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="publisher">Reads the domain's alias set and sends it to the agent.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">The module's localized names.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    public CreateMailAliasCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        MailAliasSetPublisher publisher,
        MailAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames,
        IClock clock)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _publisher = publisher;
        _journal = journal;
        _displayNames = displayNames;
        _clock = clock;
    }

    /// <summary>Creates the address, refusing it before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateMailAliasCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new address — or <c>MailNotEnabled</c>, <c>MailDomainNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailAliasTargetInvalid</c>, <c>MailAliasTargetLimit</c>, <c>MailAliasTaken</c>,
    /// <c>MailCatchAllConflict</c>, <c>MailAliasExternalTargetNotAllowed</c>, <c>MailAliasKindMismatch</c>,
    /// <c>MailAliasProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailAliasDto>> HandleAsync(
        CreateMailAliasCommand command,
        CancellationToken cancellationToken)
    {
        var isCatchAll = command.Kind == MailAliasKind.CatchAll;
        var localPart = isCatchAll
            ? MailboxAddressPolicy.CatchAllLocalPart
            : MailboxAddressPolicy.Normalize(command.LocalPart);

        var settings = await _server.FindEnabledAsync(cancellationToken);
        if (settings is null)
        {
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict),
                cancellationToken);
        }

        // Tenant-scoped: another account's domain is not in the result set, so a guessed id reads as
        // "not found" rather than "forbidden".
        var domain = await _dbContext.MailDomains
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == command.MailDomainId, cancellationToken);
        if (domain is null)
        {
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailDomainNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var address = MailboxAddressPolicy.Compose(localPart, domain.Name);

        var account = await _accounts.FindAsync(domain.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var targets = MailAliasTargetPolicy.Normalise(command.Targets);
        if (!targets.IsSuccess)
        {
            return await FailAsync(command, address, targets.Error!, cancellationToken);
        }

        var existing = await _publisher.LoadSetAsync(domain.Id, cancellationToken);
        var taken = await AddressTakenAsync(domain.Id, localPart, isCatchAll, existing, cancellationToken);
        if (taken is not null)
        {
            return await FailAsync(command, address, taken, cancellationToken);
        }

        var domainNames = await _publisher.AccountDomainNamesAsync(domain.AccountId, cancellationToken);
        var judgement = MailAliasTargetPolicy.Judge(
            command.Kind, targets.Value, domainNames, settings.AllowExternalForwarding);
        if (judgement is not null)
        {
            return await FailAsync(command, address, judgement, cancellationToken);
        }

        var kind = isCatchAll ? MailAliasKind.CatchAll : MailAliasTargetPolicy.KindFor(targets.Value, domainNames);
        var previous = MailAliasSetMapper.From(existing);

        var published = await _publisher.PublishAsync(
            account.Username,
            domain.Name,
            MailAliasSetMapper.With(existing, localPart, targets.Value),
            cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, address, published.Error!, cancellationToken);
        }

        var alias = new MailAlias(
            Guid.NewGuid(), domain.AccountId, domain.Id, localPart, kind, targets.Value, _clock.UtcNow);

        var recorded = await RecordAsync(
            alias, account.Username, domain, previous, command, address, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<MailAliasDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            isCatchAll ? HostedMailAuditActions.MailCatchAllSet : HostedMailAuditActions.MailAliasCreated,
            address,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([alias.AccountId], cancellationToken);

        return Result<MailAliasDto>.Ok(MailAliasMapper.From(alias, domain.Name, ownerNames, _displayNames));
    }

    /// <summary>Says whether the address is already in use in the domain, as an alias, a mailbox or a second catch-all.</summary>
    /// <param name="mailDomainId">The domain.</param>
    /// <param name="localPart">The local part, or the catch-all marker.</param>
    /// <param name="isCatchAll">Whether the new address is the catch-all.</param>
    /// <param name="existing">The aliases the domain already has.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conflict, or <c>null</c> when the address is free.</returns>
    private async Task<Error?> AddressTakenAsync(
        Guid mailDomainId,
        string localPart,
        bool isCatchAll,
        IReadOnlyList<MailAlias> existing,
        CancellationToken cancellationToken)
    {
        var aliasHoldsIt = existing.Any(alias =>
        {
            return alias.LocalPart == localPart;
        });

        if (isCatchAll)
        {
            return aliasHoldsIt ? Error.Of(nameof(ErrorMessages.MailCatchAllConflict), ErrorType.Conflict) : null;
        }

        var mailboxHoldsIt = await _dbContext.Mailboxes
            .AsNoTracking()
            .AnyAsync(
                mailbox => mailbox.MailDomainId == mailDomainId && mailbox.LocalPart == localPart,
                cancellationToken);

        return aliasHoldsIt || mailboxHoldsIt
            ? Error.Of(nameof(ErrorMessages.MailAliasTaken), ErrorType.Conflict)
            : null;
    }

    /// <summary>Writes the row for an address the agent already holds, putting the previous set back if it cannot.</summary>
    /// <param name="alias">The row to write.</param>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The domain the address lives in.</param>
    /// <param name="previous">The set the host held before this change.</param>
    /// <param name="command">The creation being recorded.</param>
    /// <param name="address">The address, the journal's subject.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result> RecordAsync(
        MailAlias alias,
        string accountUsername,
        MailDomain domain,
        MailAliasSet previous,
        CreateMailAliasCommand command,
        string address,
        CancellationToken cancellationToken)
    {
        // Adding the alias adds its target rows with it: they are new children of a new parent.
        _dbContext.MailAliases.Add(alias);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
        catch (DbUpdateException exception)
        {
            _dbContext.ChangeTracker.Clear();

            // The ONE narrowing: SqlState 23505 and nothing else means a duplicate. Every other database
            // failure — a dropped connection, a timeout, a constraint added later — is not "taken", and
            // telling the customer so would discourage the retry that would repair it.
            if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent creation committed the address while this one was on the host. Its row is
                // the truth now, so the host gets THAT set back, not the one read before it existed.
                var committed = await _publisher.LoadSetAsync(domain.Id, cancellationToken);
                await _publisher.RestoreAsync(
                    accountUsername, domain.Name, MailAliasSetMapper.From(committed), cancellationToken);

                var conflict = alias.Kind == MailAliasKind.CatchAll
                    ? Error.Of(nameof(ErrorMessages.MailCatchAllConflict), ErrorType.Conflict)
                    : Error.Of(nameof(ErrorMessages.MailAliasTaken), ErrorType.Conflict);

                return Result.Fail(await FailedErrorAsync(command, address, conflict, cancellationToken));
            }

            await _publisher.RestoreAsync(accountUsername, domain.Name, previous, cancellationToken);

            return Result.Fail(await FailedErrorAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailAliasProvisioningFailed), ErrorType.Failure),
                cancellationToken));
        }
    }

    /// <summary>Journals a refused creation and hands back the error to answer with.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">What it was attempted on: the address, or the domain id when the domain was not found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="error"/>, unchanged.</returns>
    private async Task<Error> FailedErrorAsync(
        CreateMailAliasCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            command.Kind == MailAliasKind.CatchAll
                ? HostedMailAuditActions.MailCatchAllSet
                : HostedMailAuditActions.MailAliasCreated,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailAliasDto>> FailAsync(
        CreateMailAliasCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<MailAliasDto>.Fail(await FailedErrorAsync(command, subject, error, cancellationToken));
    }
}
