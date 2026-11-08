using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Mail.Commands.CreateMailDomain;

/// <summary>
/// Handles <see cref="CreateMailDomainCommand"/>: refuses what the server, the account or the site
/// ownership do not allow, generates the domain's DKIM key, provisions the domain through the agent
/// and only then records the row that owns it, compensating on the host if the row cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: checks first, agent second, row third.</b> Every refusal that needs no host (mail off,
/// account unknown, the name is not a site of this account, the name is held by someone) is answered
/// before the agent is called, so a domain the panel would refuse never reaches the host. The agent
/// runs before the row for the reason every provisioning handler in the panel gives: a host domain
/// with no row is invisible and, with the compensation below, short-lived, while a row with no host
/// domain is a customer told mail works when it does not.
/// </para>
/// <para>
/// <b>A mail domain is unique across the whole server, not within one account.</b> The server
/// accepts mail for a name on behalf of exactly one account, so the uniqueness read deliberately
/// bypasses the tenant filter: scoped to the caller it would say "free" for a name another account
/// holds. The unique index is what makes two simultaneous creations unable to both succeed, and the
/// loser of that race arrives here as a typed conflict.
/// </para>
/// <para>
/// <b>The private key is stored only as ciphertext.</b> The PEM is revealed once, to encrypt it for
/// the row; the agent receives it as a <c>SensitiveString</c>, and nothing that is journaled, logged
/// or returned contains it.
/// </para>
/// <para>
/// <b>The unique-violation branch is narrowed to SqlState 23505 and nothing else.</b> A 23505 means a
/// concurrent creation won and ITS row owns the host state, so deleting would tear down a domain
/// another account has just been given; every other database failure leaves nothing owning the host
/// domain at all and falls through to the compensating branch.
/// </para>
/// </remarks>
public sealed class CreateMailDomainCommandHandler
{
    /// <summary>The agent client's stable code for "the host already holds this name".</summary>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Names the domain and the agent's error CODE, never the agent's text and never key material.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateMailDomainCommandHandler)),
            "Created mail domain {MailDomain} could not be recorded and could not be removed either "
            + "({AgentErrorCode}); it is now on the host with no row.");

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The mail server's settings row, which says whether mail is on.</summary>
    private readonly MailServerReader _server;

    /// <summary>The window onto the owning account's system user name and state.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The window onto the sites that prove a domain belongs to an account.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Makes the domain's DKIM key pair.</summary>
    private readonly IDkimKeyGenerator _keys;

    /// <summary>Encrypts the private key for storage.</summary>
    private readonly IEncryptionService _encryption;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The module's localized display names, for the returned row.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateMailDomainCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Reads the mail server's settings.</param>
    /// <param name="accounts">The owning account's system user name and state.</param>
    /// <param name="sites">Proves the domain is a site of the account.</param>
    /// <param name="agent">The agent client that provisions the domain.</param>
    /// <param name="keys">Generates the DKIM key pair.</param>
    /// <param name="encryption">Encrypts the private key at rest.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source.</param>
    /// <param name="names">The module's localized display names.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateMailDomainCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        ISiteDirectory sites,
        IAgentMailClient agent,
        IDkimKeyGenerator keys,
        IEncryptionService encryption,
        MailAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names,
        ILogger<CreateMailDomainCommandHandler> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _sites = sites;
        _agent = agent;
        _keys = keys;
        _encryption = encryption;
        _journal = journal;
        _clock = clock;
        _names = names;
        _logger = logger;
    }

    /// <summary>Creates the mail domain, refusing it before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateMailDomainCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new domain, or <c>MailNotEnabled</c>, <c>AccountNotFound</c>, <c>MailDomainNotOwnedSite</c>,
    /// <c>MailDomainTaken</c>, <c>MailDomainProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailDomainDto>> HandleAsync(
        CreateMailDomainCommand command,
        CancellationToken cancellationToken)
    {
        var name = command.Name.ToLowerInvariant();

        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict), cancellationToken);
        }

        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // The ownership proof, and the whole of it: the name is the primary domain of a site, and
        // that site belongs to the account the domain is being created for. A site of another
        // account is refused with the same sentence as no site at all, so the answer does not
        // confirm that a neighbour hosts the name.
        var site = await _sites.FindByDomainAsync(name, cancellationToken);
        if (site is null || site.AccountId != command.AccountId)
        {
            return await FailAsync(
                command,
                name,
                Error.Of(nameof(ErrorMessages.MailDomainNotOwnedSite), ErrorType.Validation),
                cancellationToken);
        }

#pragma warning disable RS0030 // a mail domain is unique across the whole server, so another account's row must be seen
        var taken = await _dbContext.MailDomains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(domain => domain.Name == name, cancellationToken);
#pragma warning restore RS0030
        if (taken)
        {
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.MailDomainTaken), ErrorType.Conflict), cancellationToken);
        }

        var now = _clock.UtcNow;
        var selector = DkimSelectorPolicy.For(now, 0);
        var generated = _keys.Generate();

        var configured = await _agent.ConfigureDomainAsync(
            new AgentMailDomainArguments(account.Username, name, selector, generated.PrivateKeyPem),
            cancellationToken);
        if (!configured.IsSuccess)
        {
            return await FailAsync(command, name, TranslateProvisioningFailure(configured.Error!), cancellationToken);
        }

        var mailDomain = new MailDomain(Guid.NewGuid(), command.AccountId, name, site.Id, now);
        mailDomain.RotateDkim(
            new DkimKey(
                Guid.NewGuid(),
                mailDomain.Id,
                selector,
                _encryption.Encrypt(generated.PrivateKeyPem.Reveal()),
                generated.PublicKeyBase64,
                now),
            now);

        var recorded = await RecordAsync(mailDomain, account.Username, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<MailDomainDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailDomainCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([mailDomain.AccountId], cancellationToken);
        var status = MailAccountStates.DomainStatusOf(account.State == AccountState.Suspended);

        return Result<MailDomainDto>.Ok(MailDomainMapper.From(mailDomain, ownerNames, status, 0, _names));
    }

    /// <summary>Re-reads the agent's refusal as a sentence about the NAME where that is what it means.</summary>
    /// <param name="error">The typed failure the agent client produced.</param>
    /// <returns>The name-taken failure for a host-side collision; otherwise <paramref name="error"/> unchanged.</returns>
    private static Error TranslateProvisioningFailure(Error error)
    {
        if (!string.Equals(error.Code, AgentAlreadyExistsCode, StringComparison.Ordinal))
        {
            return error;
        }

        return Error.Of(nameof(ErrorMessages.MailDomainTaken), ErrorType.Conflict);
    }

    /// <summary>Writes the row for a domain the agent has already configured, compensating if it cannot.</summary>
    /// <param name="mailDomain">The row to write, with its first DKIM key.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating removal.</param>
    /// <param name="command">The creation being recorded, for the journal's subject.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result> RecordAsync(
        MailDomain mailDomain,
        string accountUsername,
        CreateMailDomainCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.MailDomains.Add(mailDomain);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
        catch (DbUpdateException exception)
        {
            _dbContext.ChangeTracker.Clear();

            if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent creation won, and ITS row owns the host state now. Nothing is
                // orphaned, and removing would tear down the winner's domain.
                return Result.Fail(await FailedErrorAsync(
                    command,
                    mailDomain.Name,
                    Error.Of(nameof(ErrorMessages.MailDomainTaken), ErrorType.Conflict),
                    cancellationToken));
            }

            var removed = await _agent.RemoveDomainAsync(accountUsername, mailDomain.Name, cancellationToken);
            if (!removed.IsSuccess)
            {
                LogCompensationFailed(_logger, mailDomain.Name, removed.Error!.Code, null);
            }

            return Result.Fail(await FailedErrorAsync(
                command,
                mailDomain.Name,
                Error.Of(nameof(ErrorMessages.MailDomainProvisioningFailed), ErrorType.Failure),
                cancellationToken));
        }
    }

    /// <summary>Journals a refused creation and hands back the error to answer with.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">The domain name, which is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="error"/>, unchanged.</returns>
    private async Task<Error> FailedErrorAsync(
        CreateMailDomainCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailDomainCreated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">The domain name, which is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailDomainDto>> FailAsync(
        CreateMailDomainCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<MailDomainDto>.Fail(await FailedErrorAsync(command, subject, error, cancellationToken));
    }
}
