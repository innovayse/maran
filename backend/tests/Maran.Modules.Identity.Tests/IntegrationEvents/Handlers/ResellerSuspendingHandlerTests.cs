using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that suspends a reseller's login and ends its sessions
/// (issue #179, decision D2): the login and its sessions only, never a hosting account.
/// </summary>
public sealed class ResellerSuspendingHandlerTests : IDisposable
{
    private static readonly Guid ResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    private static readonly Guid OtherResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static ResellerSuspending Suspending(Guid? resellerId = null)
    {
        return new ResellerSuspending(resellerId ?? ResellerId);
    }

    private ResellerSuspendingHandler NewHandler()
    {
        return new ResellerSuspendingHandler(
            _context,
            new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 })),
            ErasureHandlers.KeyRevoker(_context));
    }

    /// <summary>Stores a claimed reseller login with one live own session.</summary>
    /// <param name="resellerId">The reseller the login belongs to.</param>
    /// <param name="name">The login name.</param>
    /// <returns>The login and its session.</returns>
    private async Task<(User Login, Session Session)> SeedResellerAsync(Guid resellerId, string name)
    {
        var login = User.InviteReseller(Guid.NewGuid(), name, $"{name}@example.com", resellerId, Now);
        login.Activate("hash");
        var session = new Session(Guid.NewGuid(), login.Id, Guid.NewGuid(), $"{name}-token", Now, Now.AddDays(14), "203.0.113.7", "browser");
        _context.Users.Add(login);
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();
        return (login, session);
    }

    /// <summary>Stores a customer owning <see cref="AccountId"/>, with one live session acting for it.</summary>
    /// <returns>The customer login and its session.</returns>
    private async Task<(User Login, Session Session)> SeedCustomerAsync()
    {
        var login = new User(Guid.NewGuid(), "customer-one", "customer@example.com", "hash", UserRole.Customer, Now);
        login.Activate("hash");
        var session = new Session(Guid.NewGuid(), login.Id, Guid.NewGuid(), "customer-token", Now, Now.AddDays(14), "203.0.113.9", "browser", AccountId);
        _context.Users.Add(login);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), login.Id, AccountId, Now));
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();
        return (login, session);
    }

    /// <summary>Suspending a reseller suspends its login so it can no longer authenticate.</summary>
    [Fact]
    public async Task Suspending_a_reseller_suspends_its_login()
    {
        var (login, _) = await SeedResellerAsync(ResellerId, "northwind");

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == login.Id);
        Assert.True(stored.IsSuspended);
        Assert.False(stored.MayAuthenticate);
    }

    /// <summary>
    /// Suspension revokes the live sessions of an already-signed-in reseller, not merely the next
    /// sign-in, and names the reseller as the reason.
    /// </summary>
    /// <remarks>
    /// Removing the revocation call must turn this red: the login flag and the sessions are separate
    /// rows, and a token issued before the suspension would otherwise authenticate until it expires.
    /// </remarks>
    [Fact]
    public async Task Suspension_revokes_the_live_sessions_of_the_reseller_with_the_reseller_reason()
    {
        var (_, session) = await SeedResellerAsync(ResellerId, "northwind");

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var stored = await _context.Sessions.SingleAsync(candidate => candidate.Id == session.Id);
        Assert.False(stored.IsActive(Now));
        Assert.Equal(SessionRevocationReason.ResellerSuspended, stored.Revocation?.Reason);
    }

    /// <summary>A view-as session an administrator opened on the reseller's login ends with it.</summary>
    [Fact]
    public async Task Suspension_ends_a_view_as_session_opened_on_the_resellers_login()
    {
        var (login, _) = await SeedResellerAsync(ResellerId, "northwind");
        var viewAs = Session.StartViewAs(
            Guid.NewGuid(), login.Id, null, Guid.NewGuid(), Guid.NewGuid(), "view-as-token", Now, "203.0.113.1", "admin-agent");
        _context.Sessions.Add(viewAs);
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.False((await _context.Sessions.SingleAsync(candidate => candidate.Id == viewAs.Id)).IsActive(Now));
    }

    /// <summary>
    /// Suspending a reseller touches no hosting account: the customer's login, membership and live
    /// session are exactly as they were.
    /// </summary>
    /// <remarks>
    /// The accepting control is in the same test: the reseller's own session did end, so the handler
    /// demonstrably ran to completion and the customer's row was left alone by choice.
    /// </remarks>
    [Fact]
    public async Task Suspending_a_reseller_touches_no_account_login_membership_or_session()
    {
        var (_, resellerSession) = await SeedResellerAsync(ResellerId, "northwind");
        var (customer, customerSession) = await SeedCustomerAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.False((await _context.Sessions.SingleAsync(candidate => candidate.Id == resellerSession.Id)).IsActive(Now));
        var storedCustomer = await _context.Users.SingleAsync(candidate => candidate.Id == customer.Id);
        Assert.True(storedCustomer.MayAuthenticate);
        Assert.False(storedCustomer.IsSuspended);
        Assert.Equal((UserState.Active, false), (storedCustomer.LegacyState, storedCustomer.LegacyAccountSuspended));
        Assert.Equal(MembershipState.Active, (await _context.AccountMemberships.SingleAsync()).State);
        Assert.True((await _context.Sessions.SingleAsync(candidate => candidate.Id == customerSession.Id)).IsActive(Now));
    }

    /// <summary>Another reseller's login and session are left alone.</summary>
    [Fact]
    public async Task Another_resellers_login_and_session_are_left_alone()
    {
        await SeedResellerAsync(ResellerId, "northwind");
        var (other, otherSession) = await SeedResellerAsync(OtherResellerId, "contoso");

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.False((await _context.Users.SingleAsync(candidate => candidate.Id == other.Id)).IsSuspended);
        Assert.True((await _context.Sessions.SingleAsync(candidate => candidate.Id == otherSession.Id)).IsActive(Now));
    }

    /// <summary>Repeating the event changes nothing and keeps the first revocation reason.</summary>
    [Fact]
    public async Task Repeating_the_event_changes_nothing_and_keeps_the_first_revocation()
    {
        var (login, session) = await SeedResellerAsync(ResellerId, "northwind");
        var handler = NewHandler();
        await handler.HandleAsync(Suspending(), CancellationToken.None);
        var firstRevokedAt = (await _context.Sessions.SingleAsync(candidate => candidate.Id == session.Id)).Revocation?.At;
        _clock.Advance(TimeSpan.FromMinutes(5));

        await handler.HandleAsync(Suspending(), CancellationToken.None);

        Assert.True((await _context.Users.SingleAsync(candidate => candidate.Id == login.Id)).IsSuspended);
        var stored = await _context.Sessions.SingleAsync(candidate => candidate.Id == session.Id);
        Assert.Equal(firstRevokedAt, stored.Revocation?.At);
        Assert.Equal(SessionRevocationReason.ResellerSuspended, stored.Revocation?.Reason);
    }

    /// <summary>A reseller with no login has nothing to close, and the event is not an error.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_is_left_with_nothing_to_do()
    {
        var (customer, _) = await SeedCustomerAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.True((await _context.Users.SingleAsync(candidate => candidate.Id == customer.Id)).MayAuthenticate);
    }

    /// <summary>A reseller that never accepted its invitation is suspended and still invited.</summary>
    [Fact]
    public async Task Suspension_leaves_an_invited_reseller_invited()
    {
        var login = User.InviteReseller(Guid.NewGuid(), "northwind", "northwind@example.com", ResellerId, Now);
        _context.Users.Add(login);
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync();
        Assert.True(stored.IsSuspended);
        Assert.True(stored.HasPendingInvitation);
    }

    /// <summary>
    /// Suspension revokes the keys the reseller's login created, journals each with the reason, and a
    /// resume does not revive them (issue #551); another login's key is left active.
    /// </summary>
    /// <remarks>The other reseller's key is the accepting control: the handler ran and revoked by creator only.</remarks>
    [Fact]
    public async Task Suspension_revokes_the_resellers_keys_and_resuming_does_not_revive_them()
    {
        var (login, _) = await SeedResellerAsync(ResellerId, "northwind");
        var (other, _) = await SeedResellerAsync(OtherResellerId, "contoso");
        var (key, _) = await ApiKeySeed.AddAsync(_context, Now, name: "mine", resellerId: ResellerId, createdBy: login.Id);
        var (otherKey, _) = await ApiKeySeed.AddAsync(_context, Now, name: "theirs", resellerId: OtherResellerId, createdBy: other.Id);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.NotNull((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).RevokedAt);
        Assert.Null((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == otherKey.Id)).RevokedAt);
        var entry = await _context.AuditEvents.AsNoTracking().SingleAsync(written => written.Action == IdentityAuditActions.ApiKeyRevoked);
        Assert.Equal($"{key.Prefix} mine [self.read] reason=CreatorDisabled", entry.Subject);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == login.Id);
        stored.Resume();
        await _context.SaveChangesAsync();
        Assert.NotNull((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).RevokedAt);
    }
}
