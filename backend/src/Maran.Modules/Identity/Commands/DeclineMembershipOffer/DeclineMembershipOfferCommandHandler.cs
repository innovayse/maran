using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.DeclineMembershipOffer;

/// <summary>
/// Handles <see cref="DeclineMembershipOfferCommand"/>: the offered login turns an offer down.
/// </summary>
/// <remarks>
/// <b>Only the login the offer was made to can answer it.</b> The offer is looked up by its id and the
/// caller's own user id, so a foreign offer, an unknown one, one already answered and one past its
/// expiry all answer <c>MembershipOfferNotFound</c> (404), exactly as accepting does. Declining
/// creates nothing and changes no membership; it only closes the offer, so the owner can offer again.
/// </remarks>
public sealed class DeclineMembershipOfferCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Records the answer.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated caller, who must be the login the offer was made to.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">Records the answer.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated caller.</param>
    public DeclineMembershipOfferCommandHandler(
        IdentityDbContext dbContext,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Declines the offer.</summary>
    /// <param name="command">The offer being declined.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the offer is closed, or <c>MembershipOfferNotFound</c>.</returns>
    public async Task<Result> HandleAsync(DeclineMembershipOfferCommand command, CancellationToken cancellationToken)
    {
        var userId = SignedInCallerPolicy.Require(_currentUser).UserId;
        var now = _clock.UtcNow;

        var offer = await _dbContext.AccountMembershipOffers.SingleOrDefaultAsync(
            candidate => candidate.Id == command.OfferId && candidate.UserId == userId,
            cancellationToken);
        var answered = offer is not null && offer.IsOpen(now);
        if (answered)
        {
            offer!.Decline(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // The subject is the id the caller named, so a probe of somebody else's offer leaves a trace.
        await _journal.RecordIdentifiedAsync(
            userId,
            IdentityAuditActions.SubUserMembershipOfferDeclined,
            command.OfferId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            answered,
            cancellationToken);

        return answered
            ? Result.Ok()
            : Result.Fail(Error.Of(nameof(ErrorMessages.MembershipOfferNotFound), ErrorType.NotFound));
    }
}
