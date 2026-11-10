using System.Data.Common;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Interfaces;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Commands.RemoveSftpUserKey;

/// <summary>
/// Handles <see cref="RemoveSftpUserKeyCommand"/>: deletes the key in the panel, sends the agent the
/// login's COMPLETE remaining key set, ends the login's open sessions, and commits only when both
/// effects took.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: lock, delete (flushed, not committed), agent set, end sessions, commit.</b> The set is
/// read after the login's row lock (<see cref="ISftpUserKeyLock"/>), so a concurrent add cannot be
/// overwritten by this request's smaller set. An empty remaining set is valid and clears the key file.
/// </para>
/// <para>
/// <b>The agent refuses the set:</b> the transaction rolls back; the key stays listed and, because a
/// refused write restores the previous file, stays live. The revoker is told it was NOT removed.
/// </para>
/// <para>
/// <b>Ending the sessions fails after the set was replaced</b> (anything but <c>AgentNotFound</c>):
/// the key is already refused for new sign-ins, but sessions admitted with it stay open. That must
/// reach the caller and must not pass silently, so the ROW is rolled back — the key stays listed, a
/// second removal is possible, re-sends the same set (idempotent) and asks again to end the sessions.
/// Had the row been committed gone, the retry would be 404 and nothing in the panel could end them.
/// The caller gets the dedicated <c>SftpSessionsNotEnded</c>. The divergence (a listed key the agent
/// no longer honours) is in the safe direction, is visible, and the retry repairs it.
/// <c>AgentNotFound</c> means the host holds no such login, so there is nothing to end and the removal
/// proceeds (the <c>DeleteSftpUser</c> precedent).
/// </para>
/// <para>
/// <b>The commit fails after both effects took:</b> the host lacks the key and the panel still lists
/// it. Safe direction; the error goes out and a retry converges. No compensation is needed, and
/// issuing one would restore a revoked key.
/// </para>
/// <para>
/// Ending the sessions cuts EVERY session of the login, including ones that authenticated by password
/// or with another key; the confirm dialog and the operator documentation say so. The audit entries
/// carry the login, the label and the fingerprint, never the key.
/// </para>
/// </remarks>
public sealed class RemoveSftpUserKeyCommandHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the login's key file and sessions on the host.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>Serialises key changes of one login.</summary>
    private readonly ISftpUserKeyLock _keyLock;

    /// <summary>This module's audit journal.</summary>
    private readonly SftpAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that writes the key set and ends the sessions.</param>
    /// <param name="keyLock">Serialises key changes of one login.</param>
    /// <param name="journal">This module's audit journal.</param>
    public RemoveSftpUserKeyCommandHandler(
        SftpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSftpClient agent,
        ISftpUserKeyLock keyLock,
        SftpAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _keyLock = keyLock;
        _journal = journal;
    }

    /// <summary>Removes the key and revokes the access it gave.</summary>
    /// <param name="command">Which login and which key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success, or <c>SftpUserNotFound</c>, <c>AccountNotFound</c>, <c>SftpKeyNotFound</c>,
    /// <c>SftpSessionsNotEnded</c>, <c>SftpKeyChangeFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(RemoveSftpUserKeyCommand command, CancellationToken cancellationToken)
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

        // Disposed without a commit on every early return, which is the rollback.
        await using var transaction = await _keyLock.BeginAsync(login.Id, cancellationToken);

        // Read again AFTER the lock, with the keys: the set sent is the one no other request can be
        // changing now.
        var sftpUser = await _dbContext.SftpUsers
            .Include(row => row.Keys)
            .SingleOrDefaultAsync(row => row.Id == login.Id, cancellationToken);
        if (sftpUser is null)
        {
            return await FailAsync(
                command,
                login.Name,
                string.Empty,
                string.Empty,
                Error.Of(nameof(ErrorMessages.SftpUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        // Found by BOTH ids: a key of another login, the caller's own or somebody else's, is simply
        // not among this login's keys.
        var removed = sftpUser.RemoveKey(command.KeyId);
        if (!removed.IsSuccess)
        {
            return await FailAsync(
                command, sftpUser.Name, string.Empty, string.Empty, removed.Error!, cancellationToken);
        }

        var key = removed.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var written = await _agent.SetAuthorizedKeysAsync(
            account.Username, sftpUser.Name, SftpAuthorizedKeysMapper.From(sftpUser.Keys), cancellationToken);
        if (!written.IsSuccess)
        {
            return await FailAsync(
                command, sftpUser.Name, key.Label, key.Fingerprint, written.Error!, cancellationToken);
        }

        var ended = await _agent.EndSessionsAsync(account.Username, sftpUser.Name, cancellationToken);
        if (!ended.IsSuccess && ended.Error!.Code != AgentErrorCodes.NotFound)
        {
            return await FailAsync(
                command,
                sftpUser.Name,
                key.Label,
                key.Fingerprint,
                Error.Of(nameof(ErrorMessages.SftpSessionsNotEnded), ErrorType.Failure),
                cancellationToken);
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is DbException or InvalidOperationException or OperationCanceledException)
        {
            // The host already lacks the key and its sessions are ended; the panel still lists the
            // row. Safe direction, so nothing is restored on the host: a retry converges.
            return await FailAsync(
                command,
                sftpUser.Name,
                key.Label,
                key.Fingerprint,
                Error.Of(nameof(ErrorMessages.SftpKeyChangeFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordKeySuccessAsync(
            SftpAuditActions.SftpKeyRemoved,
            sftpUser.Name,
            key.Label,
            key.Fingerprint,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused removal and returns it as the typed failure.</summary>
    /// <param name="command">The removal that was refused.</param>
    /// <param name="loginName">The login's name, or the supplied identifier when no row was found.</param>
    /// <param name="label">The stored label of the key, or empty when no key was found.</param>
    /// <param name="fingerprint">The key's fingerprint, or empty when no key was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        RemoveSftpUserKeyCommand command,
        string loginName,
        string label,
        string fingerprint,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordKeyFailureAsync(
            SftpAuditActions.SftpKeyRemoved,
            loginName,
            label,
            fingerprint,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Fail(error);
    }
}
