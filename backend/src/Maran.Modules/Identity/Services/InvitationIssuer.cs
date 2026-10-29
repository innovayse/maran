using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;
using Npgsql;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place an administrator-driven invitation token is minted: finds the account's login
/// (creating it when <c>AccountCreatedHandler</c> never managed to), retires every outstanding token
/// for it, and issues a new one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a service and not a handler's private method.</b> Two administrator actions need
/// exactly this work and differ only in where the plaintext goes afterwards: resending the
/// invitation mails it (<c>ResendInvitationCommandHandler</c>), and creating a link hands it back to
/// the administrator (<c>IssueInvitationLinkCommandHandler</c>). The refusals — no such account, a
/// login that is not <see cref="UserState.Invited"/> — are the security-relevant part, and two copies
/// of them is a way to fix one and forget the other.
/// </para>
/// <para>
/// <b>Why the plaintext can be handed out at all without weakening the token model.</b> The
/// database still holds only the digest; this service never stores, logs or journals the token, it
/// returns it to its caller in a <see cref="SensitiveString"/> and the caller's only two uses are a
/// mail and a one-time, administrator-only response. Nothing here makes a previously issued token
/// recoverable: asking again <i>replaces</i> it (every outstanding token is spent first), which is
/// what keeps "two live invitations to one account" from ever existing.
/// </para>
/// <para>
/// <b>A login that is not <see cref="UserState.Invited"/> is refused, not only an active one.</b> A
/// fresh token for a live account is a password reset in disguise that bypasses
/// <c>reset-password</c>'s own rate limit, and a <see cref="UserState.Suspended"/> login was active
/// before it was suspended, so it already has a real password: <see cref="User.Activate"/> would
/// overwrite it and silently lift the suspension.
/// </para>
/// <para>
/// <b>The missing-login insert races, and the loser is told so.</b> Two administrators asking at
/// once — or one double-click — can both read "no login for this account" before either writes one;
/// only <c>UX_Users_AccountId</c>, the partial unique index on <c>User.AccountId</c>, actually
/// stops the second row. The loser's insert fails with a unique-violation
/// <see cref="DbUpdateException"/>, which is caught here, the loser's entity is detached, and the
/// call answers <c>Ok(null)</c>: the winner's request issues its own token, and what the loser does
/// about that is its caller's decision (a resend answers success; a link request asks again).
/// </para>
/// </remarks>
public sealed class InvitationIssuer
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>
    /// The read-only window onto the Accounts module: the account's system user name and its own
    /// contact address, needed to recreate a login that was never successfully created.
    /// </summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    public InvitationIssuer(IdentityDbContext dbContext, IAccountDirectory accountDirectory)
    {
        _dbContext = dbContext;
        _accountDirectory = accountDirectory;
    }

    /// <summary>
    /// Creates the account's login if it is missing, retires every outstanding token for it, issues a
    /// new one and saves.
    /// </summary>
    /// <param name="accountId">The hosting account whose owner is being (re)invited.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/> by the caller.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The issued invitation; <c>Ok(null)</c> when a concurrent call won the race to create the login
    /// (see the type's remarks) and nothing was issued here; or a typed refusal — no such account, or
    /// a login that already has a password.
    /// </returns>
    public async Task<Result<IssuedInvitation?>> IssueAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == accountId, cancellationToken);

        if (user is null)
        {
            var (accountExists, created) = await CreateMissingLoginAsync(accountId, now, cancellationToken);
            if (!accountExists)
            {
                return Result<IssuedInvitation?>.Fail(
                    Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound));
            }

            if (created is null)
            {
                return Result<IssuedInvitation?>.Ok(null);
            }

            user = created;
        }
        else if (user.State != UserState.Invited)
        {
            return Result<IssuedInvitation?>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationLoginNotPending), ErrorType.Conflict));
        }

        foreach (var outstanding in await _dbContext.InvitationTokens
            .Where(existing => existing.UserId == user.Id && existing.UsedAt == null)
            .ToListAsync(cancellationToken))
        {
            outstanding.Consume(now);
        }

        var token = InvitationTokenHasher.Generate();
        var issued = new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now);
        _dbContext.InvitationTokens.Add(issued);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<IssuedInvitation?>.Ok(
            new IssuedInvitation(user.Username, user.Email, new SensitiveString(token), issued.ExpiresAt));
    }

    /// <summary>
    /// Creates the account's login exactly as <c>AccountCreatedHandler</c> would have, from the
    /// account's own directory entry.
    /// </summary>
    /// <param name="accountId">The account whose login is missing.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <c>(false, null)</c> when no such account exists; <c>(true, null)</c> when the account exists
    /// but a concurrent call already won the race to create its login (see the type's remarks); or
    /// <c>(true, user)</c> with the newly created, saved login.
    /// </returns>
    private async Task<(bool AccountExists, User? User)> CreateMissingLoginAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await _accountDirectory.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return (false, null);
        }

        // OwnerEmail is null only for an account that predates the column (Account.OwnerEmail's own
        // remarks) — and such an account has had every opportunity, over that whole time, for
        // AccountCreatedHandler to have created its login already. A null reaching here with no
        // login on top of it is an operational anomaly this service cannot repair by guessing an
        // address, so it fails loudly rather than issuing an invitation to a fabricated one.
        var ownerEmail = account.OwnerEmail
            ?? throw new InvalidOperationException(
                $"Account {accountId} has no login and no recorded owner e-mail; it predates " +
                "Account.OwnerEmail and cannot be recovered by resending an invitation.");

        var user = User.Invite(Guid.NewGuid(), account.Username, ownerEmail, accountId, now);
        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent call (or a double-click) won the race between the lookup in IssueAsync
            // and this insert — UX_Users_AccountId is what actually stopped the second row, not that
            // lookup. Nothing this call added exists (SaveChanges rolled the insert back), so the
            // entity is detached rather than left tracked as a still-pending add on a context the
            // rest of the caller keeps using.
            _dbContext.Entry(user).State = EntityState.Detached;
            return (true, null);
        }

        return (true, user);
    }
}
