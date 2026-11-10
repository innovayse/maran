using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;

/// <summary>Pins the host key whose fingerprint the administrator confirmed.</summary>
/// <remarks>
/// <para>
/// <b>This handler is where trust is decided, and it decides by the entity's exact match.</b> The fingerprint the
/// request carries is compared character for character with the fingerprints the server offered at its last scan; a
/// near miss, a different case, or a fingerprint the server never offered pins nothing and answers
/// <c>RemoteServerHostKeyFingerprintMismatch</c>. The interface disables its button until the typed text matches a
/// candidate, but that is advice: this check does not depend on it.
/// </para>
/// <para>
/// A refused confirmation is journalled, because a wrong fingerprint typed for a server is exactly what an operator
/// reads the journal to find. The entry names the server, never the fingerprint that was typed.
/// </para>
/// </remarks>
public sealed class ConfirmRemoteServerHostKeyCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The clock the confirmation is dated by.</summary>
    private readonly IClock _clock;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock the confirmation is dated by.</param>
    /// <param name="names">The module's display names.</param>
    public ConfirmRemoteServerHostKeyCommandHandler(
        ServerMigrationDbContext dbContext,
        ServerMigrationAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
        _names = names;
    }

    /// <summary>Pins the key, or says why it was not pinned.</summary>
    /// <param name="command">The validated request; see <see cref="ConfirmRemoteServerHostKeyCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The server in full; <c>RemoteServerNotFound</c> for an identifier that names none; or
    /// <c>RemoteServerHostKeyFingerprintMismatch</c> when no key the server offered has that fingerprint.
    /// </returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        ConfirmRemoteServerHostKeyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var server = await _dbContext.FindServerAsync(command.ServerId, cancellationToken);
        if (server is null)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.OfMissing(command.ServerId),
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        if (!server.PinHostKey(command.Fingerprint, _clock.UtcNow))
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.Of(server),
                Error.Of(nameof(ErrorMessages.RemoteServerHostKeyFingerprintMismatch), ErrorType.Conflict),
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerHostKeyPinned,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var warnings = await _dbContext.ReadWarningsAsync(server.Id, _names, cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, warnings, _names));
    }

    /// <summary>Journals a refused confirmation and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What the confirmation was attempted on.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<RemoteServerDetailDto>> RefuseAsync(
        ConfirmRemoteServerHostKeyCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerHostKeyPinned,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Fail(error);
    }
}
