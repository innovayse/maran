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

    /// <summary>Deleting an account removes every login it owns.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_login_it_owns()
    {
        // Two logins, because a handler that removed only the first would pass a single-row test
        // and leave a working password against a tenant that no longer exists.
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.Users.Where(user => user.AccountId == OwnerAccountId).ToListAsync());
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

        var stranger = Assert.Single(await context.Users.Where(user => user.AccountId == StrangerAccountId).ToListAsync());
        Assert.Single(await context.Sessions.Where(session => session.UserId == stranger.Id).ToListAsync());
        Assert.Single(await context.PasswordResetTokens.Where(token => token.UserId == stranger.Id).ToListAsync());
        Assert.Single(await context.RecoveryCodes.Where(code => code.UserId == stranger.Id).ToListAsync());
    }

    /// <summary>Deleting an account leaves the administrator and its session alone.</summary>
    /// <remarks>
    /// The administrator's <c>AccountId</c> is null, which in a nullable comparison is neither equal
    /// nor unequal to the account being deleted. A handler that got that wrong would lock the
    /// operator out of the panel the first time they deleted a customer — the most expensive
    /// possible way to discover a null-handling mistake.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_leaves_the_administrator_and_its_session_alone()
    {
        using var context = await SeedAsync();

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        var administrator = Assert.Single(await context.Users.Where(user => user.AccountId == null).ToListAsync());
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

    /// <summary>The identities of the logins the deleted account owns, read before the handler runs.</summary>
    /// <param name="context">The seeded context.</param>
    /// <returns>The two owned user ids.</returns>
    private static async Task<List<Guid>> OwnedUserIdsAsync(IdentityDbContext context)
    {
        return await context.Users
            .Where(user => user.AccountId == OwnerAccountId)
            .Select(user => user.Id)
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
    /// each with a session, a reset token, a recovery code and an audit entry.
    /// </summary>
    /// <returns>The seeded context, which the caller disposes.</returns>
    private static async Task<IdentityDbContext> SeedAsync()
    {
        var context = IdentityTestContext.Create();

        await AddLoginAsync(context, "owner-one", OwnerAccountId);
        await AddLoginAsync(context, "owner-two", OwnerAccountId);
        await AddLoginAsync(context, "stranger", StrangerAccountId);
        await AddLoginAsync(context, "administrator", accountId: null);

        return context;
    }

    /// <summary>Adds one login with the full set of rows that hang off it.</summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="name">The login name, used for its address too.</param>
    /// <param name="accountId">The account the login owns, or <c>null</c> for the administrator.</param>
    private static async Task AddLoginAsync(IdentityDbContext context, string name, Guid? accountId)
    {
        // The role follows the shape of the login: a login owning an account is a customer, and
        // the one owning none is the administrator. It is what the panel would really write, and the
        // administrator's row is the one that must survive.
        var role = accountId is null ? UserRole.Admin : UserRole.Customer;
        var user = new User(Guid.NewGuid(), name, $"{name}@example.com", "hash", role, Now);
        if (accountId is not null)
        {
            user.AssignAccount(accountId.Value);
        }

        context.Users.Add(user);
        context.Sessions.Add(new Session(
            Guid.NewGuid(), user.Id, Guid.NewGuid(), $"{name}-token", Now, Now.AddDays(30), "203.0.113.10", "tests"));
        context.PasswordResetTokens.Add(new PasswordResetToken(Guid.NewGuid(), user.Id, $"{name}-reset", Now));
        context.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), user.Id, $"{name}-code"));
        context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            Now,
            user.Id,
            name,
            AuditActions.LoginSucceeded,
            name,
            "203.0.113.10",
            "tests",
            succeeded: true,
            correlationId: null));

        await context.SaveChangesAsync();
    }
}
