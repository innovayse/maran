using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the Identity module releases when an account is deleted, what it deliberately keeps, and
/// what it leaves alone.
/// </summary>
/// <remarks>
/// The assertions are on OBSERVED ABSENCE of each dependent table in turn rather than on the
/// <c>User</c> row alone, and that is the point of the file. The panel's residue auditor counts rows
/// carrying an <c>AccountId</c>, and <c>Session</c>, <c>RecoveryCode</c> and
/// <c>PasswordResetToken</c> carry a <c>UserId</c> instead — so a handler that removed the logins
/// and left their refresh tokens would be pronounced clean by the audit and would report a completed
/// deletion over live credentials. Nothing except these assertions looks at those three tables.
/// </remarks>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>The account being deleted.</summary>
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>An account that is not being deleted, and whose login must survive.</summary>
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>The instant every seeded row is stamped with.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The address whose refused sign-ins are counted, belonging to no account.</summary>
    private const string AttackerAddress = "198.51.100.7";

    /// <summary>The shortest password the seeded server-wide policy accepts.</summary>
    private const int PolicyMinimumPasswordLength = 14;

    /// <summary>The consecutive refused sign-ins the seeded server-wide policy locks an account after.</summary>
    private const int PolicyMaxFailedLoginAttempts = 7;

    /// <summary>Deleting an account removes every login it owns.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_login_it_owns()
    {
        // Two logins, because a handler that removed only the first would pass a single-row test
        // and leave a working password against a tenant that no longer exists.
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.AccountMemberships.Where(membership => membership.AccountId == OwnerAccountId).ToListAsync());
        Assert.Empty(await context.Users.Where(user => user.Username.StartsWith("owner-")).ToListAsync());
    }

    /// <summary>Deleting an account removes the refresh sessions of the logins it owns.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_the_refresh_sessions_of_the_logins_it_owns()
    {
        using var context = await SeedAsync();
        var owned = await OwnedUserIdsAsync(context);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.Sessions.Where(session => owned.Contains(session.UserId)).ToListAsync());
    }

    /// <summary>Deleting an account removes the outstanding password reset tokens of its logins.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_the_outstanding_password_reset_tokens_of_its_logins()
    {
        // A reset token is a live permission to set a password. Left behind, it is the one leftover
        // in this module that is worth more to an attacker than the row it hangs off.
        using var context = await SeedAsync();
        var owned = await OwnedUserIdsAsync(context);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.PasswordResetTokens.Where(token => owned.Contains(token.UserId)).ToListAsync());
    }

    /// <summary>Deleting an account removes the two factor recovery codes of its logins.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_the_two_factor_recovery_codes_of_its_logins()
    {
        using var context = await SeedAsync();
        var owned = await OwnedUserIdsAsync(context);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.RecoveryCodes.Where(code => owned.Contains(code.UserId)).ToListAsync());
    }

    /// <summary>Deleting an account removes every offer made from it, answered or not, and another account's offers stay.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_offer_made_from_it_and_leaves_other_accounts_offers()
    {
        // The residue audit counts offers by AccountId, so one left behind would fail the deletion; it
        // is also a way onto a team of an account that no longer exists.
        using var context = await SeedAsync();
        var stranger = await context.Users.SingleAsync(user => user.Username == "stranger");
        context.AccountMembershipOffers.AddRange(
            AccountMembershipOffer.Offer(Guid.NewGuid(), stranger.Id, OwnerAccountId, "gone", "x", AccountFeature.None, Now),
            AccountMembershipOffer.Offer(Guid.NewGuid(), stranger.Id, StrangerAccountId, "kept", "x", AccountFeature.None, Now));
        await context.SaveChangesAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var left = await context.AccountMembershipOffers.SingleAsync();
        Assert.Equal(StrangerAccountId, left.AccountId);
    }

    /// <summary>An account that has offers but no memberships still loses its offers.</summary>
    [Fact]
    public async Task Deleting_an_account_with_offers_and_no_memberships_still_removes_the_offers()
    {
        using var context = IdentityTestContext.Create();
        var person = new User(Guid.NewGuid(), "bob", "bob@example.com", "hash", UserRole.Customer, Now);
        context.Users.Add(person);
        context.AccountMembershipOffers.Add(
            AccountMembershipOffer.Offer(Guid.NewGuid(), person.Id, OwnerAccountId, "gone", "bob", AccountFeature.None, Now));
        await context.SaveChangesAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(context.AccountMembershipOffers);
        Assert.Single(context.Users);
    }

    /// <summary>Deleting an account leaves another tenants login and its session alone.</summary>
    /// <remarks>
    /// The inverse control for every assertion above. Each of them is satisfied by a handler that
    /// emptied the table, and an emptied <c>Sessions</c> table would sign every other tenant out of
    /// the panel while looking exactly like a correct cascade.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_leaves_another_tenants_login_and_its_session_alone()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var stranger = await context.Users.SingleAsync(user => user.Username == "stranger");
        Assert.Equal(StrangerAccountId, (await context.AccountMemberships.SingleAsync(membership => membership.UserId == stranger.Id)).AccountId);
        Assert.Single(await context.Sessions.Where(session => session.UserId == stranger.Id).ToListAsync());
        Assert.Single(await context.PasswordResetTokens.Where(token => token.UserId == stranger.Id).ToListAsync());
        Assert.Single(await context.RecoveryCodes.Where(code => code.UserId == stranger.Id).ToListAsync());
    }

    /// <summary>Deleting an account leaves the administrator and its session alone.</summary>
    /// <remarks>
    /// The administrator holds no membership at all, so it is in no way named by the deletion. A
    /// handler that treated "holds no membership" as "has nothing left to reach" would lock the
    /// operator out of the panel the first time they deleted a customer — the most expensive
    /// possible way to discover that mistake.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_leaves_the_administrator_and_its_session_alone()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var administrator = Assert.Single(await context.Users.Where(user => user.Role == UserRole.Admin).ToListAsync());
        Assert.Single(await context.Sessions.Where(session => session.UserId == administrator.Id).ToListAsync());
    }

    /// <summary>Deleting an account keeps the audit entries naming its logins.</summary>
    /// <remarks>
    /// The journal is append-only and is the operator's record of what the deleted logins did.
    /// Erasing it with the account would destroy exactly the evidence a deletion makes someone want
    /// to read, so this asserts the entries are STILL there — the one place in this cascade where
    /// survival is the correct outcome.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_keeps_the_audit_entries_naming_its_logins()
    {
        using var context = await SeedAsync();
        var owned = await OwnedUserIdsAsync(context);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Equal(2, await context.AuditEvents.CountAsync(entry => owned.Contains(entry.ActorUserId!.Value)));
    }

    /// <summary>Deleting an account with no login of its own changes nothing.</summary>
    /// <remarks>
    /// The v1 case, and the one every deletion takes today: no user row names an account, so the
    /// handler must leave the administrator's own session standing rather than treating an empty
    /// match as "delete what is here".
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_with_no_login_of_its_own_changes_nothing()
    {
        using var context = await SeedAsync();
        var before = await context.Users.CountAsync();

        await new AccountDeletingHandler(context).HandleAsync(
            Deleting(Guid.Parse("cccccccc-0000-4000-8000-000000000003")), CancellationToken.None);

        Assert.Equal(before, await context.Users.CountAsync());
        Assert.Equal(4, await context.Sessions.CountAsync());
    }

    /// <summary>Deleting an account keeps the failed sign-in counters keyed by address.</summary>
    /// <remarks>
    /// The counters are keyed by an IP ADDRESS and belong to no account, so clearing them because a
    /// tenant left would hand an attacker a way to reset their own lockout by getting an account
    /// deleted. This asserts the row is STILL there, and with the count it had.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_keeps_the_failed_sign_in_counters_keyed_by_address()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var counter = Assert.Single(await context.FailedLoginsByIp.ToListAsync());
        Assert.Equal(AttackerAddress, counter.IpAddress);
        Assert.Equal(1, counter.Failures);
    }

    /// <summary>Deleting an account keeps the server wide security policy.</summary>
    /// <remarks>
    /// One row keyed by a constant, owned by no account. Removing it with a tenant would reset the
    /// panel's password rules and lockout thresholds to nothing for everybody, which is why the
    /// assertion is on the VALUES rather than on the row's presence.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_keeps_the_server_wide_security_policy()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var policy = Assert.Single(await context.SecurityPolicies.ToListAsync());
        Assert.Equal(SecurityPolicy.SingletonId, policy.Id);
        Assert.Equal(PolicyMinimumPasswordLength, policy.MinimumPasswordLength);
        Assert.Equal(PolicyMaxFailedLoginAttempts, policy.MaxFailedLoginAttempts);
    }

    /// <summary>Delivering the same deletion twice leaves the second delivery nothing to do.</summary>
    /// <remarks>
    /// The bus may deliver a message more than once, and a second delivery arrives at a panel whose
    /// logins are already gone. It must neither throw — which would abort a deletion that had
    /// already succeeded — nor remove anything further, and the counts after it are asserted as
    /// values rather than as "unchanged", so a handler that emptied a table on the second pass
    /// cannot satisfy them.
    /// </remarks>
    [Fact]
    public async Task Delivering_the_same_deletion_twice_leaves_the_second_delivery_nothing_to_do()
    {
        using var context = await SeedAsync();
        var handler = new AccountDeletingHandler(context);
        await handler.HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        await handler.HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        // The stranger and the administrator, each with the one session, token and code they were
        // seeded with: what a panel holds after this account has left, twice over.
        Assert.Equal(2, await context.Users.CountAsync());
        Assert.Equal(2, await context.Sessions.CountAsync());
        Assert.Equal(2, await context.PasswordResetTokens.CountAsync());
        Assert.Equal(2, await context.RecoveryCodes.CountAsync());
        Assert.Equal(4, await context.AuditEvents.CountAsync());
    }

    /// <summary>A removal the database refuses is not swallowed.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AccountDeleting"/> is invoked inline by the Accounts module and a subscriber that
    /// throws ABORTS the deletion, leaving the account intact and deletable again. That makes
    /// propagation the whole of this handler's failure behaviour: a caught exception here would
    /// report a completed deletion over logins that are still in the table, which is the one
    /// outcome the cascade exists to prevent.
    /// </para>
    /// <para>
    /// UNOBSERVED HERE: whether the rows survive the refusal. These tests run on the EF Core
    /// InMemory provider, which has no transaction, so an assertion about rollback would be
    /// reporting on the provider and not on the single <c>SaveChangesAsync</c> that is the actual
    /// guarantee.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_removal_the_database_refuses_is_not_swallowed()
    {
        using var context = await SeedIntoAsync(RefusingIdentityTestContext.Create());

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);
        });

        Assert.Equal(RefusingSaveInterceptor.Message, refusal.Message);
    }

    /// <summary>Deleting an account removes every membership of it and no membership of another account.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_membership_of_it_and_no_other()
    {
        using var context = await SeedAsync();
        var member = new User(Guid.NewGuid(), "member", "member@example.com", "hash", UserRole.Customer, Now);
        context.Users.Add(member);
        context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), member.Id, OwnerAccountId, Now));
        context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), member.Id, StrangerAccountId, Now));
        await context.SaveChangesAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var remaining = await context.AccountMemberships.ToListAsync();
        Assert.All(remaining, membership => { Assert.Equal(StrangerAccountId, membership.AccountId); });
        Assert.Equal(2, remaining.Count);
        Assert.Contains(member.Id, await context.Users.Select(user => user.Id).ToListAsync());
    }

    /// <summary>
    /// A login that owns the deleted account and also holds another one survives whole, so deleting
    /// one account never deletes the other's way in.
    /// </summary>
    [Fact]
    public async Task A_login_that_holds_another_account_survives_with_its_credentials()
    {
        using var context = await SeedAsync();
        var both = await context.Users.FirstAsync(user => user.Username == "owner-one");
        context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), both.Id, StrangerAccountId, Now));
        await context.SaveChangesAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var survivor = await context.Users.SingleAsync(user => user.Id == both.Id);
        var kept = await context.AccountMemberships.Where(membership => membership.UserId == survivor.Id).ToListAsync();
        Assert.Equal(StrangerAccountId, Assert.Single(kept).AccountId);
        Assert.NotEmpty(await context.Sessions.Where(session => session.UserId == both.Id).ToListAsync());
        Assert.Empty(await context.AccountMemberships.Where(membership => membership.AccountId == OwnerAccountId).ToListAsync());
    }

    /// <summary>
    /// A member who had only the deleted account is deleted with it: a customer login that reaches no
    /// account is a way in to nothing.
    /// </summary>
    [Fact]
    public async Task A_member_who_had_only_the_deleted_account_is_deleted_with_it()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.DoesNotContain("owner-two", await context.Users.Select(user => user.Username).ToListAsync());
    }

    /// <summary>The identities of the logins the deleted account owns, read before the handler runs.</summary>
    /// <param name="context">The seeded context.</param>
    /// <returns>The two owned user ids.</returns>
    private static async Task<List<Guid>> OwnedUserIdsAsync(IdentityDbContext context)
    {
        return await context.AccountMemberships
            .Where(membership => membership.AccountId == OwnerAccountId)
            .Select(membership => membership.UserId)
            .ToListAsync();
    }

    /// <summary>The event the Accounts module invokes for an account.</summary>
    /// <param name="accountId">The account about to be deleted.</param>
    /// <returns>The event.</returns>
    private static AccountDeleting Deleting(Guid accountId)
    {
        return new AccountDeleting(accountId, "owner");
    }

    /// <summary>
    /// Seeds two logins for the account under test, one for a second tenant and one administrator,
    /// each with a session, a reset token, a recovery code and an audit entry, plus the two
    /// server-wide rows this module holds that belong to no account.
    /// </summary>
    /// <returns>The seeded context, which the caller disposes.</returns>
    private static async Task<IdentityDbContext> SeedAsync()
    {
        return await SeedIntoAsync(IdentityTestContext.Create());
    }

    /// <summary>Seeds that same panel into a context the caller chose.</summary>
    /// <param name="context">The context to seed, which the caller disposes.</param>
    /// <returns>The same context, seeded.</returns>
    /// <remarks>
    /// Split from <see cref="SeedAsync"/> so the failure test can seed a context that refuses
    /// removals: it must hold exactly the panel every other test holds, or its refusal would be
    /// about a different account.
    /// </remarks>
    private static async Task<IdentityDbContext> SeedIntoAsync(IdentityDbContext context)
    {
        await AddLoginAsync(context, "owner-one", OwnerAccountId, MembershipRole.Owner);
        await AddLoginAsync(context, "owner-two", OwnerAccountId, MembershipRole.Member);
        await AddLoginAsync(context, "stranger", StrangerAccountId, MembershipRole.Owner);
        await AddLoginAsync(context, "administrator", accountId: null, MembershipRole.Owner);

        // Neither of these names an account or a user, and both must survive the cascade.
        context.FailedLoginsByIp.Add(new FailedLoginByIp(AttackerAddress, Now));
        context.SecurityPolicies.Add(new SecurityPolicy(
            PolicyMinimumPasswordLength,
            SecurityPolicy.DefaultForceTwoFactorForAdmins,
            SecurityPolicy.DefaultForceTwoFactorForResellers,
            PolicyMaxFailedLoginAttempts,
            SecurityPolicy.DefaultLockoutMinutes,
            Now));
        await context.SaveChangesAsync();

        return context;
    }

    /// <summary>Adds one login with the full set of rows that hang off it.</summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="name">The login name, used for its address too.</param>
    /// <param name="accountId">The account the login acts for, or <c>null</c> for the administrator.</param>
    /// <param name="role">Whether the login owns the account or was added to it; ignored without an account.</param>
    private static async Task AddLoginAsync(IdentityDbContext context, string name, Guid? accountId, MembershipRole role)
    {
        // The role follows the shape of the login: a login owning an account is a customer, and
        // the one owning none is the administrator. It is what the panel would really write, and the
        // administrator's row is the one that must survive.
        var userRole = accountId is null ? UserRole.Admin : UserRole.Customer;
        var user = new User(Guid.NewGuid(), name, $"{name}@example.com", "hash", userRole, Now);
        context.Users.Add(user);
        if (accountId is { } actsFor)
        {
            context.AccountMemberships.Add(role == MembershipRole.Owner
                ? AccountMembership.Own(Guid.NewGuid(), user.Id, actsFor, Now)
                : AccountMembership.Admit(Guid.NewGuid(), user.Id, actsFor, AccountFeature.None, Now));
        }

        context.Sessions.Add(new Session(
            Guid.NewGuid(), user.Id, Guid.NewGuid(), $"{name}-token", Now, Now.AddDays(30), "203.0.113.10", "tests"));
        context.PasswordResetTokens.Add(new PasswordResetToken(Guid.NewGuid(), user.Id, $"{name}-reset", Now));
        context.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), user.Id, $"{name}-code"));
        context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            Now,
            user.Id,
            name,
            IdentityAuditActions.LoginSucceeded,
            name,
            "203.0.113.10",
            "tests",
            succeeded: true,
            correlationId: null));

        await context.SaveChangesAsync();
    }
}
