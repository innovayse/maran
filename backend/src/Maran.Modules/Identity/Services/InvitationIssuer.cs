using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
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
/// login that is not <see cref="UserLifecycle.Invited"/> — are the security-relevant part, and two copies
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
/// <b>A login that is not <see cref="UserLifecycle.Invited"/> is refused, not only a suspended
/// one.</b> A fresh token for a live login is a password reset in disguise that bypasses
/// <c>reset-password</c>'s own rate limit, and a login that has accepted its invitation already has a
/// real password: <see cref="User.Activate"/> would overwrite it.
/// </para>
/// <para>
/// <b>An invited owner whose account is suspended is refused too</b>
/// (<c>InvitationAccountSuspended</c>), decided by the owner's membership
/// (<see cref="MembershipState.Suspended"/>). Accepting such an invitation would still leave the
/// login unable to act for the account, but handing a suspended account's owner a fresh credential —
/// mailed, or as a link the administrator passes on — is an offer the suspension exists to withdraw.
/// An account with no login at all is refused the same way: its directory entry carries the
/// account's state (<see cref="AccountSnapshot.State"/>), and a suspended one gets no login created
/// and no token issued.
/// </para>
/// <para>
/// <b>The missing-login insert races, and the loser is told so.</b> Two administrators asking at
/// once — or one double-click — can both read "no login for this account" before either writes one;
/// only <c>UX_AccountMemberships_AccountId_Owner</c>, the partial unique index on the owner
/// membership, actually stops the second row. The loser's insert fails with a unique-violation
/// <see cref="DbUpdateException"/>, which is caught here, the loser's entity is detached, and the
/// call answers <see cref="InvitationAlreadyBeingIssued"/>: the winner's request issues its own token,
/// and what the loser does about that is its caller's decision (a resend answers success; a link
/// request asks again).
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

    /// <summary>Finds the account's owner login through the owner membership.</summary>
    private readonly AccountOwnerFinder _owners;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="owners">Finds the account's owner login through the owner membership.</param>
    public InvitationIssuer(IdentityDbContext dbContext, IAccountDirectory accountDirectory, AccountOwnerFinder owners)
    {
        _dbContext = dbContext;
        _accountDirectory = accountDirectory;
        _owners = owners;
    }

    /// <summary>
    /// Creates the account's login if it is missing, retires every outstanding token for it, issues a
    /// new one and saves.
    /// </summary>
    /// <param name="accountId">The hosting account whose owner is being (re)invited.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/> by the caller.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <see cref="InvitationIssued"/>; <see cref="InvitationAlreadyBeingIssued"/> when a concurrent
    /// call won the race to create the login (see the type's remarks) and nothing was issued here; or
    /// a typed refusal — no such account, a login that already has a password, or an account that is
    /// suspended.
    /// </returns>
    public async Task<Result<InvitationIssueOutcome>> IssueAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Read before the login, for both paths: a missing login is rebuilt from it, and an existing
        // one's invitation is written in the owner's language it records (issue #194).
        var account = await _accountDirectory.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Result<InvitationIssueOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound));
        }

        var owner = await _owners.FindAsync(accountId, cancellationToken);

        User user;
        if (owner is null)
        {
            var prepared = PrepareMissingLogin(account, now);
            if (!prepared.IsSuccess)
            {
                return Result<InvitationIssueOutcome>.Fail(prepared.Error!);
            }

            user = prepared.Value!;
            if (!await InsertLoginAsync(user, account.Id, now, cancellationToken))
            {
                return Result<InvitationIssueOutcome>.Ok(new InvitationAlreadyBeingIssued());
            }
        }
        else if (owner.Login.Lifecycle != UserLifecycle.Invited)
        {
            // Every lifecycle but a pending one already has a password, and a fresh token for it
            // would be a password reset in disguise.
            return Result<InvitationIssueOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationLoginNotPending), ErrorType.Conflict));
        }
        else if (owner.Membership.State == MembershipState.Suspended)
        {
            // A pending invitation on a suspended account is refused with its own code (see the
            // type's remarks: a suspended account is offered no way in).
            return Result<InvitationIssueOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationAccountSuspended), ErrorType.Conflict));
        }
        else
        {
            user = owner.Login;
        }

        var minted = await MintAsync(user, now, cancellationToken);

        return Result<InvitationIssueOutcome>.Ok(
            new InvitationIssued(
                new IssuedInvitation(
                    user.Username, user.Email, minted.Token, minted.ExpiresAt, account.OwnerLanguage)));
    }

    /// <summary>
    /// Retires every outstanding token of an invited login, issues a new one and saves — the one
    /// place a token is minted, whoever the login is for.
    /// </summary>
    /// <param name="user">
    /// The login to invite. The caller has already established that it is <see cref="UserLifecycle.Invited"/>
    /// and that the person asking may invite it: this method trusts both and decides neither.
    /// </param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/> by the caller.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The plaintext token, once, and when it expires.</returns>
    public async Task<MintedInvitationToken> MintAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
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

        return new MintedInvitationToken(new SensitiveString(token), issued.ExpiresAt);
    }

    /// <summary>
    /// Builds the account's missing login exactly as <c>AccountCreatedHandler</c> would have, from the
    /// account's own directory entry. Nothing is added to the context or saved.
    /// </summary>
    /// <param name="account">The account whose login is missing, as its directory entry reads.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <returns>The login to insert, or <c>InvitationAccountSuspended</c> when the account is suspended.</returns>
    private static Result<User> PrepareMissingLogin(AccountSnapshot account, DateTimeOffset now)
    {
        if (account.State == AccountState.Suspended)
        {
            // See the type's remarks: a suspended account is offered no way in, and a login created
            // here would be one.
            return Result<User>.Fail(Error.Of(nameof(ErrorMessages.InvitationAccountSuspended), ErrorType.Conflict));
        }

        // OwnerEmail is null only for an account that predates the column (Account.OwnerEmail's own
        // remarks) — and such an account has had every opportunity, over that whole time, for
        // AccountCreatedHandler to have created its login already. A null reaching here with no
        // login on top of it is an operational anomaly this service cannot repair by guessing an
        // address, so it fails loudly rather than issuing an invitation to a fabricated one.
        var ownerEmail = account.OwnerEmail
            ?? throw new InvalidOperationException(
                $"Account {account.Id} has no login and no recorded owner e-mail; it predates " +
                "Account.OwnerEmail and cannot be recovered by resending an invitation.");

        return Result<User>.Ok(User.Invite(Guid.NewGuid(), account.Username, ownerEmail, now));
    }

    /// <summary>Inserts a prepared login with its owner membership, and says whether this call did.</summary>
    /// <param name="user">The login built by <see cref="PrepareMissingLogin"/>.</param>
    /// <param name="accountId">The hosting account the login belongs to.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <c>true</c> when the login was inserted; <c>false</c> when a concurrent call already won the
    /// race to create it (see the type's remarks).
    /// </returns>
    private async Task<bool> InsertLoginAsync(
        User user,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var membership = AccountMembership.Own(Guid.NewGuid(), user.Id, accountId, now);
        _dbContext.Users.Add(user);
        _dbContext.AccountMemberships.Add(membership);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent call (or a double-click) won the race between the lookup in IssueAsync
            // and this insert — UX_AccountMemberships_AccountId_Owner is what actually stopped the
            // second row, not that lookup. Nothing this call added exists (SaveChanges rolled the insert back), so the
            // entity is detached rather than left tracked as a still-pending add on a context the
            // rest of the caller keeps using.
            _dbContext.Entry(user).State = EntityState.Detached;
            _dbContext.Entry(membership).State = EntityState.Detached;
            return false;
        }

        return true;
    }
}
