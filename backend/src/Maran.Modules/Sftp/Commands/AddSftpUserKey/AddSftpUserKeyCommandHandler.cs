using System.Data.Common;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Interfaces;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Sftp.Commands.AddSftpUserKey;

/// <summary>
/// Handles <see cref="AddSftpUserKeyCommand"/>: registers the key in the panel, sends the agent the
/// login's COMPLETE key set, and commits only when the agent has accepted it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: lock, insert (flushed, not committed), agent, commit.</b> The panel is the source of
/// truth for the key set and the agent converges to whatever full set it is sent, so the set is read
/// AFTER the login's row lock (<see cref="ISftpUserKeyLock"/>) is held: two simultaneous changes on
/// one login would otherwise each send a set lacking the other's key. Adding a key never ends
/// sessions.
/// </para>
/// <para>
/// <b>The agent refuses:</b> the transaction rolls back. The agent's write is atomic and a refused
/// write restores the previous file, so both sides hold the old set and the typed agent error goes out.
/// </para>
/// <para>
/// <b>The commit fails after the agent accepted:</b> the agent would honour a key the panel does not
/// list, a live credential no screen shows. That is the dangerous direction, so the handler makes ONE
/// best-effort compensating call with the PREVIOUS set, logs the failure (never the key), and answers
/// <c>SftpKeyChangeFailed</c>. If the compensation also fails the hidden key stays until the next
/// successful change to this login's keys resends the full panel set, or until the login is deleted;
/// there is no periodic reconcile (threat note, section 5). A commit that actually reached the
/// database but whose acknowledgement was lost lands in the safe direction instead: the row is listed
/// and the compensation removed the key from the agent, so the customer sees a key that does not work
/// and may remove it.
/// </para>
/// <para>
/// A locked or suspended login is not an error: the panel stores the key either way and still calls
/// the agent, which decides where the key file is placed.
/// </para>
/// <para>
/// The key body is never logged, never audited and never put in an error: the journal records the
/// login, the label and the fingerprint only.
/// </para>
/// </remarks>
public sealed class AddSftpUserKeyCommandHandler
{
    /// <summary>Pre-compiled log delegate for a commit that failed after the agent accepted the set.</summary>
    /// <remarks>
    /// Names the login and the exception TYPE only: the failing statement carries the key body as a
    /// parameter, and a log line is not a place for it.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCommitFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(AddSftpUserKeyCommandHandler)),
            "Key set of SFTP login {SftpUserName} was accepted by the agent but the panel could not commit it "
            + "({ExceptionType}); the previous set is being restored on the host.");

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, nameof(AddSftpUserKeyCommandHandler)),
            "Key set of SFTP login {SftpUserName} could not be restored on the host ({AgentErrorCode}); the host "
            + "may honour a key the panel does not list until the login's next key change.");

    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the login's key file on the host.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>Serialises key changes of one login.</summary>
    private readonly ISftpUserKeyLock _keyLock;

    /// <summary>This module's audit journal.</summary>
    private readonly SftpAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a commit failure and a failed compensation are reported.</summary>
    private readonly ILogger<AddSftpUserKeyCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that writes the login's key set.</param>
    /// <param name="keyLock">Serialises key changes of one login.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="logger">Where a commit failure and a failed compensation are reported.</param>
    public AddSftpUserKeyCommandHandler(
        SftpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSftpClient agent,
        ISftpUserKeyLock keyLock,
        SftpAuditJournal journal,
        IClock clock,
        ILogger<AddSftpUserKeyCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _keyLock = keyLock;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Registers the key and makes it live on the host.</summary>
    /// <param name="command">The validated key; see <see cref="AddSftpUserKeyCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new key, or <c>SftpUserNotFound</c>, <c>AccountNotFound</c>, <c>SftpKeyInvalid</c>,
    /// <c>SftpKeyLabelInvalid</c>, <c>SftpKeyDuplicate</c>, <c>SftpKeyLimitReached</c>,
    /// <c>SftpKeyChangeFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<SftpUserKeyDto>> HandleAsync(
        AddSftpUserKeyCommand command,
        CancellationToken cancellationToken)
    {
        // Through the tenant-filtered context: another account's login finds nothing and answers 404,
        // never 403. The subject is the identifier the caller supplied, because no name is known.
        var login = await _dbContext.SftpUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == command.SftpUserId, cancellationToken);
        if (login is null)
        {
            return await FailAsync(
                command,
                command.SftpUserId.ToString(),
                string.Empty,
                string.Empty,
                Error.Of(nameof(ErrorMessages.SftpUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(login.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                login.Name,
                string.Empty,
                string.Empty,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var parsed = SftpPublicKey.Parse(command.PublicKey);
        if (!parsed.IsSuccess)
        {
            return await FailAsync(command, login.Name, string.Empty, string.Empty, parsed.Error!, cancellationToken);
        }

        var publicKey = parsed.Value;

        // Disposed without a commit on every early return, which is the rollback.
        await using var transaction = await _keyLock.BeginAsync(login.Id, cancellationToken);

        // Read again AFTER the lock, with the keys: the set decided against and sent is the one no
        // other request can be changing now.
        var sftpUser = await _dbContext.SftpUsers
            .Include(row => row.Keys)
            .SingleOrDefaultAsync(row => row.Id == login.Id, cancellationToken);
        if (sftpUser is null)
        {
            return await FailAsync(
                command,
                login.Name,
                string.Empty,
                publicKey.Fingerprint,
                Error.Of(nameof(ErrorMessages.SftpUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var previousSet = SftpAuthorizedKeysMapper.From(sftpUser.Keys);

        var added = sftpUser.AddKey(Guid.NewGuid(), command.Label, publicKey, _clock.UtcNow);
        if (!added.IsSuccess)
        {
            return await FailAsync(
                command, sftpUser.Name, string.Empty, publicKey.Fingerprint, added.Error!, cancellationToken);
        }

        var key = added.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The unique index on (login, fingerprint) is the arbiter of two simultaneous adds of one
            // key; the loser is a conflict, never a 500.
            return await FailAsync(
                command,
                sftpUser.Name,
                key.Label,
                key.Fingerprint,
                Error.Of(nameof(ErrorMessages.SftpKeyDuplicate), ErrorType.Conflict),
                cancellationToken);
        }

        var written = await _agent.SetAuthorizedKeysAsync(
            account.Username, sftpUser.Name, SftpAuthorizedKeysMapper.From(sftpUser.Keys), cancellationToken);
        if (!written.IsSuccess)
        {
            return await FailAsync(
                command, sftpUser.Name, key.Label, key.Fingerprint, written.Error!, cancellationToken);
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is DbException or InvalidOperationException or OperationCanceledException)
        {
            await CompensateAsync(account.Username, sftpUser.Name, previousSet, exception);

            return await FailAsync(
                command,
                sftpUser.Name,
                key.Label,
                key.Fingerprint,
                Error.Of(nameof(ErrorMessages.SftpKeyChangeFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordKeySuccessAsync(
            SftpAuditActions.SftpKeyAdded,
            sftpUser.Name,
            key.Label,
            key.Fingerprint,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<SftpUserKeyDto>.Ok(SftpUserKeyMapper.From(key));
    }

    /// <summary>Puts the host back to the set it held before this request, after a commit that failed.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="sftpUsername">The login's name suffix, which addresses the agent call.</param>
    /// <param name="previousSet">The key set the login held before this request.</param>
    /// <param name="commitFailure">The failure of the commit, of which only the type is logged.</param>
    /// <remarks>
    /// Best effort, once, and with <see cref="CancellationToken.None"/>: it runs because the request
    /// failed, possibly because it was cancelled, and a cancelled token would skip the one call that
    /// removes a credential nobody can see. A failure is logged with the agent's error code and not
    /// surfaced; the customer is already being told the change failed.
    /// </remarks>
    private async Task CompensateAsync(
        string accountUsername,
        string sftpUsername,
        IReadOnlyList<string> previousSet,
        Exception commitFailure)
    {
        LogCommitFailed(_logger, sftpUsername, commitFailure.GetType().Name, null);

        var restored = await _agent.SetAuthorizedKeysAsync(
            accountUsername, sftpUsername, previousSet, CancellationToken.None);
        if (!restored.IsSuccess)
        {
            LogCompensationFailed(_logger, sftpUsername, restored.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused add and returns it as the typed failure.</summary>
    /// <param name="command">The add that was refused.</param>
    /// <param name="loginName">The login's name, or the supplied identifier when no row was found.</param>
    /// <param name="label">The stored label, or empty when none was accepted yet.</param>
    /// <param name="fingerprint">The key's fingerprint, or empty when the key was not parsed.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SftpUserKeyDto>> FailAsync(
        AddSftpUserKeyCommand command,
        string loginName,
        string label,
        string fingerprint,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordKeyFailureAsync(
            SftpAuditActions.SftpKeyAdded,
            loginName,
            label,
            fingerprint,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<SftpUserKeyDto>.Fail(error);
    }
}
