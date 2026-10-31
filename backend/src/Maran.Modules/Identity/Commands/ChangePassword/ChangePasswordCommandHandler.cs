using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.ChangePassword;

/// <summary>
/// Handles <see cref="ChangePasswordCommand"/>: proves the current password, stores the new one and
/// ends every other sign-in the user had. Threat note:
/// docs/superpowers/notes/2026-10-31-change-password-threat-note.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>A wrong current password counts against the account exactly as a failed sign-in does.</b> A
/// stolen access token without the password would otherwise turn this endpoint into a password
/// oracle that only the per-address rate limit stands in front of, and that limit cannot see an
/// attacker who rotates addresses. The per-user counter can, and it is the same one sign-in feeds,
/// with the operator's threshold and window.
/// </para>
/// <para>
/// <b>A locked account is refused before the password is looked at, with the same code.</b>
/// Verifying during the lock would make the lockout window a window of free guesses.
/// </para>
/// <para>
/// <b>Every other session is revoked; the caller's own sign-in is kept.</b> A password is changed
/// because it may be in somebody else's hands, and their refresh cookie survives the change unless
/// something ends it. The caller's rotation family is kept, so the person who just proved the
/// password is not signed out of the browser they proved it in. Revocation runs after the password
/// is committed, so a failure between the two cannot leave the account with its old password and no
/// sessions.
/// </para>
/// </remarks>
public sealed class ChangePasswordCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the current password and hashes the new one with Argon2id.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Ends every session outside the caller's own sign-in.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>The operator's lockout threshold and window.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>Records the change, and records a refusal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies and hashes passwords.</param>
    /// <param name="sessionService">Ends the other sessions.</param>
    /// <param name="policyCache">The operator's lockout policy.</param>
    /// <param name="journal">Records the change or the refusal.</param>
    /// <param name="clock">The panel's clock.</param>
    public ChangePasswordCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ISessionService sessionService,
        SecurityPolicyCache policyCache,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _sessionService = sessionService;
        _policyCache = policyCache;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Proves the current password, stores the new one and revokes the other sessions.</summary>
    /// <param name="command">Both passwords, the caller and the caller's session.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success; <c>CurrentPasswordIncorrect</c> for a wrong current password or a locked account;
    /// <c>UserNotFound</c> when the token names a user that no longer exists.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.UserNotFound), ErrorType.NotFound));
        }

        var now = _clock.UtcNow;

        if (user.IsLockedOut(now))
        {
            return await RefuseAsync(user, command, cancellationToken);
        }

        if (!_passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            var policy = await _policyCache.GetAsync(cancellationToken);
            user.RecordFailedLogin(now, policy.MaxFailedLoginAttempts, policy.LockoutDuration());
            await _dbContext.SaveChangesAsync(cancellationToken);

            return await RefuseAsync(user, command, cancellationToken);
        }

        user.ChangePassword(_passwordHasher.Hash(command.NewPassword));

        // The current password was just proven, which is what a failed-attempt count is a count of
        // the absence of.
        user.ClearLockout();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _sessionService.RevokeOthersAsync(
            user.Id,
            command.CurrentSessionId,
            SessionRevocationReason.PasswordChanged,
            cancellationToken);

        await _journal.RecordIdentifiedAsync(
            user.Id,
            AuditActions.PasswordChanged,
            user.Id.ToString(),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Records a refused change and returns the one refusal it gets.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="command">The attempt. Neither password reaches the journal.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The refusal.</returns>
    private async Task<Result<bool>> RefuseAsync(
        User user,
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        await _journal.RecordIdentifiedAsync(
            user.Id,
            AuditActions.PasswordChanged,
            user.Id.ToString(),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.CurrentPasswordIncorrect), ErrorType.Validation));
    }
}
