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
/// Behavioural contract of the handler that removes a deleted reseller's login and every credential
/// hanging off it (issue #179): a way in is released, nothing else is.
/// </summary>
public sealed class ResellerDeletingHandlerTests : IDisposable
{
    private static readonly Guid ResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    private static readonly Guid OtherResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static ResellerDeleting Deleting(Guid? resellerId = null)
    {
        return new ResellerDeleting(resellerId ?? ResellerId);
    }

    /// <summary>Adds a login with the full set of credential rows that hang off it.</summary>
    /// <param name="user">The login.</param>
    private void AddCredentials(User user)
    {
        _context.Users.Add(user);
        _context.Sessions.Add(new Session(
            Guid.NewGuid(), user.Id, Guid.NewGuid(), $"{user.Username}-token", Now, Now.AddDays(30), "203.0.113.10", "tests"));
        _context.PasswordResetTokens.Add(new PasswordResetToken(Guid.NewGuid(), user.Id, $"{user.Username}-reset", Now));
        _context.InvitationTokens.Add(new InvitationToken(Guid.NewGuid(), user.Id, $"{user.Username}-invite", Now));
        _context.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), user.Id, $"{user.Username}-code"));
    }

    private static User NewReseller(Guid resellerId, string name)
    {
        var login = User.InviteReseller(Guid.NewGuid(), name, $"{name}@example.com", resellerId, Now);
        login.Activate("hash");
        return login;
    }

    /// <summary>Deleting a reseller removes its login and every credential keyed by it.</summary>
    [Fact]
    public async Task Deleting_a_reseller_removes_its_login_and_every_credential()
    {
        AddCredentials(NewReseller(ResellerId, "northwind"));
        await _context.SaveChangesAsync();

        await new ResellerDeletingHandler(_context).HandleAsync(Deleting(), CancellationToken.None);

        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(await _context.Sessions.ToListAsync());
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
        Assert.Empty(await _context.InvitationTokens.ToListAsync());
        Assert.Empty(await _context.RecoveryCodes.ToListAsync());
    }

    /// <summary>A view-as session an administrator opened on the reseller's login goes with it.</summary>
    [Fact]
    public async Task A_view_as_session_opened_on_the_resellers_login_is_removed_with_it()
    {
        var login = NewReseller(ResellerId, "northwind");
        AddCredentials(login);
        _context.Sessions.Add(Session.StartViewAs(
            Guid.NewGuid(), login.Id, null, Guid.NewGuid(), Guid.NewGuid(), "view-as-token", Now, "203.0.113.1", "admin-agent"));
        await _context.SaveChangesAsync();

        await new ResellerDeletingHandler(_context).HandleAsync(Deleting(), CancellationToken.None);

        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>
    /// Another reseller's login and credentials, a customer's login, membership and journal, and the
    /// policy row all survive: the accepting control for every removal above.
    /// </summary>
    [Fact]
    public async Task Everything_that_is_not_the_deleted_resellers_survives()
    {
        AddCredentials(NewReseller(ResellerId, "northwind"));
        var other = NewReseller(OtherResellerId, "contoso");
        AddCredentials(other);
        var customer = new User(Guid.NewGuid(), "customer-one", "customer@example.com", "hash", UserRole.Customer, Now);
        customer.Activate("hash");
        AddCredentials(customer);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, AccountId, Now));
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), Now, null, "northwind", IdentityAuditActions.ResellerInvited, "northwind", "203.0.113.10", "tests", succeeded: true, correlationId: null));
        _context.SecurityPolicies.Add(new SecurityPolicy(12, false, true, 10, 15, Now));
        await _context.SaveChangesAsync();

        await new ResellerDeletingHandler(_context).HandleAsync(Deleting(), CancellationToken.None);

        Assert.Equal(
            new[] { "contoso", "customer-one" },
            (await _context.Users.Select(candidate => candidate.Username).ToListAsync()).Order().ToArray());
        Assert.Equal(2, await _context.Sessions.CountAsync());
        Assert.Equal(2, await _context.PasswordResetTokens.CountAsync());
        Assert.Equal(2, await _context.InvitationTokens.CountAsync());
        Assert.Equal(2, await _context.RecoveryCodes.CountAsync());
        Assert.Single(await _context.AccountMemberships.ToListAsync());
        Assert.Single(await _context.AuditEvents.ToListAsync());
        Assert.Single(await _context.SecurityPolicies.ToListAsync());
    }

    /// <summary>Repeating the event, or deleting a reseller with no login, is not an error.</summary>
    [Fact]
    public async Task Repeating_the_event_or_deleting_a_reseller_with_no_login_is_harmless()
    {
        AddCredentials(NewReseller(ResellerId, "northwind"));
        await _context.SaveChangesAsync();
        var handler = new ResellerDeletingHandler(_context);

        await handler.HandleAsync(Deleting(), CancellationToken.None);
        await handler.HandleAsync(Deleting(), CancellationToken.None);
        await handler.HandleAsync(Deleting(OtherResellerId), CancellationToken.None);

        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>A database that refuses the removal fails the handler, so the Accounts module abandons the deletion.</summary>
    /// <remarks>
    /// UNOBSERVED HERE: whether the rows survive the refusal. The in-memory provider has no
    /// transaction, so an assertion about rollback would report on the provider and not on the single
    /// <c>SaveChangesAsync</c> that is the real guarantee; only propagation is asserted.
    /// </remarks>
    [Fact]
    public async Task A_removal_the_database_refuses_is_not_swallowed()
    {
        using var refusing = RefusingIdentityTestContext.Create();
        var login = NewReseller(ResellerId, "northwind");
        refusing.Users.Add(login);
        await refusing.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new ResellerDeletingHandler(refusing).HandleAsync(Deleting(), CancellationToken.None);
        });

        Assert.Equal(RefusingSaveInterceptor.Message, exception.Message);
    }
}
