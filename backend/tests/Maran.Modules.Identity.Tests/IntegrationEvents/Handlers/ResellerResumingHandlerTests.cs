using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that lets a reactivated reseller's login sign in again
/// (issue #179): it lifts the login's suspension and nothing else.
/// </summary>
public sealed class ResellerResumingHandlerTests : IDisposable
{
    private static readonly Guid ResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    private static readonly Guid OtherResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static ResellerResuming Resuming(Guid? resellerId = null)
    {
        return new ResellerResuming(resellerId ?? ResellerId);
    }

    /// <summary>Stores a reseller login, claimed or not, suspended or not.</summary>
    /// <param name="resellerId">The reseller the login belongs to.</param>
    /// <param name="name">The login name.</param>
    /// <param name="claimed">Whether the login has set its password.</param>
    /// <param name="suspended">Whether the login is suspended.</param>
    /// <returns>The login.</returns>
    private async Task<User> SeedAsync(Guid resellerId, string name, bool claimed, bool suspended)
    {
        var login = User.InviteReseller(Guid.NewGuid(), name, $"{name}@example.com", resellerId, Now);
        if (claimed)
        {
            login.Activate("hash");
        }

        if (suspended)
        {
            login.Suspend();
        }

        _context.Users.Add(login);
        await _context.SaveChangesAsync();
        return login;
    }

    /// <summary>Resuming a suspended reseller lets its login authenticate again.</summary>
    [Fact]
    public async Task Resuming_a_suspended_reseller_lets_its_login_authenticate_again()
    {
        var login = await SeedAsync(ResellerId, "northwind", claimed: true, suspended: true);

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == login.Id);
        Assert.False(stored.IsSuspended);
        Assert.True(stored.MayAuthenticate);
    }

    /// <summary>The sessions the suspension ended stay ended: resuming restores sign-in, not sessions.</summary>
    [Fact]
    public async Task The_sessions_the_suspension_ended_stay_ended()
    {
        var login = await SeedAsync(ResellerId, "northwind", claimed: true, suspended: true);
        var session = new Session(Guid.NewGuid(), login.Id, Guid.NewGuid(), "token", Now, Now.AddDays(14), "203.0.113.7", "browser");
        session.Revoke(Now, SessionRevocationReason.ResellerSuspended);
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        var stored = await _context.Sessions.SingleAsync(candidate => candidate.Id == session.Id);
        Assert.False(stored.IsActive(Now));
        Assert.Equal(SessionRevocationReason.ResellerSuspended, stored.Revocation?.Reason);
    }

    /// <summary>A reseller still waiting for its invitation stays waiting: resuming does not claim the login.</summary>
    [Fact]
    public async Task Resuming_leaves_a_reseller_that_never_accepted_invited()
    {
        await SeedAsync(ResellerId, "northwind", claimed: false, suspended: true);

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync();
        Assert.False(stored.IsSuspended);
        Assert.True(stored.HasPendingInvitation);
        Assert.False(stored.MayAuthenticate);
    }

    /// <summary>Resuming a login that was never suspended, or twice, changes nothing.</summary>
    [Fact]
    public async Task Resuming_twice_or_a_login_that_was_never_suspended_changes_nothing()
    {
        var login = await SeedAsync(ResellerId, "northwind", claimed: true, suspended: false);
        var handler = new ResellerResumingHandler(_context);

        await handler.HandleAsync(Resuming(), CancellationToken.None);
        await handler.HandleAsync(Resuming(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == login.Id);
        Assert.False(stored.IsSuspended);
        Assert.True(stored.MayAuthenticate);
    }

    /// <summary>Another reseller's suspended login stays suspended.</summary>
    [Fact]
    public async Task Another_resellers_suspension_is_left_alone()
    {
        await SeedAsync(ResellerId, "northwind", claimed: true, suspended: true);
        var other = await SeedAsync(OtherResellerId, "contoso", claimed: true, suspended: true);

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        Assert.True((await _context.Users.SingleAsync(candidate => candidate.Id == other.Id)).IsSuspended);
    }

    /// <summary>A reseller with no login has nothing to resume, and the event is not an error.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_is_left_with_nothing_to_do()
    {
        var other = await SeedAsync(OtherResellerId, "contoso", claimed: true, suspended: true);

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        Assert.True((await _context.Users.SingleAsync(candidate => candidate.Id == other.Id)).IsSuspended);
    }

    /// <summary>Resuming touches no customer: an unrelated customer login keeps its state exactly.</summary>
    [Fact]
    public async Task Resuming_touches_no_customer_login()
    {
        await SeedAsync(ResellerId, "northwind", claimed: true, suspended: true);
        var customer = new User(Guid.NewGuid(), "customer-one", "customer@example.com", "hash", UserRole.Customer, Now);
        customer.Activate("hash");
        customer.SyncLegacyColumns(MembershipState.Suspended);
        _context.Users.Add(customer);
        await _context.SaveChangesAsync();

        await new ResellerResumingHandler(_context).HandleAsync(Resuming(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == customer.Id);
        Assert.Equal((UserState.Suspended, true), (stored.LegacyState, stored.LegacyAccountSuspended));
    }
}
