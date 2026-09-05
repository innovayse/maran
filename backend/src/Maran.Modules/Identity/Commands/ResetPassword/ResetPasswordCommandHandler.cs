using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Commands.ResetPassword;

/// <summary>
/// Handles <see cref="ResetPasswordCommand"/>: spends a reset token, replaces the password, and
/// closes every session the account had.
/// </summary>
/// <remarks>
/// <para>
/// <b>One refusal for four different failures.</b> A token that never existed, one that has expired,
/// one already spent, and one whose user has since been deleted all return
/// <c>PasswordResetTokenInvalid</c>. Telling them apart would let anybody with a stolen mailbox — or
/// a guessed token — learn whether an account exists and whether its reset link has been used yet,
/// which is a live account's owner being told "somebody has already reset this" by the attacker who
/// did it. The journal records the refusal; the caller learns only that the link does not work.
/// </para>
/// <para>
/// <b>Every session is revoked, and that is not tidiness.</b> A password is reset because it may be
/// in somebody else's hands, and a stolen refresh cookie survives a password change unless something
/// explicitly ends it. Without this line the reset restores the owner's access without removing the
/// intruder's, which is the failure the whole feature exists to prevent. The mutation that skips it
/// must turn a named test red.
/// </para>
/// <para>
/// <b>The lock is cleared.</b> An account locked by the failed attempts that led its owner to ask for
/// a reset would otherwise still refuse them for the rest of the lockout window, with their brand
/// new password, and the panel would look broken at the exact moment it worked.
/// </para>
/// <para>
/// <b>The token is spent before anything else is written, and every other outstanding token with
/// it.</b> A second link in a second mail is a second key to an account whose owner has just told the
/// panel they lost control of it.
/// </para>
/// </remarks>
public sealed class ResetPasswordCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Hashes the new password with Argon2id.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Closes every session the account had.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the reset, and records a refusal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Hashes the new password.</param>
    /// <param name="sessionService">Closes every session the account had.</param>
    /// <param name="journal">Records the reset or the refusal.</param>
    /// <param name="clock">The panel's clock.</param>
    public ResetPasswordCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ISessionService sessionService,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _sessionService = sessionService;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Spends the token and sets the new password.</summary>
    /// <param name="command">The token and the new password.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success, or the single refusal every unusable token gets.</returns>
    public async Task<Result<bool>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tokenHash = PasswordResetTokenHasher.Hash(command.Token);

        var token = await _dbContext.PasswordResetTokens
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

        if (token is null || !token.IsUsable(now))
        {
            return await RefuseAsync(token?.UserId, command, cancellationToken);
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == token.UserId, cancellationToken);

        if (user is null)
        {
            return await RefuseAsync(token.UserId, command, cancellationToken);
        }

        token.Consume(now);

        foreach (var outstanding in await _dbContext.PasswordResetTokens
            .Where(other => other.UserId == user.Id && other.UsedAt == null)
            .ToListAsync(cancellationToken))
        {
            outstanding.Consume(now);
        }

        user.ChangePassword(_passwordHasher.Hash(command.NewPassword));
        user.ClearLockout();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // After the password is committed, so a failure here cannot leave the account with its old
        // password and no sessions — a state in which nobody can get in at all.
        await _sessionService.RevokeAllAsync(user.Id, SessionRevocationReason.PasswordChanged, cancellationToken);

        await _journal.RecordClaimAsync(
            user.Id,
            user.Username,
            AuditActions.PasswordChanged,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Records a refused token and returns the one refusal every unusable token gets.</summary>
    /// <param name="userId">The user the token named, when it named one; null for a token that never existed.</param>
    /// <param name="command">The attempt. Its token and its password never reach the journal.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// The journal entry carries no subject that could identify the token — a digest in an
    /// append-only table an operator reads is a way to recognise a token somebody still holds. What
    /// it carries is the user, when the token named a real one, because "somebody presented a spent
    /// reset link for this account" is the entry that matters.
    /// </remarks>
    private async Task<Result<bool>> RefuseAsync(
        Guid? userId,
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (userId is { } identified)
        {
            await _journal.RecordIdentifiedAsync(
                identified,
                AuditActions.PasswordResetRefused,
                identified.ToString(),
                command.IpAddress,
                command.UserAgent,
                succeeded: false,
                cancellationToken);
        }
        else
        {
            await _journal.RecordUnidentifiedAsync(
                AuditActions.PasswordResetRefused,
                string.Empty,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
        }

        return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.PasswordResetTokenInvalid), ErrorType.Validation));
    }
}
