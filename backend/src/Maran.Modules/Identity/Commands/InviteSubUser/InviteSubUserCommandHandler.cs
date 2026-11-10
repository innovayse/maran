using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Identity.Commands.InviteSubUser;

/// <summary>
/// Handles <see cref="InviteSubUserCommand"/>: creates the new person's login (without a password),
/// their membership with the chosen features, and a one-time invitation that is mailed to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The person sets their own password.</b> The owner never learns it and is never handed a link:
/// the invitation is the same token an account owner receives, minted by the same
/// <see cref="InvitationIssuer"/>, mailed through the same composer, and accepted through the same
/// <c>AcceptInvitationCommandHandler</c>. Nothing here stores, logs or journals the token.
/// </para>
/// <para>
/// <b>The membership exists from the invitation, not from its acceptance.</b> The login is
/// <c>Invited</c> until the password is chosen and an invited login cannot sign in, so a membership
/// that exists early grants nothing; it does mean the owner sees the pending person in the list and
/// can limit or withdraw them before they ever accept, and that acceptance needs no knowledge of
/// accounts at all.
/// </para>
/// <para>
/// <b>The answer does not say whether the name or address was free.</b> A login name or an address
/// that already belongs to somebody on this server is a fact about OTHER tenants, and an owner who
/// could read it off the response could test any name or mailbox against the whole server (a
/// cross-tenant enumeration, issue #341). So a request that cannot be honoured because the name or
/// address is taken answers exactly as one that was: the same status, the same body
/// (<see cref="SubUserInvitationDto"/> echoing the owner's own input), and, as far as the rules
/// allow, the same amount of work. Nothing is created and nothing is mailed for a taken request; the
/// real outcome is visible to the owner as the presence or absence of the pending person in their
/// list and to the administrator in the audit log (<c>succeeded: false</c>). Adding an existing
/// person to a second account without their say would put an account in their switcher unasked, and
/// mailing them a token that sets a password they already have is of no use to them, so neither is
/// done.
/// </para>
/// <para>
/// <b>Equal work, by staging the same invitation for a login that is not there.</b> Both paths run
/// one transaction with the same statements in the same order: take the account's lock, count the
/// team, look for a holder of the name or address, then stage a login, a membership and a token (the
/// outstanding-token lookup and the insert batch included) and render the invitation mail. A free
/// request stages the person who was asked for, writes the journal line in that transaction and
/// commits. A taken request stages a DECOY login (a random name and an address that cannot resolve, so
/// it never collides and never reaches anybody), rolls the transaction back, and writes the journal
/// line on its own. Each path therefore ends with one journal row and exactly one durable commit. A
/// decoy is never durable and its token is never published: the rows are rolled back, and the
/// rendered mail is dropped, because a secret-bearing message must not outlive its purpose
/// (rules/csharp.md "a message carrying a secret is never durable").
/// </para>
/// <para>
/// <b>What this does not close, stated.</b> Three differences remain, all small: the free path hands
/// the finished mail to the bus (an in-memory enqueue, the mail itself is sent after the response),
/// a rolled-back transaction costs less than a committed one in write-ahead-log volume (not in
/// flush count: the journal commit is the one flush both paths pay), and the free path's rows are
/// real where the decoy's are random. And an owner who invites a name and then reads their list still
/// learns whether the row appeared. That channel is bounded, not removed: by the per-account cap on
/// sub-users (<see cref="SubUserOptions.MaxPerAccount"/>, refused before the taken check so it says
/// nothing about the target) and by invitation sending's own rate-limit bucket
/// (<c>RateLimitPolicies.InvitationSending</c>). Removing it needs a consenting join flow for
/// existing logins, which is a separate feature.
/// </para>
/// <para>
/// <b>The cap counts members in any state, and the count is decided under a lock.</b> A pending
/// invitation is already a login, a membership and a mailed token, and a suspended member still holds
/// a credential, so all of them count against <see cref="SubUserOptions.MaxPerAccount"/>, and so does every
/// open membership offer (a place held for a person who may accept it). The check
/// runs before the taken check, so a full account is told so whatever it asked for. It runs AFTER
/// <see cref="ISubUserTeamLock"/> has made this invitation the only one for the account: counting
/// first and inserting later, under <c>READ COMMITTED</c>, would let two parallel invitations both
/// read N-1 and both insert, so the account would end over its cap.
/// </para>
/// <para>
/// <b>Uniqueness is still decided by the database.</b> The pre-check is one query over both columns;
/// <c>IX_Users_Username</c> and <c>IX_Users_Email</c> are what actually stop a race, and a unique
/// violation is answered exactly as a pre-check hit is.
/// </para>
/// </remarks>
public sealed class InviteSubUserCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Makes this invitation the only one for the account while the cap is decided.</summary>
    private readonly ISubUserTeamLock _teamLock;

    /// <summary>Retires old tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>Writes the invitation mail.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>The read-only window onto the Accounts module: the account's state and language.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Publishes the mail for the Notifications module to send.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the invitation.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The per-account cap on sub-users.</summary>
    private readonly SubUserOptions _limits;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="teamLock">Makes this invitation the only one for the account while the cap is decided.</param>
    /// <param name="issuer">Retires old tokens and mints the new one.</param>
    /// <param name="composer">Writes the invitation mail.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="bus">Publishes the mail.</param>
    /// <param name="journal">Records the invitation.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    /// <param name="limits">The per-account cap on sub-users.</param>
    public InviteSubUserCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        ISubUserTeamLock teamLock,
        InvitationIssuer issuer,
        InvitationMailComposer composer,
        IAccountDirectory accountDirectory,
        IMessageBus bus,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser,
        IOptions<SubUserOptions> limits)
    {
        _dbContext = dbContext;
        _guard = guard;
        _teamLock = teamLock;
        _issuer = issuer;
        _composer = composer;
        _accountDirectory = accountDirectory;
        _bus = bus;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
        _limits = limits.Value;
    }

    /// <summary>Adds the person and mails their invitation.</summary>
    /// <param name="command">The new person's login name, address and features.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The same <see cref="SubUserInvitationDto"/> whether or not the name or address was free, or a
    /// typed refusal that concerns only the owner's own account (not the owner, a suspended account,
    /// a full team).
    /// </returns>
    public async Task<Result<SubUserInvitationDto>> HandleAsync(InviteSubUserCommand command, CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var account = await _accountDirectory.FindAsync(owned.Value, cancellationToken);
        if (account is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (account.State == AccountState.Suspended)
        {
            // A suspended account is offered no way in for anybody (InvitationIssuer's remarks).
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountSuspended), ErrorType.Conflict), cancellationToken);
        }

        // Everything from here to the commit or the rollback is one transaction holding the account's
        // lock, so the count below is the deciding read and the insert behind it cannot be overtaken.
        await using var transaction = await _teamLock.BeginAsync(owned.Value, cancellationToken);

        // Before the taken check, and independent of the target: the cap is about the owner's own
        // team, so refusing on it discloses nothing about anybody else.
        var teamSize = await _dbContext.AccountMemberships.CountAsync(
            candidate => candidate.AccountId == owned.Value && candidate.Role == MembershipRole.Member,
            cancellationToken);

        // An open membership offer is a team place reserved for a person who may accept it, so it
        // counts here exactly as it does when an offer is made: the two ways in share one cap.
        var capNow = _clock.UtcNow;
        var openOffers = await _dbContext.AccountMembershipOffers.CountAsync(
            candidate => candidate.AccountId == owned.Value && candidate.ResolvedAt == null && candidate.ExpiresAt > capNow,
            cancellationToken);
        if (teamSize + openOffers >= _limits.MaxPerAccount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserLimitReached), ErrorType.Conflict), cancellationToken);
        }

        var uniform = Result<SubUserInvitationDto>.Ok(new SubUserInvitationDto(command.Username, command.Email));
        var taken = await IsTakenAsync(command, cancellationToken);

        // The same staging for both answers (see the type's remarks): a taken request stages a decoy.
        var now = _clock.UtcNow;
        var user = taken
            ? User.Invite(Guid.NewGuid(), DecoyUsername(), DecoyEmail(), now)
            : User.Invite(Guid.NewGuid(), command.Username, command.Email, now);
        var membership = AccountMembership.Admit(
            Guid.NewGuid(), user.Id, owned.Value, command.Features.ToSet(), now);
        _dbContext.Users.Add(user);
        _dbContext.AccountMemberships.Add(membership);

        MintedInvitationToken minted;
        try
        {
            // Saves the login, the membership and the token's digest, inside the transaction.
            minted = await _issuer.MintAsync(user, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race to a login that took the name or address between the pre-check and the
            // insert. The statement failed, so the transaction is unusable: end it, stop tracking
            // what this call added so a retry on this context starts clean, and answer as a taken
            // request does.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            await JournalAsync(command, null, succeeded: false, cancellationToken);
            return uniform;
        }

        // Rendered for both paths so the work is the same; only the honoured one is ever published.
        var mail = _composer.Compose(user.Email, minted.Token.Reveal(), account.OwnerLanguage);

        if (taken)
        {
            // Answered as a success on purpose (see the type's remarks); only the journal knows.
            // The decoy's rows are undone, and the journal line is written on its own afterwards.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            await JournalAsync(command, null, succeeded: false, cancellationToken);
            return uniform;
        }

        // Written inside the transaction, so the invitation and its record stand or fall together
        // and the free path pays one commit, as the taken path's journal write does.
        await JournalAsync(command, user.Id, succeeded: true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Published, not sent, because the mail credential lives in the Notifications module. The
        // token is in the message body for this one hop and is stored nowhere (the message is not
        // durable: rules/csharp.md "a message carrying a secret is never durable").
        await _bus.PublishAsync(mail);

        return uniform;
    }

    /// <summary>A login name no request can have asked for, for the login a taken request stages.</summary>
    /// <returns>A random 32-character name, which no request is going to carry and so never collides.</returns>
    private static string DecoyUsername()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>An address that can never be a real person's, for the login a taken request stages.</summary>
    /// <returns>A random address under the reserved <c>.invalid</c> top-level domain.</returns>
    private static string DecoyEmail()
    {
        return Guid.NewGuid().ToString("N") + "@decoy.invalid";
    }

    /// <summary>Asks whether a login already holds the requested name or address.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> when either is taken by any login on this server.</returns>
    private async Task<bool> IsTakenAsync(InviteSubUserCommand command, CancellationToken cancellationToken)
    {
        // One query over both columns, so the work does not depend on WHICH of the two matched.
        return await _dbContext.Users.AnyAsync(
            candidate => candidate.Username == command.Username || candidate.Email == command.Email,
            cancellationToken);
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SubUserInvitationDto>> RefuseAsync(
        InviteSubUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await JournalAsync(command, null, succeeded: false, cancellationToken);
        return Result<SubUserInvitationDto>.Fail(error);
    }

    /// <summary>Journals the invitation attempt with the owner as actor and the requested login as subject.</summary>
    /// <param name="command">The request.</param>
    /// <param name="userId">The login that was created, or <c>null</c> when none was kept: the subject is then only the typed text.</param>
    /// <param name="succeeded">Whether the invitation was created.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>A task that completes once the entry is written.</returns>
    private async Task JournalAsync(
        InviteSubUserCommand command,
        Guid? userId,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        // The subject is the login name the owner asked for: a refused attempt still leaves a trace of
        // what was attempted, as the other credential-issuing actions do. The audit log is
        // administrator-only, so the real outcome recorded here is not an answer to the owner.
        var actorUserId = SignedInCallerPolicy.Require(_currentUser).UserId;
        if (userId is { } invited)
        {
            // The login exists: it is the subject login, so erasing it later finds this row by identity.
            await _journal.RecordOnUserAsync(
                actorUserId,
                invited,
                IdentityAuditActions.SubUserInvited,
                command.Username,
                command.IpAddress,
                command.UserAgent,
                succeeded,
                cancellationToken);

            return;
        }

        await _journal.RecordIdentifiedAsync(
            actorUserId,
            IdentityAuditActions.SubUserInvited,
            command.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
