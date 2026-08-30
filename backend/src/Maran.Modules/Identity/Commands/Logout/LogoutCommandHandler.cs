using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.Logout;

/// <summary>Handles <see cref="LogoutCommand"/> by revoking one session.</summary>
public sealed class LogoutCommandHandler
{
    /// <summary>Revokes the session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the sign-out.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Revokes the session.</param>
    /// <param name="auditWriter">Records the sign-out.</param>
    public LogoutCommandHandler(ISessionService sessionService, IAuditWriter auditWriter)
    {
        _sessionService = sessionService;
        _auditWriter = auditWriter;
    }

    /// <summary>Revokes the session behind the presented token.</summary>
    /// <param name="command">The token presented, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always a success. A sign-out that cannot find its session — an expired cookie, a second
    /// click, a session an administrator already ended — has still achieved what the user asked
    /// for, and answering with an error would only teach them that signing out sometimes fails.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var userId = await _sessionService.RevokeByRefreshTokenAsync(
            command.RefreshToken,
            SessionRevocationReason.Logout,
            cancellationToken);

        if (userId is { } actorId)
        {
            await _auditWriter.WriteAsync(
                new AuditEntry(
                    actorId,
                    string.Empty,
                    AuditActions.LoggedOut,
                    actorId.ToString(),
                    command.IpAddress,
                    command.UserAgent,
                    Succeeded: true),
                cancellationToken);
        }

        return Result<bool>.Ok(true);
    }
}
