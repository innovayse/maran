using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Commands.AcceptInvitation;

/// <summary>
/// Handles <see cref="AcceptInvitationCommand"/>: spends an invitation token and sets the login's
/// first password, making it usable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Modelled on <c>ResetPasswordCommandHandler</c></b>, which this mirrors closely: the token
/// lookup, the <see cref="Maran.Modules.Identity.Domain.Entities.InvitationToken.IsUsable"/> check,
/// the consume-and-retire loop and the single refusal path are the same shape over a different
/// table, and are not re-invented here.
/// </para>
/// <para>
/// <b>One refusal for three different failures.</b> A token that never existed, one that has
/// expired, and one already spent all return <c>InvitationTokenInvalid</c>. Telling them apart would
/// let anybody holding a stolen or guessed token learn whether an invitation is still live, which is
/// exactly the information a stale invitation link must not leak.
/// </para>
/// <para>
/// <b>The login becomes usable only here.</b> A token that never arrives — lost mail, an unread
/// inbox — leaves the account in <c>UserState.Invited</c>, a state nobody can sign in from, rather
/// than one anybody who intercepts the mail can sign in to. There is no session to revoke and no
/// lockout to clear: an invited login has never had either.
/// </para>
/// <para>
/// <b>The token is spent before anything else is written, and every other outstanding token with
/// it.</b> An administrator's resend retires the previous token already (see
/// <c>ResendInvitationCommandHandler</c>), but a user who follows two different invitation mails —
/// one from account creation, one from a resend — must not be able to use the older one after the
/// newer one has been accepted.
/// </para>
/// </remarks>
public sealed class AcceptInvitationCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Hashes the new password with Argon2id.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Records the acceptance, and records a refusal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Hashes the new password.</param>
    /// <param name="journal">Records the acceptance or the refusal.</param>
    /// <param name="clock">The panel's clock.</param>
    public AcceptInvitationCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Spends the token and sets the login's first password.</summary>
    /// <param name="command">The token and the chosen password.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success, or the single refusal every unusable token gets.</returns>
    public async Task<Result<bool>> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tokenHash = InvitationTokenHasher.Hash(command.Token);

        var token = await _dbContext.InvitationTokens
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

        foreach (var outstanding in await _dbContext.InvitationTokens
            .Where(other => other.UserId == user.Id && other.UsedAt == null)
            .ToListAsync(cancellationToken))
        {
            outstanding.Consume(now);
        }

        // The login becomes usable only here, so a token that never arrives leaves an account
        // nobody can sign in to rather than one anybody can.
        user.Activate(_passwordHasher.Hash(command.NewPassword));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            user.Id,
            AuditActions.InvitationAccepted,
            user.Username,
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
    /// or expired invitation for this account" is the entry that matters.
    /// </remarks>
    private async Task<Result<bool>> RefuseAsync(
        Guid? userId,
        AcceptInvitationCommand command,
        CancellationToken cancellationToken)
    {
        if (userId is { } identified)
        {
            await _journal.RecordIdentifiedAsync(
                identified,
                AuditActions.InvitationAcceptRefused,
                identified.ToString(),
                command.IpAddress,
                command.UserAgent,
                succeeded: false,
                cancellationToken);
        }
        else
        {
            await _journal.RecordUnidentifiedAsync(
                AuditActions.InvitationAcceptRefused,
                string.Empty,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
        }

        return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.InvitationTokenInvalid), ErrorType.Validation));
    }
}
