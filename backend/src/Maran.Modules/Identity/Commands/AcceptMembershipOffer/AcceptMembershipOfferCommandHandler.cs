using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Maran.Modules.Identity.Commands.AcceptMembershipOffer;

/// <summary>
/// Handles <see cref="AcceptMembershipOfferCommand"/>: the one place an offer becomes a membership.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consent is the caller's own.</b> The offer is looked up by its id AND the caller's own user id,
/// both from the signed-in token, so an offer addressed to somebody else is simply not found. A foreign
/// offer, an unknown one, one already answered or withdrawn, and one past its expiry all answer
/// <c>MembershipOfferNotFound</c> (404): the answer never says which, and an expired offer is not
/// answered with a conflict because that would confirm it once existed. The operator-viewing-as
/// boundary is on the endpoint (<c>[ViewAsAccess(Refused)]</c>), so an operator cannot accept for
/// the person.
/// </para>
/// <para>
/// <b>The membership comes from the offer's own stored grants.</b> <c>AccountMembershipOffer.Accept</c>
/// resolves the offer and builds the membership in one call, and both are saved in one
/// <c>SaveChanges</c>, so there is no path that marks an offer accepted without its membership and no
/// request field that can widen what was offered.
/// </para>
/// <para>
/// <b>The account is re-read here, under its lock.</b> Whether the account still exists and is
/// in service is the state of its owner membership in this module's own table: an account's
/// suspension suspends every membership of it, and deleting the account removes them. The read happens
/// while holding the account's team lock (the owner membership row), whose UPDATE (a suspension) waits for it,
/// so the state cannot change between the read and the save. A missing owner
/// answers 404 and a suspended one <c>MembershipOfferAccountSuspended</c> (409, a refusal that
/// discloses nothing about another tenant: the person was told of the account by its own offer). An
/// existing membership of this login for the account also answers 404, so a stale offer never makes
/// a second row.
/// </para>
/// <para>
/// <b>Accepting twice, or in parallel, makes one membership.</b> The second attempt either finds the
/// offer resolved or loses on <c>UX_AccountMemberships_UserId_AccountId</c>; the unique violation is
/// answered 404 and nothing is kept.
/// </para>
/// </remarks>
public sealed class AcceptMembershipOfferCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Holds the account's lock while its state is read and the membership is created.</summary>
    private readonly ISubUserTeamLock _teamLock;

    /// <summary>Records the answer.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated caller, who must be the login the offer was made to.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="teamLock">Holds the account's lock while its state is read and the membership is created.</param>
    /// <param name="journal">Records the answer.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated caller.</param>
    public AcceptMembershipOfferCommandHandler(
        IdentityDbContext dbContext,
        ISubUserTeamLock teamLock,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _teamLock = teamLock;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Accepts the offer and admits the caller to the account.</summary>
    /// <param name="command">The offer being accepted.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the membership exists, or a typed refusal.</returns>
    public async Task<Result> HandleAsync(AcceptMembershipOfferCommand command, CancellationToken cancellationToken)
    {
        var userId = SignedInCallerPolicy.Require(_currentUser).UserId;
        var now = _clock.UtcNow;

        var offer = await _dbContext.AccountMembershipOffers.SingleOrDefaultAsync(
            candidate => candidate.Id == command.OfferId && candidate.UserId == userId,
            cancellationToken);
        if (offer is null || !offer.IsOpen(now))
        {
            return await RefuseAsync(command, NotFound(), cancellationToken);
        }

        // The account's lock, taken before its state is read: a suspension updates the owner's
        // membership row, which this lock holds, so the state read below cannot change before the
        // save, and a membership is never created Active under an account that has just been suspended.
        await using var transaction = await _teamLock.BeginAsync(offer.AccountId, cancellationToken);

        var owner = await _dbContext.AccountMemberships.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.AccountId == offer.AccountId && candidate.Role == MembershipRole.Owner,
            cancellationToken);
        if (owner is null)
        {
            return await RollBackAndRefuseAsync(command, NotFound(), transaction, cancellationToken);
        }

        if (owner.State == MembershipState.Suspended)
        {
            return await RollBackAndRefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.MembershipOfferAccountSuspended), ErrorType.Conflict),
                transaction,
                cancellationToken);
        }

        var alreadyHere = await _dbContext.AccountMemberships.AnyAsync(
            candidate => candidate.UserId == userId && candidate.AccountId == offer.AccountId,
            cancellationToken);
        if (alreadyHere)
        {
            return await RollBackAndRefuseAsync(command, NotFound(), transaction, cancellationToken);
        }

        // Resolves the offer and builds the membership from its own stored grants; both are saved
        // below in one statement batch, so they stand or fall together.
        _dbContext.AccountMemberships.Add(offer.Accept(Guid.NewGuid(), now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A parallel acceptance got there first. The statement failed, so the transaction is
            // unusable: nothing of this attempt is kept.
            _dbContext.ChangeTracker.Clear();
            return await RollBackAndRefuseAsync(command, NotFound(), transaction, cancellationToken);
        }

        // Inside the transaction, so the membership and its record stand or fall together.
        await _journal.RecordIdentifiedAsync(
            userId,
            IdentityAuditActions.SubUserMembershipOfferAccepted,
            offer.Id.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>The answer for every offer that cannot be accepted by this caller, whatever the reason.</summary>
    /// <returns>The <c>MembershipOfferNotFound</c> failure kind.</returns>
    private static Error NotFound()
    {
        return Error.Of(nameof(ErrorMessages.MembershipOfferNotFound), ErrorType.NotFound);
    }

    /// <summary>Ends the transaction, then journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="transaction">The transaction holding the account's lock.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> RollBackAndRefuseAsync(
        AcceptMembershipOfferCommand command,
        Error error,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Rolled back first: a journal line written inside it would be undone with it.
        await transaction.RollbackAsync(cancellationToken);
        return await RefuseAsync(command, error, cancellationToken);
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> RefuseAsync(
        AcceptMembershipOfferCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // The subject is the id the caller named: a refused attempt on somebody else's offer still
        // leaves a trace of what was probed.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.SubUserMembershipOfferAccepted,
            command.OfferId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result.Fail(error);
    }
}
