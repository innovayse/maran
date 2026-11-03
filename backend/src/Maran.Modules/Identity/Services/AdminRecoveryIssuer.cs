using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place a break-glass administrator recovery is performed: finds the administrator, spends
/// every outstanding reset token, mints a new one, lifts the lockout, signs the account out
/// everywhere and, when asked, removes its two-factor enrolment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who may call it.</b> Not a request: this service runs only from the local console
/// (<c>maran admin-reset-password</c>), whose authority is shell access to the server — the same
/// authority that can already read the database. There is no principal, and nothing here consults
/// one; Identity declares no query filter, so no principal is needed to read the user table.
/// </para>
/// <para>
/// <b>Administrators only, and only a login that can sign in.</b> A customer's lost password is the
/// reset mail's business; recovering one from the console would turn root on the server into a
/// bypass of the customer area's own rate limit and audit trail. A login that cannot sign in (one
/// still awaiting its first password is the only such state a login has now that suspension belongs
/// to an account's membership) is refused for the reason <see cref="InvitationIssuer"/> gives:
/// consuming an invitation is a decision, not a side effect of a password reset.
/// </para>
/// <para>
/// <b>The token model is unchanged.</b> The database holds the digest of a one-hour, single-use token;
/// the plaintext is returned inside a <see cref="SensitiveString"/> and never stored, logged or
/// journalled. Asking again replaces the previous token.
/// </para>
/// <para>
/// <b>Nothing is hidden about a missing user.</b> The caller is the server's operator, not an
/// anonymous visitor, so "no such user" and "not an administrator" are distinct, typed refusals; the
/// enumeration concern that makes the public reset endpoint answer uniformly does not apply to a
/// shell.
/// </para>
/// <para>
/// <b>Two-factor is removed only when asked.</b> A lost authenticator is the second thing that locks an
/// administrator out, but a reset that silently weakened a stronger login would be a downgrade
/// nobody chose. When requested, the secret and every recovery code are removed together, so no
/// half-enrolled state survives.
/// </para>
/// <para>
/// <b>Order is deliberate.</b> The token and the account changes are saved first, sessions are revoked
/// after, and the audit row is written last, once something has actually changed — a failure part-way
/// leaves an account that still has its old sessions rather than none and no way back in.
/// </para>
/// </remarks>
public sealed class AdminRecoveryIssuer
{
    /// <summary>The longest operator-name claim written to the journal.</summary>
    private const int OperatorClaimMaxLength = 64;

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Closes every session the account had.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Removes the administrator's recovery codes when two-factor is cleared.</summary>
    private readonly IRecoveryCodeService _recoveryCodes;

    /// <summary>Builds the link the token is embedded in.</summary>
    private readonly PasswordResetLinkBuilder _links;

    /// <summary>Records the recovery, and records a refusal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sessionService">Closes every session the account had.</param>
    /// <param name="recoveryCodes">Removes the recovery codes when two-factor is cleared.</param>
    /// <param name="links">Builds the reset link.</param>
    /// <param name="journal">Records the recovery or the refusal.</param>
    /// <param name="clock">The panel's clock.</param>
    public AdminRecoveryIssuer(
        IdentityDbContext dbContext,
        ISessionService sessionService,
        IRecoveryCodeService recoveryCodes,
        PasswordResetLinkBuilder links,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
        _recoveryCodes = recoveryCodes;
        _links = links;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Recovers one administrator's access and returns the link to set a new password.</summary>
    /// <param name="identifier">The administrator's e-mail address (when it contains <c>@</c>) or username.</param>
    /// <param name="clearTwoFactor">Whether to also remove two-factor enrolment and recovery codes.</param>
    /// <param name="operatorClaim">
    /// The operator's name as the console reports it (the invoking <c>SUDO_USER</c>). An unverified
    /// claim, journalled as such.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <see cref="AdminRecoveryIssued"/>; <see cref="NoAdministratorExists"/> when no administrator
    /// exists at all, in which case the caller points the operator at first-run setup
    /// (<c>/setup</c>) and prints no token; or a typed refusal — <c>AdminRecoveryUserNotFound</c>, <c>AdminRecoveryNotAdministrator</c>,
    /// <c>AdminRecoveryLoginNotActive</c>.
    /// </returns>
    public async Task<Result<AdminRecoveryOutcome>> IssueAsync(
        string identifier,
        bool clearTwoFactor,
        string operatorClaim,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var claim = TidyClaim(operatorClaim);

        var user = identifier.Contains('@', StringComparison.Ordinal)
            ? await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Email == identifier, cancellationToken)
            : await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Username == identifier, cancellationToken);

        if (user is null)
        {
            if (!await _dbContext.Users.AnyAsync(candidate => candidate.Role == UserRole.Admin, cancellationToken))
            {
                // No administrator has ever been created: first-run setup is the way in, and a reset
                // token would be a key to a door that does not exist.
                return Result<AdminRecoveryOutcome>.Ok(new NoAdministratorExists());
            }

            await _journal.RecordConsoleRecoveryAsync(identifier, claim, succeeded: false, cancellationToken);
            return Result<AdminRecoveryOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.AdminRecoveryUserNotFound), ErrorType.NotFound));
        }

        if (user.Role != UserRole.Admin)
        {
            await _journal.RecordConsoleRecoveryAsync(user.Id.ToString(), claim, succeeded: false, cancellationToken);
            return Result<AdminRecoveryOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.AdminRecoveryNotAdministrator), ErrorType.Conflict));
        }

        if (!user.MayAuthenticate)
        {
            await _journal.RecordConsoleRecoveryAsync(user.Id.ToString(), claim, succeeded: false, cancellationToken);
            return Result<AdminRecoveryOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.AdminRecoveryLoginNotActive), ErrorType.Conflict));
        }

        foreach (var outstanding in await _dbContext.PasswordResetTokens
            .Where(existing => existing.UserId == user.Id && existing.UsedAt == null)
            .ToListAsync(cancellationToken))
        {
            outstanding.Consume(now);
        }

        var token = PasswordResetTokenHasher.Generate();
        var issued = new PasswordResetToken(Guid.NewGuid(), user.Id, PasswordResetTokenHasher.Hash(token), now);
        _dbContext.PasswordResetTokens.Add(issued);

        user.ClearLockout();
        var hadTwoFactor = user.IsTotpEnabled;
        if (clearTwoFactor)
        {
            user.DisableTotp();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (clearTwoFactor)
        {
            await _recoveryCodes.DiscardAsync(user.Id, cancellationToken);
        }

        await _sessionService.RevokeAllAsync(user.Id, SessionRevocationReason.PasswordChanged, cancellationToken);

        await _journal.RecordConsoleRecoveryAsync(user.Id.ToString(), claim, succeeded: true, cancellationToken);

        return Result<AdminRecoveryOutcome>.Ok(
            new AdminRecoveryIssued(
                new IssuedAdminRecovery(
                    user.Username,
                    new SensitiveString(_links.Build(token)),
                    !_links.HasPublicAddress,
                    issued.ExpiresAt,
                    clearTwoFactor && hadTwoFactor)));
    }

    /// <summary>Drops control characters from the operator's claimed name and bounds its length.</summary>
    /// <param name="operatorClaim">The name as the console reported it.</param>
    /// <returns>The claim safe to write to a log-like column; <c>unknown</c> when nothing usable remains.</returns>
    private static string TidyClaim(string operatorClaim)
    {
        var cleaned = new string(operatorClaim.Where(character =>
        {
            return !char.IsControl(character);
        }).ToArray()).Trim();
        if (cleaned.Length > OperatorClaimMaxLength)
        {
            cleaned = cleaned[..OperatorClaimMaxLength];
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
    }
}
