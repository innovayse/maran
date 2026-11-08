using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetResellerInvitationStatus;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Tests.Queries.GetResellerInvitationStatus;

/// <summary>
/// Behavioural contract of <see cref="GetResellerInvitationStatusQueryHandler"/>: where a reseller's
/// invitation stands, computed from the stored rows at the moment of the request.
/// </summary>
public sealed class GetResellerInvitationStatusQueryHandlerTests : IDisposable
{
    /// <summary>The reseller whose login is invited.</summary>
    private static readonly Guid ResellerId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>The fixed instant the clock starts at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The clock.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Asks the handler about the reseller under test.</summary>
    private async Task<InvitationStatusDto> AskAsync()
    {
        var handler = new GetResellerInvitationStatusQueryHandler(
            _context, new InvitationStatusReader(_context, _clock, IdentityTestContext.DisplayNameResources()));
        var result = await handler.HandleAsync(new GetResellerInvitationStatusQuery(ResellerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Stores the reseller's invited login, optionally with a token issued at <paramref name="tokenCreatedAt"/>.</summary>
    /// <param name="tokenCreatedAt">When its token was issued, or <c>null</c> for none.</param>
    private async Task<User> SeedLoginAsync(DateTimeOffset? tokenCreatedAt)
    {
        var user = User.InviteReseller(Guid.NewGuid(), "reseller-one", "reseller@example.com", ResellerId, Now);
        _context.Users.Add(user);
        if (tokenCreatedAt is not null)
        {
            _context.InvitationTokens.Add(
                new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash("t"), tokenCreatedAt.Value));
        }

        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>A reseller with no login yet reports no invitation.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_reports_no_invitation()
    {
        Assert.IsType<NoInvitationStatusDto>(await AskAsync());
    }

    /// <summary>An unused, unexpired token reports pending with its expiry.</summary>
    [Fact]
    public async Task An_unused_unexpired_token_reports_pending_with_its_expiry()
    {
        await SeedLoginAsync(Now);

        var pending = Assert.IsType<PendingInvitationStatusDto>(await AskAsync());

        Assert.Equal(Now + InvitationToken.Lifetime, pending.ExpiresAt);
    }

    /// <summary>A token past its lifetime reports expired.</summary>
    [Fact]
    public async Task A_lapsed_token_reports_expired()
    {
        await SeedLoginAsync(Now - InvitationToken.Lifetime - TimeSpan.FromMinutes(1));

        Assert.IsType<ExpiredInvitationStatusDto>(await AskAsync());
    }

    /// <summary>A login that has a password reports accepted, so no link is offered.</summary>
    [Fact]
    public async Task A_login_with_a_password_reports_accepted()
    {
        var login = await SeedLoginAsync(Now);
        login.Activate("hash");
        await _context.SaveChangesAsync();

        Assert.IsType<AcceptedInvitationStatusDto>(await AskAsync());
    }
}
