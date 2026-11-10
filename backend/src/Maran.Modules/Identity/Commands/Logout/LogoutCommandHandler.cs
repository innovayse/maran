using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.Logout;

/// <summary>Handles <see cref="LogoutCommand"/> by revoking one session.</summary>
public sealed class LogoutCommandHandler
{
    /// <summary>Revokes the session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the sign-out.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Reads the name the sign-out entry is about.</summary>
    private readonly LoginNameReader _loginNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Revokes the session.</param>
    /// <param name="journal">Records the sign-out.</param>
    /// <param name="loginNames">Reads the name the sign-out entry is about.</param>
    public LogoutCommandHandler(ISessionService sessionService, IdentityAuditJournal journal, LoginNameReader loginNames)
    {
        _sessionService = sessionService;
        _journal = journal;
        _loginNames = loginNames;
    }

    /// <summary>Revokes the session behind the presented token.</summary>
    /// <param name="command">The token presented, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always a success. A sign-out that cannot find its session — an expired cookie, a second
    /// click, a session an administrator already ended — has still achieved what the user asked
    /// for, and answering with an error would only teach them that signing out sometimes fails.
    /// </returns>
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var userId = await _sessionService.RevokeByRefreshTokenAsync(
            command.RefreshToken,
            SessionRevocationReason.Logout,
            cancellationToken);

        if (userId is { } actorId)
        {
            // The subject is the login's name, as a sign-in's is, and the entry carries the login's id so
            // that erasing the login finds it: a name in a subject column is personal data.
            await _journal.RecordOnUserAsync(
                actorId,
                actorId,
                IdentityAuditActions.LoggedOut,
                await _loginNames.ReadAsync(actorId, cancellationToken),
                command.IpAddress,
                command.UserAgent,
                succeeded: true,
                cancellationToken);
        }

        return Result.Ok();
    }
}
