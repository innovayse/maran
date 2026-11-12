using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Commands.DeleteMailAlias;

/// <summary>
/// Handles <see cref="DeleteMailAliasCommand"/>: sends the agent the domain's COMPLETE alias set without
/// this address, and only then deletes the rows — putting the previous set back if they cannot be deleted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which address goes is decided by the panel's row and by nothing else, and the refusal for another
/// account's row is the refusal for a row that does not exist.</b> The row is loaded through the
/// tenant-filtered context, so a guessed identifier finds nothing (rules/security.md item 6).
/// </para>
/// <para>
/// <b>No switch is read.</b> Deleting an address withdraws a route and opens none, so a host whose mail was
/// turned off still lets the customer clear what they set up; the agent decides whether the host can take
/// the call.
/// </para>
/// </remarks>
public sealed class DeleteMailAliasCommandHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Reads the domain's alias set and sends it to the agent.</summary>
    private readonly MailAliasSetPublisher _publisher;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Republishes the account's local submission binding, whose fan-out figures an alias change moves.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="publisher">Reads the domain's alias set and sends it to the agent.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="submission">Republishes the account's local submission binding after the change.</param>
    public DeleteMailAliasCommandHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        MailAliasSetPublisher publisher,
        MailAuditJournal journal,
        MailLocalSubmissionPublisher submission)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _publisher = publisher;
        _journal = journal;
        _submission = submission;
    }

    /// <summary>Removes the address from the host and then from the database.</summary>
    /// <param name="command">The parameters.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success, or <c>MailAliasNotFound</c>, <c>AccountNotFound</c>, <c>MailAliasProvisioningFailed</c>, or the
    /// agent's own typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(DeleteMailAliasCommand command, CancellationToken cancellationToken)
    {
        var alias = await _dbContext.MailAliases
            .Include(row => row.MailDomain)
            .Include(row => row.Targets)
            .SingleOrDefaultAsync(row => row.Id == command.MailAliasId, cancellationToken);
        if (alias is null)
        {
            // The subject is the identifier the caller supplied, because no address is known — a probe
            // for an address the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                string.Empty,
                command.MailAliasId.ToString(),
                Error.Of(nameof(ErrorMessages.MailAliasNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        // Captured before the row goes: the journal records the address, the only thing about a removed
        // one anybody can later search for.
        var localPart = alias.LocalPart;
        var domainName = alias.MailDomain.Name;
        var address = MailboxAddressPolicy.Compose(localPart, domainName);

        var account = await _accounts.FindAsync(alias.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                localPart,
                address,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var set = await _publisher.LoadTrackedSetAsync(alias.MailDomainId, cancellationToken);
        var previous = MailAliasSetMapper.From(set);

        var published = await _publisher.PublishAsync(
            account.Username, domainName, MailAliasSetMapper.Without(set, localPart), cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, localPart, address, published.Error!, cancellationToken);
        }

        // The target rows are tracked through the alias and go with it.
        _dbContext.MailAliases.Remove(alias);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await _publisher.RestoreAsync(account.Username, domainName, previous, cancellationToken);

            return await FailAsync(
                command,
                localPart,
                address,
                Error.Of(nameof(ErrorMessages.MailAliasProvisioningFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            ActionOf(localPart), address, command.IpAddress, command.UserAgent, cancellationToken);

        // The deleted alias no longer fans out: the sender's cost for it falls back to one delivery now,
        // not at the next reconcile.
        await _submission.TryPublishAfterChangeAsync(account, cancellationToken);

        return Result.Ok();
    }

    /// <summary>The audit action for removing an address: the catch-all has its own.</summary>
    /// <param name="localPart">The address's local part, or the catch-all marker.</param>
    /// <returns>The action name.</returns>
    private static string ActionOf(string localPart)
    {
        return localPart == MailboxAddressPolicy.CatchAllLocalPart
            ? HostedMailAuditActions.MailCatchAllCleared
            : HostedMailAuditActions.MailAliasDeleted;
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="localPart">The address's local part, which picks the action; empty when the address is not known.</param>
    /// <param name="subject">What it was attempted on: the address, or the identifier the caller supplied.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteMailAliasCommand command,
        string localPart,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            ActionOf(localPart), subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
