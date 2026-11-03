using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// A hosting account's owner asking an EXISTING login to join the account's team, waiting for that
/// login to answer. It grants nothing: only <see cref="Accept"/>, called for the offered login, brings
/// an <see cref="AccountMembership"/> into being.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a table of its own and not a pending membership.</b> <see cref="MembershipState"/> is
/// <c>Active | Suspended</c>, and resuming an account resumes every membership it has, so a third
/// "pending" state would be activated by an unrelated operation, and every reader of the state
/// (sign-in, refresh, the account switcher, the request guard) would need re-auditing. This row is
/// read by the offer operations alone, so a mistake in it cannot grant access.
/// </para>
/// <para>
/// <b>The only way to the accepted state is the method that also builds the membership.</b>
/// <see cref="Accept"/> returns the <see cref="AccountMembership"/> made from the offer's own stored
/// grants, so no caller can mark an offer accepted without having a membership to save with it, and
/// the grants can never come from a request.
/// </para>
/// <para>
/// <b>The row is the invitee's as well as the account's, so it is not tenant-filtered.</b> The
/// offered login reads it from a session that speaks for another account, or for none; a filter on
/// <see cref="AccountId"/> would hide it from them. It is named in the tenant census's exemption list
/// with that reason, and every operation that reads it compares the caller's own id.
/// </para>
/// <para>
/// <b>The account's name is stored with the offer, as it was when the offer was made.</b> The offered
/// login has no right to read the account (the accounts directory answers only for the caller's own
/// account), and the offer has to say who is asking. The owner typed the offer while acting for the
/// account, so the name is a fact the owner already had.
/// </para>
/// </remarks>
public sealed class AccountMembershipOffer
{
    /// <summary>How long an offer can be answered, counted from the instant it is made.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>Longest stored account name, the column's width.</summary>
    public const int AccountNameLength = 256;

    /// <summary>Longest stored identifier, matching the longest address the panel accepts.</summary>
    public const int IdentifierLength = 254;

    /// <summary>The offer's identity, and the only identifier either side ever names.</summary>
    public Guid Id { get; private set; }

    /// <summary>The login the account's owner asked to join.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The hosting account the offer is to join.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The account's name as it was when the offer was made; the words the offer is shown with.</summary>
    public string AccountName { get; private set; }

    /// <summary>
    /// What the owner typed to name the login, exactly as typed. The owner's own list shows this and
    /// never the login's real name, so an offer row discloses nothing the owner did not supply.
    /// </summary>
    public string Identifier { get; private set; }

    /// <summary>The features the membership will hold if the offer is accepted.</summary>
    public AccountFeature Grants { get; private set; }

    /// <summary>The instant the offer was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant after which the offer can no longer be answered.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The instant the offer was resolved, or <c>null</c> while it is unresolved.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>How the offer was resolved, or <c>null</c> while it is unresolved.</summary>
    public MembershipOfferOutcome? Outcome { get; private set; }

    /// <summary>Creates an offer with every field stated.</summary>
    /// <param name="id">The offer's identity.</param>
    /// <param name="userId">The offered login.</param>
    /// <param name="accountId">The account to join.</param>
    /// <param name="accountName">The account's name, shortened to the column.</param>
    /// <param name="identifier">What the owner typed to name the login, shortened to the column.</param>
    /// <param name="grants">The features the membership will hold.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    private AccountMembershipOffer(
        Guid id,
        Guid userId,
        Guid accountId,
        string accountName,
        string identifier,
        AccountFeature grants,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        AccountId = accountId;
        AccountName = ColumnText.Fit(accountName, AccountNameLength);
        Identifier = ColumnText.Fit(identifier, IdentifierLength);
        Grants = grants;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AccountMembershipOffer()
    {
        AccountName = string.Empty;
        Identifier = string.Empty;
    }

    /// <summary>Makes an offer for a login to join an account.</summary>
    /// <param name="id">The offer's identity.</param>
    /// <param name="userId">The offered login.</param>
    /// <param name="accountId">The account to join.</param>
    /// <param name="accountName">The account's name, as the offer will show it.</param>
    /// <param name="identifier">What the owner typed to name the login.</param>
    /// <param name="grants">The features the membership will hold if accepted.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>An open offer that expires <see cref="Lifetime"/> after <paramref name="createdAt"/>.</returns>
    public static AccountMembershipOffer Offer(
        Guid id,
        Guid userId,
        Guid accountId,
        string accountName,
        string identifier,
        AccountFeature grants,
        DateTimeOffset createdAt)
    {
        return new AccountMembershipOffer(id, userId, accountId, accountName, identifier, grants, createdAt);
    }

    /// <summary>Whether the offer can still be answered at <paramref name="now"/>.</summary>
    /// <param name="now">The instant asked about, taken from <see cref="IClock"/>.</param>
    /// <returns><c>true</c> while it is unresolved and has not reached its expiry.</returns>
    public bool IsOpen(DateTimeOffset now)
    {
        return ResolvedAt is null && now < ExpiresAt;
    }

    /// <summary>
    /// The offered login accepts: the offer is resolved and the membership it describes is built, so the
    /// two are saved together or not at all.
    /// </summary>
    /// <param name="membershipId">The identity of the membership to create.</param>
    /// <param name="now">The instant of acceptance, taken from <see cref="IClock"/>.</param>
    /// <returns>A member membership holding the offer's own grants.</returns>
    /// <exception cref="InvalidOperationException">The offer is not open at <paramref name="now"/>.</exception>
    public AccountMembership Accept(Guid membershipId, DateTimeOffset now)
    {
        Resolve(MembershipOfferOutcome.Accepted, now);
        return AccountMembership.Admit(membershipId, UserId, AccountId, Grants, now);
    }

    /// <summary>The offered login declines.</summary>
    /// <param name="now">The instant of the answer, taken from <see cref="IClock"/>.</param>
    /// <exception cref="InvalidOperationException">The offer is not open at <paramref name="now"/>.</exception>
    public void Decline(DateTimeOffset now)
    {
        Resolve(MembershipOfferOutcome.Declined, now);
    }

    /// <summary>
    /// The account's owner withdraws the offer. Allowed whether or not the offer has expired, so an
    /// owner can clear one they no longer want; an offer that is already resolved stays as it was.
    /// </summary>
    /// <param name="now">The instant of withdrawal, taken from <see cref="IClock"/>.</param>
    public void Cancel(DateTimeOffset now)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        ResolvedAt = now;
        Outcome = MembershipOfferOutcome.Cancelled;
    }

    /// <summary>
    /// Closes an offer that reached its expiry unanswered, so a fresh offer to the same login is not
    /// refused by the one-open-offer-per-login rule. Does nothing to an offer that is resolved or
    /// still open.
    /// </summary>
    /// <param name="now">The instant of closing, taken from <see cref="IClock"/>.</param>
    public void Expire(DateTimeOffset now)
    {
        if (ResolvedAt is not null || now < ExpiresAt)
        {
            return;
        }

        ResolvedAt = now;
        Outcome = MembershipOfferOutcome.Expired;
    }

    /// <summary>Resolves an open offer with <paramref name="outcome"/>.</summary>
    /// <param name="outcome">How it was resolved.</param>
    /// <param name="now">The instant of resolution.</param>
    /// <exception cref="InvalidOperationException">The offer is not open at <paramref name="now"/>.</exception>
    private void Resolve(MembershipOfferOutcome outcome, DateTimeOffset now)
    {
        if (!IsOpen(now))
        {
            throw new InvalidOperationException("Only an open offer can be answered.");
        }

        ResolvedAt = now;
        Outcome = outcome;
    }
}
