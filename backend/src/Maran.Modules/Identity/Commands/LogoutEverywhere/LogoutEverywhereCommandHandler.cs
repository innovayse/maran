using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.LogoutEverywhere;

/// <summary>Handles <see cref="LogoutEverywhereCommand"/> by revoking every session of a user.</summary>
public sealed class LogoutEverywhereCommandHandler
{
    /// <summary>Revokes the sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the sign-out.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Revokes the sessions.</param>
    /// <param name="auditWriter">Records the sign-out.</param>
    public LogoutEverywhereCommandHandler(ISessionService sessionService, IAuditWriter auditWriter)
    {
        _sessionService = sessionService;
        _auditWriter = auditWriter;
    }

    /// <summary>Revokes every live session the user has, including the one making the request.</summary>
    /// <param name="command">Whose sessions to end, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Always a success; the operation is idempotent.</returns>
    public async Task<Result<bool>> HandleAsync(LogoutEverywhereCommand command, CancellationToken cancellationToken)
    {
        await _sessionService.RevokeAllAsync(command.UserId, SessionRevocationReason.LogoutAll, cancellationToken);

        await _auditWriter.WriteAsync(
            new AuditEntry(
                command.UserId,
                string.Empty,
                AuditActions.LoggedOutEverywhere,
                command.UserId.ToString(),
                command.IpAddress,
                command.UserAgent,
                Succeeded: true),
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
