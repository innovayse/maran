using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Answers the Sdk's <see cref="IReauthentication"/>: proves that the signed-in administrator can type
/// their current password again, right now, before an action as privileged as installing code into the
/// panel (the marketplace's install, update, remove and rollback, and switching it on).
/// </summary>
/// <remarks>
/// <para>
/// <b>A wrong password counts against the login exactly as a failed sign-in does.</b> The seam is
/// otherwise a password oracle for anyone holding a stolen access token, standing behind nothing but
/// the per-address rate limit. It feeds the same <see cref="IFailedLoginRecorder"/>, under the same row
/// lock, with the operator's threshold and window, and does not copy any of that logic.
/// </para>
/// <para>
/// <b>It never says which part was wrong.</b> A wrong password, a locked login and a user that no
/// longer exists are all the one <c>ReauthenticationFailed</c> failure of kind
/// <see cref="ErrorType.Validation"/> (never 401: the SPA signs a user out on 401, and a mistyped
/// password is not a lost session). A distinct "locked" answer would tell a guesser that the guessing
/// worked, which is the reason sign-in and the password change refuse a locked login with their ordinary
/// code too. A locked login is refused before its password is looked at, so the lock window is not a
/// window of free guesses.
/// </para>
/// <para>
/// Only a failure is journalled: the caller's own action (the install, the switch) records that it
/// happened. The entry carries the user's id and no origin, because the seam is handed no request; it
/// never carries the password.
/// </para>
/// </remarks>
public sealed class PasswordReauthentication : IReauthentication
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the typed password against the stored Argon2id hash.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Counts a wrong password against the login, serialised with every concurrent one.</summary>
    private readonly IFailedLoginRecorder _failedLoginRecorder;

    /// <summary>Records a refused re-entry.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies passwords.</param>
    /// <param name="failedLoginRecorder">Counts a wrong password against the login.</param>
    /// <param name="journal">Records a refused re-entry.</param>
    /// <param name="clock">The panel's clock.</param>
    public PasswordReauthentication(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IFailedLoginRecorder failedLoginRecorder,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _failedLoginRecorder = failedLoginRecorder;
        _journal = journal;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> VerifyAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
        if (user is null)
        {
            return Refused();
        }

        var now = _clock.UtcNow;

        if (user.IsLockedOut(now))
        {
            await RecordRefusalAsync(userId, ct);
            return Refused();
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            await _failedLoginRecorder.RecordAsync(user, now, ct);
            await RecordRefusalAsync(userId, ct);
            return Refused();
        }

        if (user.FailedLoginAttempts > 0)
        {
            // The password was just proven, which is what a failed-attempt count is a count of the
            // absence of, exactly as after a successful sign-in.
            user.ClearLockout();
            await _dbContext.SaveChangesAsync(ct);
        }

        return Result.Ok();
    }

    /// <summary>Journals a refused re-entry against the user.</summary>
    /// <param name="userId">The user the password belonged to.</param>
    /// <param name="ct">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RecordRefusalAsync(Guid userId, CancellationToken ct)
    {
        await _journal.RecordIdentifiedAsync(
            userId,
            IdentityAuditActions.ReauthenticationFailed,
            userId.ToString(),
            string.Empty,
            string.Empty,
            succeeded: false,
            ct);
    }

    /// <summary>The one refusal every failure gets.</summary>
    /// <returns>The failed result.</returns>
    private static Result Refused()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.ReauthenticationFailed), ErrorType.Validation));
    }
}
