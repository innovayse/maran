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
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Commands.RotateDkimKey;

/// <summary>
/// Handles <see cref="RotateDkimKeyCommand"/>: generates a new key under a fresh selector, gives it to
/// the agent to sign with, and only then records it as the domain's active key.
/// </summary>
/// <remarks>
/// <para>
/// <b>The selector is unique by construction.</b> It is the policy's day-stamped name with a sequence
/// equal to how many keys the domain already created that UTC day, so rotating twice in a day yields
/// two different names and an old public record is never overwritten by a new key.
/// </para>
/// <para>
/// <b>The previous key is kept.</b> <see cref="MailDomain.RotateDkim"/> moves the active key to
/// retiring and any earlier retiring key to retired, so the DNS policy keeps publishing the key that
/// just stopped signing for as long as in-flight mail may still need it.
/// </para>
/// <para>
/// <b>If the row cannot be written after the agent signs with the new key</b>, the host and the panel
/// disagree about which key is active, and signed mail would fail verification against the published
/// record. The previous key is pushed back to the agent (best effort, logged) and the request fails.
/// </para>
/// </remarks>
public sealed class RotateDkimKeyCommandHandler
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(RotateDkimKeyCommandHandler)),
            "The DKIM key of mail domain {MailDomain} could not be recorded after rotation and the previous "
            + "key could not be restored on the host either ({AgentErrorCode}); the host signs with a key the "
            + "panel does not publish.");

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto the owning account's system user name and state.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Makes the new DKIM key pair.</summary>
    private readonly IDkimKeyGenerator _keys;

    /// <summary>Encrypts the private key for storage and reads the previous one back for a restore.</summary>
    private readonly IEncryptionService _encryption;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The module's localized display names, for the returned row.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<RotateDkimKeyCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">The owning account's system user name and state.</param>
    /// <param name="agent">The agent client that installs the key.</param>
    /// <param name="keys">Generates the DKIM key pair.</param>
    /// <param name="encryption">Encrypts the private key at rest.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source.</param>
    /// <param name="names">The module's localized display names.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public RotateDkimKeyCommandHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        IDkimKeyGenerator keys,
        IEncryptionService encryption,
        MailAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names,
        ILogger<RotateDkimKeyCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _keys = keys;
        _encryption = encryption;
        _journal = journal;
        _clock = clock;
        _names = names;
        _logger = logger;
    }

    /// <summary>Rotates the domain's key.</summary>
    /// <param name="command">The domain to rotate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The domain with its new active selector, or <c>MailDomainNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailDomainProvisioningFailed</c> or the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailDomainDto>> HandleAsync(
        RotateDkimKeyCommand command,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.MailDomains
            .Include(row => row.DkimKeys)
            .SingleOrDefaultAsync(row => row.Id == command.MailDomainId, cancellationToken);
        if (domain is null)
        {
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailDomainNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(domain.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                domain.Name,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var now = _clock.UtcNow;
        var sequence = domain.DkimKeys.Count(key =>
        {
            return DkimSelectorPolicy.SameDay(key.CreatedAt, now);
        });
        var selector = DkimSelectorPolicy.For(now, sequence);
        var previous = domain.ActiveKey;
        var generated = _keys.Generate();

        var configured = await _agent.ConfigureDomainAsync(
            new AgentMailDomainArguments(account.Username, domain.Name, selector, generated.PrivateKeyPem),
            cancellationToken);
        if (!configured.IsSuccess)
        {
            return await FailAsync(command, domain.Name, configured.Error!, cancellationToken);
        }

        domain.RotateDkim(
            new DkimKey(
                Guid.NewGuid(),
                domain.Id,
                selector,
                _encryption.Encrypt(generated.PrivateKeyPem.Reveal()),
                generated.PublicKeyBase64,
                now),
            now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await RestoreAsync(account.Username, domain.Name, previous, cancellationToken);

            return await FailAsync(
                command,
                domain.Name,
                Error.Of(nameof(ErrorMessages.MailDomainProvisioningFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailDkimKeyRotated,
            domain.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([domain.AccountId], cancellationToken);
        var status = MailAccountStates.DomainStatusOf(account.State == AccountState.Suspended);
        var mailboxCount = await _dbContext.Mailboxes
            .CountAsync(row => row.MailDomainId == domain.Id, cancellationToken);

        return Result<MailDomainDto>.Ok(MailDomainMapper.From(domain, ownerNames, status, mailboxCount, _names));
    }

    /// <summary>Puts the previous active key back on the host after a rotation that could not be recorded.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domainName">The domain.</param>
    /// <param name="previous">The key that was active before the rotation, if there was one.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    private async Task RestoreAsync(
        string accountUsername,
        string domainName,
        DkimKey? previous,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            return;
        }

        var restored = await _agent.ConfigureDomainAsync(
            new AgentMailDomainArguments(
                accountUsername,
                domainName,
                previous.Selector,
                new SensitiveString(_encryption.Decrypt(previous.PrivateKeyEncrypted))),
            cancellationToken);
        if (!restored.IsSuccess)
        {
            LogRestoreFailed(_logger, domainName, restored.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused rotation and returns it as the typed failure.</summary>
    /// <param name="command">The rotation that was refused.</param>
    /// <param name="subject">The domain name, or the identifier probed for.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailDomainDto>> FailAsync(
        RotateDkimKeyCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailDkimKeyRotated,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailDomainDto>.Fail(error);
    }
}
