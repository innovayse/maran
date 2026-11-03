using Maran.Modules.Identity.Commands.RemoveSubUser;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.RemoveSubUser;

/// <summary>Behavioural contract of the sub-user removal handler.</summary>
public sealed class RemoveSubUserCommandHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private RemoveSubUserCommandHandler NewHandler(StubCustomerUser? caller = null)
    {
        var principal = caller ?? _world.Caller;
        return new RemoveSubUserCommandHandler(
            _world.Context,
            _world.Guard(principal),
            new IdentityAuditJournal(_world.Audit, principal),
            _world.Clock,
            principal);
    }

    private static RemoveSubUserCommand Command(Guid userId)
    {
        return new RemoveSubUserCommand(userId, "203.0.113.7", "agent");
    }

    /// <summary>A login made for this team only goes away with its access, sessions included.</summary>
    [Fact]
    public async Task Removing_a_team_only_login_deletes_it_with_its_membership_and_sessions()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        _world.Context.Sessions.Add(NewSession(member.Id, SubUserWorld.AccountId));
        await _world.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(await _world.Context.Users.AnyAsync(candidate => candidate.Id == member.Id));
        Assert.False(await _world.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == member.Id));
        Assert.False(await _world.Context.Sessions.AnyAsync(candidate => candidate.UserId == member.Id));
    }

    /// <summary>A login with another membership stays, and only the sessions for this account end.</summary>
    [Fact]
    public async Task Removing_a_login_with_other_access_revokes_only_this_accounts_sessions()
    {
        var elsewhere = Guid.NewGuid();
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        _world.Context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, elsewhere, AccountFeature.Cron, SubUserWorld.Now));
        var here = NewSession(member.Id, SubUserWorld.AccountId);
        var there = NewSession(member.Id, elsewhere);
        _world.Context.Sessions.AddRange(here, there);
        await _world.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(await _world.Context.Users.AnyAsync(candidate => candidate.Id == member.Id));
        var storedHere = await _world.Context.Sessions.SingleAsync(candidate => candidate.Id == here.Id);
        Assert.Equal(SessionRevocationReason.MembershipRemoved, storedHere.Revocation?.Reason);
        var storedThere = await _world.Context.Sessions.SingleAsync(candidate => candidate.Id == there.Id);
        Assert.Null(storedThere.Revocation);
        Assert.False(await _world.Context.AccountMemberships.AnyAsync(
            candidate => candidate.UserId == member.Id && candidate.AccountId == SubUserWorld.AccountId));
    }

    /// <summary>The removal is journalled against the login name that left.</summary>
    [Fact]
    public async Task Removing_is_journalled()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserRemoved, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Another account's sub-user is not found and stays exactly as it was.</summary>
    [Fact]
    public async Task Another_accounts_sub_user_is_not_found_and_is_left_in_place()
    {
        var stranger = await _world.SeedMemberAsync("rival", AccountFeature.Databases, accountId: Guid.NewGuid());

        var result = await NewHandler().HandleAsync(Command(stranger.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.True(await _world.Context.Users.AnyAsync(candidate => candidate.Id == stranger.Id));
        Assert.True(await _world.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == stranger.Id));
        Assert.False(Assert.Single(_world.Audit.Written).Succeeded);
    }

    /// <summary>The owner cannot remove themselves through the sub-user endpoint.</summary>
    [Fact]
    public async Task The_owner_cannot_be_removed_as_a_sub_user()
    {
        var result = await NewHandler().HandleAsync(Command(_world.Owner.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.True(await _world.Context.Users.AnyAsync(candidate => candidate.Id == _world.Owner.Id));
    }

    /// <summary>A team member cannot remove a teammate.</summary>
    [Fact]
    public async Task A_member_cannot_remove_a_teammate()
    {
        var actor = await _world.SeedMemberAsync("actor", AccountFeature.All);
        var victim = await _world.SeedMemberAsync("victim", AccountFeature.Databases);
        var caller = new StubCustomerUser(actor.Id, actor.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(Command(victim.Id), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        Assert.True(await _world.Context.Users.AnyAsync(candidate => candidate.Id == victim.Id));
    }

    /// <summary>Removing the row an open offer is listed as withdraws the offer and touches no login or membership.</summary>
    [Fact]
    public async Task Removing_an_open_offer_withdraws_it_and_touches_no_login()
    {
        var person = new User(Guid.NewGuid(), "bob", "bob@example.com", "hash", UserRole.Customer, SubUserWorld.Now);
        _world.Context.Users.Add(person);
        var offer = AccountMembershipOffer.Offer(
            Guid.NewGuid(), person.Id, SubUserWorld.AccountId, "acme", "bob", AccountFeature.Databases, SubUserWorld.Now);
        _world.Context.AccountMembershipOffers.Add(offer);
        await _world.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(offer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _world.Context.AccountMembershipOffers.SingleAsync();
        Assert.Equal(MembershipOfferOutcome.Cancelled, stored.Outcome);
        Assert.True(await _world.Context.Users.AnyAsync(candidate => candidate.Id == person.Id));
        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserRemoved, entry.Action);
        Assert.Equal("bob", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Another account's offer is not found, and stays open.</summary>
    [Fact]
    public async Task Another_accounts_offer_is_not_found_and_stays_open()
    {
        var person = new User(Guid.NewGuid(), "bob", "bob@example.com", "hash", UserRole.Customer, SubUserWorld.Now);
        _world.Context.Users.Add(person);
        var offer = AccountMembershipOffer.Offer(
            Guid.NewGuid(), person.Id, Guid.NewGuid(), "other", "bob", AccountFeature.Databases, SubUserWorld.Now);
        _world.Context.AccountMembershipOffers.Add(offer);
        await _world.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(offer.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.True((await _world.Context.AccountMembershipOffers.SingleAsync()).IsOpen(SubUserWorld.Now));
    }

    private static Session NewSession(Guid userId, Guid accountId)
    {
        return new Session(
            Guid.NewGuid(),
            userId,
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            SubUserWorld.Now,
            SubUserWorld.Now.AddDays(14),
            "203.0.113.7",
            "agent",
            accountId);
    }
}
