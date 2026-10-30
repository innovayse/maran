using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetProfile;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Queries.GetProfile;

/// <summary>Behavioural contract of the get-profile query handler.</summary>
public sealed class GetProfileQueryHandlerTests : IDisposable
{
    /// <summary>The fixed instant every test runs at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory Identity database.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The controllable clock.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A profile reports who the user is, for an administrator who owns no account.</summary>
    [Fact]
    public async Task A_profile_reports_who_an_administrator_is()
    {
        var admin = await AddUserAsync("root", UserRole.Admin);

        var result = await NewHandler().HandleAsync(new GetProfileQuery(admin.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("root", result.Value.Username);
        Assert.Equal("root@example.com", result.Value.Email);
        Assert.Equal(UserRole.Admin, result.Value.Role);
        Assert.False(result.Value.IsTwoFactorEnabled);
    }

    /// <summary>A profile reports an enrolled second factor.</summary>
    [Fact]
    public async Task A_profile_reports_an_enrolled_second_factor()
    {
        var user = await AddUserAsync("alice", UserRole.Customer);
        user.EnableTotp("JBSWY3DPEHPK3PXP");
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(new GetProfileQuery(user.Id), CancellationToken.None);

        Assert.True(result.Value.IsTwoFactorEnabled);
        Assert.Equal(UserRole.Customer, result.Value.Role);
    }

    /// <summary>The session count covers only the user's own live sessions.</summary>
    [Fact]
    public async Task The_session_count_covers_only_the_users_own_live_sessions()
    {
        var user = await AddUserAsync("alice", UserRole.Customer);
        var stranger = await AddUserAsync("bob", UserRole.Customer);
        var sessions = NewSessionService();
        await sessions.IssueAsync(user.Id, "203.0.113.7", "laptop", CancellationToken.None);
        var revoked = await sessions.IssueAsync(user.Id, "203.0.113.8", "phone", CancellationToken.None);
        await sessions.RevokeAsync(revoked.SessionId, SessionRevocationReason.Logout, CancellationToken.None);
        await sessions.IssueAsync(stranger.Id, "198.51.100.4", "other", CancellationToken.None);

        var result = await NewHandler().HandleAsync(new GetProfileQuery(user.Id), CancellationToken.None);

        Assert.Equal(1, result.Value.ActiveSessionCount);
    }

    /// <summary>An expired session is not counted.</summary>
    [Fact]
    public async Task An_expired_session_is_not_counted()
    {
        var user = await AddUserAsync("alice", UserRole.Customer);
        await NewSessionService().IssueAsync(user.Id, "203.0.113.7", "laptop", CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(15));

        var result = await NewHandler().HandleAsync(new GetProfileQuery(user.Id), CancellationToken.None);

        Assert.Equal(0, result.Value.ActiveSessionCount);
    }

    /// <summary>A user that no longer exists is reported as not found.</summary>
    [Fact]
    public async Task A_user_that_no_longer_exists_is_reported_as_not_found()
    {
        var result = await NewHandler().HandleAsync(new GetProfileQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("UserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Stores a user with a predictable address.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="role">The role.</param>
    /// <returns>The stored user.</returns>
    private async Task<User> AddUserAsync(string username, UserRole role)
    {
        var user = new User(Guid.NewGuid(), username, $"{username}@example.com", "hash", role, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Builds a session service over the fixture's database.</summary>
    /// <returns>The service.</returns>
    private SessionService NewSessionService()
    {
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    /// <summary>Builds the handler under test.</summary>
    /// <returns>The handler.</returns>
    private GetProfileQueryHandler NewHandler()
    {
        return new GetProfileQueryHandler(_context, _clock);
    }
}
