using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of session service.</summary>

public sealed class SessionServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public async Task InitializeAsync()
    {
        _context.Users.Add(new User(_userId, "admin", "admin@example.com", "hash", UserRole.Admin, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private SessionService NewService()
    {
        return new SessionService(_context, _clock, Options.Create(new JwtOptions { RefreshTokenDays = 14 }));
    }

    /// <summary>Rotating a refresh token revokes the old session and issues a new one.</summary>
    [Fact]
    public async Task Rotating_a_refresh_token_revokes_the_old_session_and_issues_a_new_one()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var rotated = await service.RotateAsync(issued.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        Assert.True(rotated.IsSuccess);
        Assert.NotEqual(issued.RefreshToken, rotated.Value.RefreshToken);
        var old = await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId);
        Assert.Equal(SessionRevocationReason.Rotated, old.RevocationReason);
    }

    /// <summary>A rotated session keeps the family of the one it replaces.</summary>
    [Fact]
    public async Task A_rotated_session_keeps_the_family_of_the_one_it_replaces()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var rotated = await service.RotateAsync(issued.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        var first = await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId);
        var second = await _context.Sessions.SingleAsync(s => s.Id == rotated.Value.SessionId);
        Assert.Equal(first.FamilyId, second.FamilyId);
    }

    /// <summary>Presenting an already rotated refresh token revokes the whole family.</summary>
    [Fact]
    public async Task Presenting_an_already_rotated_refresh_token_revokes_the_whole_family()
    {
        var service = NewService();
        var first = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        var second = await service.RotateAsync(first.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        var replay = await service.RotateAsync(first.RefreshToken, "198.51.100.4", "thief", CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Equal("RefreshTokenReusedUnauthorized", replay.Error!.Code);
        var live = await _context.Sessions.SingleAsync(s => s.Id == second.Value.SessionId);
        Assert.Equal(SessionRevocationReason.ReuseDetected, live.RevocationReason);
    }

    /// <summary>An unknown refresh token is rejected without revoking anything.</summary>
    [Fact]
    public async Task An_unknown_refresh_token_is_rejected_without_revoking_anything()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var result = await service.RotateAsync("a-token-nobody-issued", "203.0.113.7", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RefreshTokenInvalidUnauthorized", result.Error!.Code);
        Assert.True((await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId)).IsActive(Now));
    }

    /// <summary>An expired refresh token is rejected.</summary>
    [Fact]
    public async Task An_expired_refresh_token_is_rejected()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(15));

        var result = await service.RotateAsync(issued.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RefreshTokenInvalidUnauthorized", result.Error!.Code);
    }

    /// <summary>The database never holds the plaintext refresh token.</summary>
    [Fact]
    public async Task The_database_never_holds_the_plaintext_refresh_token()
    {
        var service = NewService();

        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var stored = await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId);
        Assert.NotEqual(issued.RefreshToken, stored.TokenHash);
        Assert.DoesNotContain(issued.RefreshToken, stored.TokenHash, StringComparison.Ordinal);
    }

    /// <summary>Two issued sessions never share a refresh token.</summary>
    [Fact]
    public async Task Two_issued_sessions_never_share_a_refresh_token()
    {
        var service = NewService();

        var first = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        var second = await service.IssueAsync(_userId, "198.51.100.4", "other", CancellationToken.None);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    /// <summary>Revoking one session leaves the users other sessions alone.</summary>
    [Fact]
    public async Task Revoking_one_session_leaves_the_users_other_sessions_alone()
    {
        var service = NewService();
        var first = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        var second = await service.IssueAsync(_userId, "198.51.100.4", "other", CancellationToken.None);

        await service.RevokeAsync(first.SessionId, SessionRevocationReason.Logout, CancellationToken.None);

        Assert.False((await _context.Sessions.SingleAsync(s => s.Id == first.SessionId)).IsActive(Now));
        Assert.True((await _context.Sessions.SingleAsync(s => s.Id == second.SessionId)).IsActive(Now));
    }

    /// <summary>Revoking all sessions leaves no active session for the user.</summary>
    [Fact]
    public async Task Revoking_all_sessions_leaves_no_active_session_for_the_user()
    {
        var service = NewService();
        await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        await service.IssueAsync(_userId, "198.51.100.4", "other", CancellationToken.None);

        await service.RevokeAllAsync(_userId, SessionRevocationReason.LogoutAll, CancellationToken.None);

        Assert.Empty(await _context.Sessions.Where(s => s.UserId == _userId && s.RevokedAt == null).ToListAsync());
    }

    /// <summary>A session issued today expires after the configured refresh lifetime.</summary>
    [Fact]
    public async Task A_session_issued_today_expires_after_the_configured_refresh_lifetime()
    {
        var issued = await NewService().IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        Assert.Equal(Now.AddDays(14), issued.ExpiresAt);
    }
}
