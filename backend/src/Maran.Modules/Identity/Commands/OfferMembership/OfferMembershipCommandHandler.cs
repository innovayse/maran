using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Constants;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Identity.Commands.OfferMembership;

/// <summary>
/// Handles <see cref="OfferMembershipCommand"/>: stores an offer for an existing login to join the
/// caller's account and queues a plain notification to that login. Nothing is granted until the login
/// accepts inside the panel (<c>AcceptMembershipOfferCommandHandler</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The answer does not say whether anything happened.</b> The owner typed one identifier, and
/// whether it names a login that qualifies is a fact about OTHER people on this server. So a request
/// that cannot be honoured answers exactly as one that was: the same status and the same body
/// (<see cref="MembershipOfferRequestedDto"/>, an echo of the owner's own input), nothing is stored,
/// and nothing is mailed. The real outcome is the audit log's (<c>succeeded: false</c>,
/// administrators only) and, bounded and delayed, the presence of a pending row in the owner's list.
/// Only the owner's own state is ever refused out loud, and it is decided BEFORE the target is read:
/// not the owner of the account, the team cap, and the validator's input rules.
/// </para>
/// <para>
/// <b>Who qualifies.</b> An offer is stored only when the identifier matches a login (one query over
/// both columns, so the work does not depend on which matched) that is <see cref="UserRole.Customer"/>
/// and <see cref="UserLifecycle.Active"/>, holds no membership of this account in any state, has no
/// open offer from this account, and the account exists and is not suspended. An administrator, an
/// invited login and a deactivated one are all no-ops, so an owner cannot pull an administrator onto a
/// customer team and cannot tell that was the reason.
/// </para>
/// <para>
/// <b>Open offers count toward <see cref="SubUserOptions.MaxPerAccount"/>.</b> Members in any state
/// and offers that are open and unexpired are added up under the account's team lock, before the
/// target is read, so an owner cannot park unlimited offers and the refusal depends only on their own
/// team.
/// </para>
/// <para>
/// <b>An expired offer is treated as absent.</b> It is still unresolved, so it would collide with a
/// fresh offer on the one-open-offer-per-login index. It is closed (<c>Expire</c>) and saved before
/// the new one is staged. It does not change the answer.
/// </para>
/// <para>
/// <b>The mail goes to the stored address of the login the identifier resolved to</b>, never to the
/// typed text, so typing a stranger's address that is not a login mails nobody. It carries no token and
/// no link secret (<see cref="MembershipOfferMailComposer"/>), so, unlike an invitation, nothing in it
/// is a secret that a durable queue would keep.
/// </para>
/// <para>
/// <b>Equal work, by staging a decoy offer for a login that is not there.</b> Both paths run one
/// transaction holding the account's lock with the same statements in the same order: count the team and the open offers,
/// look up the login, check for a membership, check for an open offer, then stage an offer and render
/// the mail. An honoured request stages the offer for the login it resolved, journals in that
/// transaction and commits. Any other request stages a DECOY offer (addressed to the owner's own login,
/// which exists, so the foreign key holds, and which never has an offer to its own account, so the
/// index never fires), rolls the transaction back and journals on its own. The decoy's mail is rendered
/// and dropped.
/// </para>
/// <para>
/// <b>What this does not close, stated.</b> The honoured path commits and publishes; the no-op path
/// rolls back and publishes nothing. The commit writes more log than a rollback, and the mail is an
/// in-memory enqueue on one side and a dropped render on the other; publishing the decoy would mail a
/// stranger. An expired row, when there is one, adds an extra statement to the honoured path only,
/// since only a real login can have one. These are well below network jitter on one request, and a
/// patient measurer could average them at the account's hourly sending bucket. The owner's list is a
/// second channel: a stored offer appears in it. See the threat note for the bounds.
/// </para>
/// </remarks>
public sealed class OfferMembershipCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Makes this request the only team change for the account while the cap is decided.</summary>
    private readonly ISubUserTeamLock _teamLock;

    /// <summary>Writes the notification mail.</summary>
    private readonly MembershipOfferMailComposer _composer;

    /// <summary>The read-only window onto the Accounts module: the account's name, state and language.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Publishes the mail for the Notifications module to send.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the request.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner, the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The per-account cap on sub-users.</summary>
    private readonly SubUserOptions _limits;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="teamLock">Makes this request the only team change for the account while the cap is decided.</param>
    /// <param name="composer">Writes the notification mail.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="bus">Publishes the mail.</param>
    /// <param name="journal">Records the request.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    /// <param name="limits">The per-account cap on sub-users.</param>
    public OfferMembershipCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        ISubUserTeamLock teamLock,
        MembershipOfferMailComposer composer,
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
        _composer = composer;
        _accountDirectory = accountDirectory;
        _bus = bus;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
        _limits = limits.Value;
    }

    /// <summary>Stores the offer and queues the notification, when the identifier qualifies.</summary>
    /// <param name="command">The identifier typed and the features offered.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The same <see cref="MembershipOfferRequestedDto"/> whatever became of the request, or a typed
    /// refusal that concerns only the owner's own state (not the owner, a full team).
    /// </returns>
    public async Task<Result<MembershipOfferRequestedDto>> HandleAsync(
        OfferMembershipCommand command,
        CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var accountId = owned.Value;
        var ownerId = SignedInCallerPolicy.Require(_currentUser).UserId;
        var account = await _accountDirectory.FindAsync(accountId, cancellationToken);
        var now = _clock.UtcNow;

        // Everything from here to the commit or the rollback is one transaction holding the account's
        // lock, so the count below is the deciding read and the insert behind it cannot be overtaken.
        await using var transaction = await _teamLock.BeginAsync(accountId, cancellationToken);

        // Before the target is read, and independent of it: the cap is about the owner's own team, so
        // refusing on it discloses nothing about anybody else.
        var teamSize = await _dbContext.AccountMemberships.CountAsync(
            candidate => candidate.AccountId == accountId && candidate.Role == MembershipRole.Member,
            cancellationToken);
        var openOffers = await _dbContext.AccountMembershipOffers.CountAsync(
            candidate => candidate.AccountId == accountId && candidate.ResolvedAt == null && candidate.ExpiresAt > now,
            cancellationToken);
        if (teamSize + openOffers >= _limits.MaxPerAccount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserLimitReached), ErrorType.Conflict), cancellationToken);
        }

        var uniform = Result<MembershipOfferRequestedDto>.Ok(new MembershipOfferRequestedDto(command.Identifier));
        var target = await FindQualifyingLoginAsync(command, account, accountId, now, cancellationToken);

        // The same staging for both answers (see the type's remarks): a request that does not qualify
        // stages a decoy addressed to the owner's own login.
        var offer = AccountMembershipOffer.Offer(
            Guid.NewGuid(),
            target?.Id ?? ownerId,
            accountId,
            account?.Username ?? string.Empty,
            command.Identifier,
            command.Features.ToSet(),
            now);
        _dbContext.AccountMembershipOffers.Add(offer);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race to another offer for the same login and account. The statement failed, so
            // the transaction is unusable: end it, stop tracking what this call added, and answer as a
            // request that did not qualify does.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            await JournalAsync(command, succeeded: false, cancellationToken);
            return uniform;
        }

        // Rendered for both paths so the work is the same; only the honoured one is ever published.
        var mail = _composer.Compose(
            target?.Email ?? DecoyEmail(), offer.AccountName, account?.OwnerLanguage ?? PanelLanguages.English);

        if (target is null)
        {
            // Answered as a success on purpose (see the type's remarks); only the journal knows. The
            // decoy is undone and the journal line is written on its own afterwards.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            await JournalAsync(command, succeeded: false, cancellationToken);
            return uniform;
        }

        // Written inside the transaction, so the offer and its record stand or fall together and this
        // path pays one commit, as the other path's journal write does.
        await JournalAsync(command, succeeded: true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Published after the commit, so no mail names an offer that was rolled back. The message
        // carries no secret, so losing it in the window before it is queued costs the person a
        // notification they can still find on their own page.
        await _bus.PublishAsync(mail);

        return uniform;
    }

    /// <summary>An address that can never be a real person's, for the mail a request that did not qualify renders.</summary>
    /// <returns>A random address under the reserved <c>.invalid</c> top-level domain.</returns>
    private static string DecoyEmail()
    {
        return Guid.NewGuid().ToString("N") + "@decoy.invalid";
    }

    /// <summary>
    /// Reads everything the decision needs, in the same order whatever it finds, and closes an expired
    /// offer for the same login so it cannot collide with the one about to be stored.
    /// </summary>
    /// <param name="command">The request.</param>
    /// <param name="account">The caller's account as the directory reads it, or <c>null</c>.</param>
    /// <param name="accountId">The account the offer is for.</param>
    /// <param name="now">The panel's current instant.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The login to address when the request qualifies, otherwise nothing.</returns>
    private async Task<User?> FindQualifyingLoginAsync(
        OfferMembershipCommand command,
        AccountSnapshot? account,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // One query over both columns, so the work does not depend on WHICH of the two matched.
        var login = await _dbContext.Users
            .Where(candidate => candidate.Username == command.Identifier || candidate.Email == command.Identifier)
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Asked even when there is no login (against the empty id), so the reads do not depend on it.
        var loginId = login?.Id ?? Guid.Empty;
        var alreadyHere = await _dbContext.AccountMemberships.AnyAsync(
            candidate => candidate.UserId == loginId && candidate.AccountId == accountId,
            cancellationToken);
        var outstanding = await _dbContext.AccountMembershipOffers.FirstOrDefaultAsync(
            candidate => candidate.UserId == loginId && candidate.AccountId == accountId && candidate.ResolvedAt == null,
            cancellationToken);

        if (outstanding is not null && !outstanding.IsOpen(now))
        {
            // Unanswered past its expiry: absent as far as this request is concerned, and closed so
            // the one-open-offer index does not refuse the new one.
            outstanding.Expire(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            outstanding = null;
        }

        var qualifies = login is { Role: UserRole.Customer, Lifecycle: UserLifecycle.Active }
            && !alreadyHere
            && outstanding is null
            && account is { State: AccountState.Active };

        return qualifies ? login : null;
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MembershipOfferRequestedDto>> RefuseAsync(
        OfferMembershipCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await JournalAsync(command, succeeded: false, cancellationToken);
        return Result<MembershipOfferRequestedDto>.Fail(error);
    }

    /// <summary>Journals the request with the owner as actor and what they typed as subject.</summary>
    /// <param name="command">The request.</param>
    /// <param name="succeeded">Whether an offer was stored.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>A task that completes once the entry is written.</returns>
    private async Task JournalAsync(OfferMembershipCommand command, bool succeeded, CancellationToken cancellationToken)
    {
        // The subject is what the owner typed: a request that qualified nobody still leaves a trace of
        // what was asked. The audit log is administrator-only, so the real outcome recorded here is not
        // an answer to the owner.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.SubUserMembershipOffered,
            command.Identifier,
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
