using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.LogoutEverywhere;

/// <summary>Handles <see cref="LogoutEverywhereCommand"/> by revoking every session of a user.</summary>
public sealed class LogoutEverywhereCommandHandler
{
    /// <summary>Revokes the sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the sign-out.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Revokes the sessions.</param>
    /// <param name="journal">Records the sign-out.</param>
    public LogoutEverywhereCommandHandler(ISessionService sessionService, IdentityAuditJournal journal)
    {
        _sessionService = sessionService;
        _journal = journal;
    }

    /// <summary>Revokes every live session the user has, including the one making the request.</summary>
    /// <param name="command">Whose sessions to end, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Always a success; the operation is idempotent.</returns>
    public async Task<Result<bool>> HandleAsync(LogoutEverywhereCommand command, CancellationToken cancellationToken)
    {
        await _sessionService.RevokeAllAsync(command.UserId, SessionRevocationReason.LogoutAll, cancellationToken);

        await _journal.RecordIdentifiedAsync(
            command.UserId,
            AuditActions.LoggedOutEverywhere,
            command.UserId.ToString(),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
