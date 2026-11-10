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

    /// <summary>Reads the name the sign-out entry is about.</summary>
    private readonly LoginNameReader _loginNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Revokes the sessions.</param>
    /// <param name="journal">Records the sign-out.</param>
    /// <param name="loginNames">Reads the name the sign-out entry is about.</param>
    public LogoutEverywhereCommandHandler(
        ISessionService sessionService,
        IdentityAuditJournal journal,
        LoginNameReader loginNames)
    {
        _sessionService = sessionService;
        _journal = journal;
        _loginNames = loginNames;
    }

    /// <summary>Revokes every live session the user has, including the one making the request.</summary>
    /// <param name="command">Whose sessions to end, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Always a success. A repeat finds no live session left to end, but journals its own entry.</returns>
    public async Task<Result> HandleAsync(LogoutEverywhereCommand command, CancellationToken cancellationToken)
    {
        await _sessionService.RevokeAllAsync(command.UserId, SessionRevocationReason.LogoutAll, cancellationToken);

        // The subject is the login's name, as a sign-in's is, and the entry carries the login's id so
        // that erasing the login finds it: a name in a subject column is personal data.
        await _journal.RecordOnUserAsync(
            command.UserId,
            command.UserId,
            IdentityAuditActions.LoggedOutEverywhere,
            await _loginNames.ReadAsync(command.UserId, cancellationToken),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }
}
