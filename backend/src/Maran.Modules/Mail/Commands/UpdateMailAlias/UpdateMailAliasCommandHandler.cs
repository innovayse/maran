using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Commands.UpdateMailAlias;

/// <summary>
/// Handles <see cref="UpdateMailAliasCommand"/>: judges the new targets exactly as a creation would, sends
/// the agent the domain's COMPLETE alias set with this address's targets replaced, and only then rewrites
/// the rows — putting the previous set back if they cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Another account's address is "not found".</b> The row is loaded through the tenant-filtered context,
/// so a guessed identifier finds nothing and the single <c>MailAliasNotFound</c> answer covers both cases;
/// a distinct refusal would confirm the identifier names a real address somebody else holds.
/// </para>
/// <para>
/// <b>The previous set is captured before anything is changed</b>, as values, because the rows are tracked
/// and are rewritten below: restoring from the rows after the rewrite would restore the new targets.
/// </para>
/// <para>
/// <b>The target rows are replaced explicitly.</b> The new rows carry keys chosen by the entity, and EF
/// treats an entity with a preset key that is reached through a tracked parent as already existing — it
/// would update rows that were never inserted. They are therefore added through their own set, and the
/// dropped rows removed through it.
/// </para>
/// </remarks>
public sealed class UpdateMailAliasCommandHandler
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

    /// <summary>Republishes the account's local submission binding, whose fan-out figures an alias change moves.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>The module's localized names, for the kind shown in the answer.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Reads the host's mail settings.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="publisher">Reads the domain's alias set and sends it to the agent.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="submission">Republishes the account's local submission binding after the change.</param>
    /// <param name="displayNames">The module's localized names.</param>
    public UpdateMailAliasCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        MailAliasSetPublisher publisher,
        MailAuditJournal journal,
        MailLocalSubmissionPublisher submission,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _publisher = publisher;
        _journal = journal;
        _submission = submission;
        _displayNames = displayNames;
    }

    /// <summary>Replaces the targets, refusing the change before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="UpdateMailAliasCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The address as it now reads — or <c>MailNotEnabled</c>, <c>MailAliasNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailAliasTargetInvalid</c>, <c>MailAliasTargetLimit</c>, <c>MailAliasExternalTargetNotAllowed</c>,
    /// <c>MailAliasProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailAliasDto>> HandleAsync(
        UpdateMailAliasCommand command,
        CancellationToken cancellationToken)
    {
        var alias = await _dbContext.MailAliases
            .Include(row => row.MailDomain)
            .Include(row => row.Targets)
            .SingleOrDefaultAsync(row => row.Id == command.MailAliasId, cancellationToken);
        if (alias is null)
        {
            // The subject is the identifier the caller supplied, because no address is known — a probe for
            // an address the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                string.Empty,
                command.MailAliasId.ToString(),
                Error.Of(nameof(ErrorMessages.MailAliasNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var address = MailboxAddressPolicy.Compose(alias.LocalPart, alias.MailDomain.Name);

        var settings = await _server.FindEnabledAsync(cancellationToken);
        if (settings is null)
        {
            return await FailAsync(
                command,
                alias.LocalPart,
                address,
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(alias.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                alias.LocalPart,
                address,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var targets = MailAliasTargetPolicy.Normalise(command.Targets);
        if (!targets.IsSuccess)
        {
            return await FailAsync(command, alias.LocalPart, address, targets.Error!, cancellationToken);
        }

        var domainNames = await _publisher.AccountDomainNamesAsync(alias.AccountId, cancellationToken);
        var judgement = MailAliasTargetPolicy.Judge(
            requestedKind: null, targets.Value, domainNames, settings.AllowExternalForwarding);
        if (judgement is not null)
        {
            return await FailAsync(command, alias.LocalPart, address, judgement, cancellationToken);
        }

        var kind = alias.Kind == MailAliasKind.CatchAll
            ? MailAliasKind.CatchAll
            : MailAliasTargetPolicy.KindFor(targets.Value, domainNames);

        var set = await _publisher.LoadTrackedSetAsync(alias.MailDomainId, cancellationToken);
        var previous = MailAliasSetMapper.From(set);

        var published = await _publisher.PublishAsync(
            account.Username,
            alias.MailDomain.Name,
            MailAliasSetMapper.With(set, alias.LocalPart, targets.Value),
            cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, alias.LocalPart, address, published.Error!, cancellationToken);
        }

        var dropped = alias.ReplaceTargets(targets.Value, kind);
        _dbContext.MailAliasTargets.RemoveRange(dropped);
        _dbContext.MailAliasTargets.AddRange(alias.Targets.ToList());

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await _publisher.RestoreAsync(account.Username, alias.MailDomain.Name, previous, cancellationToken);

            return await FailAsync(
                command,
                alias.LocalPart,
                address,
                Error.Of(nameof(ErrorMessages.MailAliasProvisioningFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            ActionOf(alias.LocalPart), address, command.IpAddress, command.UserAgent, cancellationToken);

        // The new target list changes how many deliveries one message to this address makes: the sender's
        // cost is charged by it from now on, not from the next reconcile.
        await _submission.TryPublishAfterChangeAsync(account, cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([alias.AccountId], cancellationToken);

        return Result<MailAliasDto>.Ok(
            MailAliasMapper.From(alias, alias.MailDomain.Name, ownerNames, _displayNames));
    }

    /// <summary>The audit action for a change to an address: the catch-all has its own.</summary>
    /// <param name="localPart">The address's local part, or the catch-all marker.</param>
    /// <returns>The action name.</returns>
    private static string ActionOf(string localPart)
    {
        return localPart == MailboxAddressPolicy.CatchAllLocalPart
            ? HostedMailAuditActions.MailCatchAllSet
            : HostedMailAuditActions.MailAliasUpdated;
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="localPart">The address's local part, which picks the action; empty when the address is not known.</param>
    /// <param name="subject">What it was attempted on: the address, or the identifier the caller supplied.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailAliasDto>> FailAsync(
        UpdateMailAliasCommand command,
        string localPart,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            ActionOf(localPart), subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<MailAliasDto>.Fail(error);
    }
}
