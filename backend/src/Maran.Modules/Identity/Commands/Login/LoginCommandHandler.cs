using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.Login;

/// <summary>Handles <see cref="LoginCommand"/>: verifies the password, then issues a session.</summary>
public sealed class LoginCommandHandler
{
    /// <summary>
    /// An Argon2id hash of a password nobody knows, verified against when the username does not
    /// exist. Without it, a miss returns in microseconds and a hit takes the full cost of the KDF,
    /// so the response time answers "does this account exist?" — and the identical error message
    /// above it would have been for nothing.
    /// </summary>
    private const string DummyHash =
        "$argon2id$v=19$m=65536,t=3,p=2$c2FsdHNhbHRzYWx0c2Ex$Xn7Ux4o0aMbEwbFbNRkUdQGmiWJDLQdEPGqbYAoOKzs";

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the password and reports when its hash needs upgrading.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Signs the access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Issues the refresh-token session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the attempt, successful or not.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="accessTokenIssuer">Signs the access token.</param>
    /// <param name="sessionService">Issues the refresh-token session.</param>
    /// <param name="auditWriter">Records the attempt.</param>
    /// <param name="clock">The panel's clock.</param>
    public LoginCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IAccessTokenIssuer accessTokenIssuer,
        ISessionService sessionService,
        IAuditWriter auditWriter,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _accessTokenIssuer = accessTokenIssuer;
        _sessionService = sessionService;
        _auditWriter = auditWriter;
        _clock = clock;
    }

    /// <summary>Verifies the credentials and, unless a second factor is owed, issues a session.</summary>
    /// <param name="command">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The signed-in outcome, or <c>InvalidCredentialsUnauthorized</c> — one error for both an
    /// unknown username and a wrong password, so the endpoint answers no question about who exists.
    /// </returns>
    public async Task<Result<LoginOutcome>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Username == command.Username, cancellationToken);

        if (user is null)
        {
            // Spend the same time a real verification would, then fail identically.
            _passwordHasher.Verify(command.Password, DummyHash);
            await AuditFailureAsync(null, command, cancellationToken);
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized)));
        }

        // A locked account is refused before its password is even looked at, and refused with the
        // SAME error a wrong password gets. A distinct "locked" answer would be an oracle: it tells
        // an attacker the account exists, and tells them their guessing is working well enough to
        // have tripped the lock. The person actually locked out learns nothing from the response
        // either — which is the cost, paid deliberately, and why the window is short.
        if (user.IsLockedOut(_clock.UtcNow))
        {
            await AuditFailureAsync(user, command, cancellationToken);
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized)));
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Counted on the user row, not in memory: the per-address rate limit cannot see an
            // attacker who rotates addresses, and this is the counter that can.
            user.RecordFailedLogin(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await AuditFailureAsync(user, command, cancellationToken);
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized)));
        }

        // The one moment the plaintext password is known to be correct, and therefore the only
        // moment a hash raised to stronger parameters can be recomputed without asking the user.
        if (_passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.ChangePassword(_passwordHasher.Hash(command.Password));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (user.IsTotpEnabled)
        {
            // Deliberately no session and no token: the second factor has not been shown yet, and
            // issuing anything now would make the factor optional for anyone holding the password.
            return Result<LoginOutcome>.Ok(new LoginOutcome(
                new LoginResultDto(null, null, TwoFactorRequired: true, null),
                null));
        }

        return Result<LoginOutcome>.Ok(await CompleteAsync(user, command, cancellationToken));
    }

    /// <summary>Issues the session and access token for a fully authenticated user.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="command">The attempt that authenticated them.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The response body and the session whose token becomes a cookie.</returns>
    private async Task<LoginOutcome> CompleteAsync(User user, LoginCommand command, CancellationToken cancellationToken)
    {
        var session = await _sessionService.IssueAsync(user.Id, command.IpAddress, command.UserAgent, cancellationToken);
        var accessToken = _accessTokenIssuer.Issue(user, session.SessionId);

        user.RecordLogin(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(
            new AuditEntry(
                user.Id,
                user.Username,
                AuditActions.LoginSucceeded,
                user.Username,
                command.IpAddress,
                command.UserAgent,
                Succeeded: true),
            cancellationToken);

        return new LoginOutcome(
            new LoginResultDto(
                accessToken.Value,
                accessToken.ExpiresAt,
                TwoFactorRequired: false,
                new AuthenticatedUserDto(user.Id, user.Username, user.Email, user.Role, user.AccountId)),
            session);
    }

    /// <summary>Records a refused attempt.</summary>
    /// <param name="user">The user, when the name matched one; null when it did not.</param>
    /// <param name="command">The attempt. Its password never reaches the journal.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task AuditFailureAsync(User? user, LoginCommand command, CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(
                user?.Id,
                command.Username,
                AuditActions.LoginFailed,
                command.Username,
                command.IpAddress,
                command.UserAgent,
                Succeeded: false),
            cancellationToken);
    }
}
