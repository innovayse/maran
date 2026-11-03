using Maran.Modules.Identity.Commands.AcceptMembershipOffer;
using Maran.Modules.Identity.Commands.DeclineMembershipOffer;
using Maran.Modules.Identity.Commands.OfferMembership;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The setting every membership-offer test starts from: a <see cref="SubUserWorld"/> (one account, its
/// owner signed in) plus the means to store other people's logins and offers and to build each offer
/// handler for whoever is asking.
/// </summary>
public sealed class MembershipOfferWorld : IDisposable
{
    /// <summary>The account, owner, database, clock, bus and journal every offer test shares.</summary>
    public SubUserWorld World { get; } = new();

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        World.Dispose();
    }

    /// <summary>Stores a login of <paramref name="role"/> that has already chosen its password.</summary>
    /// <param name="username">The login name; the address is <c>name@example.com</c>.</param>
    /// <param name="role">What the login is allowed to reach.</param>
    /// <returns>The stored, active login.</returns>
    public async Task<User> SeedLoginAsync(string username, UserRole role = UserRole.Customer)
    {
        var user = new User(Guid.NewGuid(), username, username + "@example.com", "hash", role, SubUserWorld.Now);
        World.Context.Users.Add(user);
        await World.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>Stores a login that has not yet accepted its own invitation.</summary>
    /// <param name="username">The login name; the address is <c>name@example.com</c>.</param>
    /// <returns>The stored, invited login.</returns>
    public async Task<User> SeedInvitedLoginAsync(string username)
    {
        var user = User.Invite(Guid.NewGuid(), username, username + "@example.com", SubUserWorld.Now);
        World.Context.Users.Add(user);
        await World.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>Stores an open offer from the world's account to <paramref name="user"/>.</summary>
    /// <param name="user">The offered login.</param>
    /// <param name="grants">The features offered.</param>
    /// <param name="accountId">The account making the offer; defaults to the world's.</param>
    /// <returns>The stored offer.</returns>
    public async Task<AccountMembershipOffer> SeedOfferAsync(
        User user,
        AccountFeature grants = AccountFeature.Databases,
        Guid? accountId = null)
    {
        var offer = AccountMembershipOffer.Offer(
            Guid.NewGuid(), user.Id, accountId ?? SubUserWorld.AccountId, "acme", user.Username, grants, World.Clock.UtcNow);
        World.Context.AccountMembershipOffers.Add(offer);
        await World.Context.SaveChangesAsync();
        return offer;
    }

    /// <summary>The caller that stands for <paramref name="user"/> signed in with no account chosen.</summary>
    /// <param name="user">The signed-in login.</param>
    /// <returns>The principal.</returns>
    public static StubCustomerUser CallerFor(User user)
    {
        return new StubCustomerUser(user.Id, user.Username, accountId: null);
    }

    /// <summary>Builds the offering handler for <paramref name="caller"/>.</summary>
    /// <param name="caller">Who is asking; defaults to the owner.</param>
    /// <param name="maxPerAccount">The per-account cap on sub-users.</param>
    /// <returns>The handler.</returns>
    public OfferMembershipCommandHandler NewOfferHandler(StubCustomerUser? caller = null, int maxPerAccount = 25)
    {
        var principal = caller ?? World.Caller;
        return new OfferMembershipCommandHandler(
            World.Context,
            World.Guard(principal),
            World.TeamLock,
            new MembershipOfferMailComposer(new StubEmailTemplates()),
            World.Accounts,
            World.Bus,
            new IdentityAuditJournal(World.Audit, principal),
            World.Clock,
            principal,
            new OptionsWrapper<SubUserOptions>(new SubUserOptions { MaxPerAccount = maxPerAccount }));
    }

    /// <summary>Builds the accepting handler for <paramref name="caller"/>.</summary>
    /// <param name="caller">Who is answering the offer.</param>
    /// <returns>The handler.</returns>
    public AcceptMembershipOfferCommandHandler NewAcceptHandler(StubCustomerUser caller)
    {
        return new AcceptMembershipOfferCommandHandler(
            World.Context, World.TeamLock, new IdentityAuditJournal(World.Audit, caller), World.Clock, caller);
    }

    /// <summary>Builds the declining handler for <paramref name="caller"/>.</summary>
    /// <param name="caller">Who is answering the offer.</param>
    /// <returns>The handler.</returns>
    public DeclineMembershipOfferCommandHandler NewDeclineHandler(StubCustomerUser caller)
    {
        return new DeclineMembershipOfferCommandHandler(
            World.Context, new IdentityAuditJournal(World.Audit, caller), World.Clock, caller);
    }
}
