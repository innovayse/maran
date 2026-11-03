using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// The fact that one panel login may act for one hosting account, and in what capacity. A login
/// holds one row per account it may act for, so one person with several accounts signs in once and
/// chooses which account a session speaks for (<see cref="Session.ActiveAccountId"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This table is the truth about which accounts a login may act for, and about whether it may
/// act for each of them right now.</b> The login itself carries no account and no suspension: the
/// older <c>Users.AccountId</c> column was dropped once every reader had moved here (issue #325), and
/// a suspension is the <see cref="State"/> of the one membership it concerns (issue #326), so a person
/// holding two accounts loses access to the suspended one and keeps the other.
/// </para>
/// <para>
/// <b>What <see cref="Grants"/> means, in one sentence: it is the set of features a
/// <see cref="MembershipRole.Member"/> may use, and an <see cref="MembershipRole.Owner"/> is not
/// restricted by it.</b> The question "may this login use this feature of this account" therefore has
/// one reader, <see cref="Allows"/>, and an owner's stored value is never consulted by it: an owner
/// row is created with <see cref="AccountFeature.None"/> and that is the absence of a value, not a
/// restriction. The account's plan still bounds both roles; that intersection is enforced where the
/// plan is read, not here. A <see cref="MembershipState.Suspended"/> membership allows nothing,
/// whatever its role or grants.
/// </para>
/// </remarks>
public sealed class AccountMembership
{
    /// <summary>The membership's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The login that may act for the account.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The hosting account the login may act for.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Whether the login owns the account or was added to it.</summary>
    public MembershipRole Role { get; private set; }

    /// <summary>
    /// The features a <see cref="MembershipRole.Member"/> may use. Meaningless for an owner, who is
    /// not restricted by it; read it through <see cref="Allows"/>, never directly.
    /// </summary>
    public AccountFeature Grants { get; private set; }

    /// <summary>
    /// Whether the login may act for the account right now. Changed only by <see cref="Suspend"/> and
    /// <see cref="Resume"/>, which the account's suspension and reactivation drive.
    /// </summary>
    public MembershipState State { get; private set; }

    /// <summary>The instant the membership was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a membership with every field stated.</summary>
    /// <param name="id">The membership's identity.</param>
    /// <param name="userId">The login that may act for the account.</param>
    /// <param name="accountId">The hosting account the login may act for.</param>
    /// <param name="role">Whether the login owns the account or was added to it.</param>
    /// <param name="grants">The features a member may use; <see cref="AccountFeature.None"/> for an owner.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    private AccountMembership(
        Guid id,
        Guid userId,
        Guid accountId,
        MembershipRole role,
        AccountFeature grants,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        AccountId = accountId;
        Role = role;
        Grants = grants;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AccountMembership()
    {
    }

    /// <summary>Creates the membership of the login that owns an account.</summary>
    /// <param name="id">The membership's identity.</param>
    /// <param name="userId">The owning login.</param>
    /// <param name="accountId">The owned account.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>An owner membership, which carries no grants because it needs none.</returns>
    public static AccountMembership Own(Guid id, Guid userId, Guid accountId, DateTimeOffset createdAt)
    {
        return new AccountMembership(id, userId, accountId, MembershipRole.Owner, AccountFeature.None, createdAt);
    }

    /// <summary>Creates the membership of a login the account's owner added to it.</summary>
    /// <param name="id">The membership's identity.</param>
    /// <param name="userId">The added login.</param>
    /// <param name="accountId">The account it may act for.</param>
    /// <param name="grants">The features the owner gave it, as the set of single features they chose.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A member membership restricted to <paramref name="grants"/>.</returns>
    public static AccountMembership Admit(
        Guid id,
        Guid userId,
        Guid accountId,
        AccountFeature grants,
        DateTimeOffset createdAt)
    {
        return new AccountMembership(id, userId, accountId, MembershipRole.Member, grants, createdAt);
    }

    /// <summary>
    /// Stops the login acting for this account because the account was suspended. The role, the
    /// grants and the membership itself are kept, so <see cref="Resume"/> returns the login to
    /// exactly what it had. Suspending a suspended membership changes nothing.
    /// </summary>
    public void Suspend()
    {
        State = MembershipState.Suspended;
    }

    /// <summary>
    /// Lets the login act for this account again because the account was resumed. Resuming an
    /// active membership changes nothing.
    /// </summary>
    public void Resume()
    {
        State = MembershipState.Active;
    }

    /// <summary>Replaces the features a member may use.</summary>
    /// <param name="grants">The new set; an empty set leaves the member with no gated feature at all.</param>
    /// <exception cref="InvalidOperationException">
    /// The membership is an owner's. An owner is not restricted by a grant set, so changing one would
    /// store a value nothing reads and imply a restriction that does not exist.
    /// </exception>
    public void ChangeGrants(AccountFeature grants)
    {
        if (Role != MembershipRole.Member)
        {
            throw new InvalidOperationException("Only a member's grants can be changed; an owner is not restricted by them.");
        }

        Grants = grants;
    }

    /// <summary>Whether this membership lets its login use <paramref name="feature"/> of the account.</summary>
    /// <param name="feature">The feature asked about; a single member of <see cref="AccountFeature"/>.</param>
    /// <returns>
    /// <c>false</c> for every feature while the membership is <see cref="MembershipState.Suspended"/>;
    /// otherwise <c>true</c> for every feature when the login owns the account and, for a member,
    /// <c>true</c> only when <see cref="Grants"/> includes it.
    /// </returns>
    public bool Allows(AccountFeature feature)
    {
        if (State == MembershipState.Suspended)
        {
            return false;
        }

        return Role == MembershipRole.Owner || (Grants & feature) == feature;
    }
}
