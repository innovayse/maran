using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.RevokeSession;

/// <summary>Handles <see cref="RevokeSessionCommand"/> by ending one of the caller's own sessions.</summary>
public sealed class RevokeSessionCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Revokes the session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the revocation.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sessionService">Revokes the session.</param>
    /// <param name="auditWriter">Records the revocation.</param>
    public RevokeSessionCommandHandler(
        IdentityDbContext dbContext,
        ISessionService sessionService,
        IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
        _auditWriter = auditWriter;
    }

    /// <summary>Ends the session, provided it belongs to the caller.</summary>
    /// <param name="command">Which session, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success, or <c>SessionNotFound</c>. Another user's session answers "not found" rather than
    /// "forbidden" on purpose (rules/testing.md): 403 would confirm the id exists, turning the
    /// endpoint into an oracle for enumerating other people's sessions.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await _dbContext.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == command.SessionId && s.UserId == command.UserId, cancellationToken);

        if (session is null)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.SessionNotFound)));
        }

        await _sessionService.RevokeAsync(command.SessionId, SessionRevocationReason.Logout, cancellationToken);

        await _auditWriter.WriteAsync(
            new AuditEntry(
                command.UserId,
                string.Empty,
                AuditActions.SessionRevoked,
                command.SessionId.ToString(),
                command.IpAddress,
                command.UserAgent,
                Succeeded: true),
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
