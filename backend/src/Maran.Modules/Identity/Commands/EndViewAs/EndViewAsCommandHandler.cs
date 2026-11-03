using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.EndViewAs;

/// <summary>
/// Handles <see cref="EndViewAsCommand"/>: revokes the caller's view-as session and journals the end
/// with the operator as actor (issues #317, #179).
/// </summary>
/// <remarks>
/// It does not issue the operator a token. The operator's own refresh cookie was never touched while
/// they viewed, so the SPA's next step — an ordinary refresh — is what returns them to their own
/// session, through the one path that already re-checks their login. A second way of minting the
/// operator a token here would be a second door to keep in step with that one.
/// </remarks>
public sealed class EndViewAsCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Reads what the session was looking at, for the audit subject.</summary>
    private readonly ViewAsSubjectReader _subjectReader;

    /// <summary>Records the end.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="subjectReader">Reads what the session was looking at, for the audit subject.</param>
    /// <param name="journal">Records the end.</param>
    /// <param name="clock">The panel's clock.</param>
    public EndViewAsCommandHandler(
        IdentityDbContext dbContext,
        ViewAsSubjectReader subjectReader,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _subjectReader = subjectReader;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Ends the caller's view-as session.</summary>
    /// <param name="command">The caller's session and login, stamped from their token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success, or <c>ViewAsNotViewing</c> (409) when the caller's token is not a live view-as session —
    /// an operator signed in as themselves has nothing to go back from.
    /// </returns>
    public async Task<Result> HandleAsync(EndViewAsCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var session = await _dbContext.Sessions.SingleOrDefaultAsync(
            s => s.Id == command.SessionId && s.UserId == command.ViewedUserId && s.ViewAs != null,
            cancellationToken);

        if (session is null || !session.IsActive(now) || session.ViewAs is not { } origin)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ViewAsNotViewing), ErrorType.Conflict));
        }

        session.Revoke(now, SessionRevocationReason.ViewAsEnded);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var operatorName = await _dbContext.Users
            .Where(u => u.Id == origin.ActorUserId)
            .Select(u => u.Username)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
        var viewedName = await _dbContext.Users
            .Where(u => u.Id == session.UserId)
            .Select(u => u.Username)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Read with the view-as token, whose scope is exactly the viewed account or reseller, so the
        // directory answers for it. A session whose facts no longer describe an allowed kind (the
        // viewed login was removed meanwhile) is still ended and journalled, naming the session as the subject.
        var subject = await _subjectReader.ReadAsync(session, cancellationToken);

        await _journal.RecordViewAsAsync(
            origin.ActorUserId,
            operatorName,
            IdentityAuditActions.ViewAsEnded,
            subject is null
                ? $"session {session.Id} as {viewedName}"
                : ViewAsSubjectText.Of(subject.Kind, subject.Name, viewedName),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }
}
