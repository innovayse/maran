using Maran.Modules.Identity.Commands.RevokeSession;
using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.RevokeSession;
/// <summary>Behavioural contract of revoke session command handler.</summary>

public sealed class RevokeSessionCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingAuditWriter _audit = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public async Task InitializeAsync()
    {
        _context.Users.Add(new User(_owner, "owner", "owner@example.com", "hash", UserRole.Customer, Now));
        _context.Users.Add(new User(_stranger, "stranger", "stranger@example.com", "hash", UserRole.Customer, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private SessionService NewSessionService()
    {
        return new SessionService(_context, _clock, Options.Create(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private RevokeSessionCommandHandler NewHandler()
    {
        return new RevokeSessionCommandHandler(_context, NewSessionService(), _audit);
    }

    /// <summary>Revoking ones own session ends it and is audited.</summary>
    [Fact]
    public async Task Revoking_ones_own_session_ends_it_and_is_audited()
    {
        var issued = await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);

        var result = await NewHandler().HandleAsync(
            new RevokeSessionCommand(issued.SessionId, _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False((await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId)).IsActive(Now));
        Assert.Equal(AuditActions.SessionRevoked, _audit.Written.Single().Action);
    }

    /// <summary>Revoking another users session answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Revoking_another_users_session_answers_not_found_rather_than_forbidden()
    {
        // 403 would confirm the id exists, which is all an attacker needs to enumerate other
        // people's devices (rules/testing.md: the IDOR answer is 404).
        var theirs = await NewSessionService().IssueAsync(_stranger, "198.51.100.4", "other", CancellationToken.None);

        var result = await NewHandler().HandleAsync(
            new RevokeSessionCommand(theirs.SessionId, _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SessionNotFound", result.Error!.Code);
    }

    /// <summary>Revoking another users session leaves it running.</summary>
    [Fact]
    public async Task Revoking_another_users_session_leaves_it_running()
    {
        var theirs = await NewSessionService().IssueAsync(_stranger, "198.51.100.4", "other", CancellationToken.None);

        await NewHandler().HandleAsync(
            new RevokeSessionCommand(theirs.SessionId, _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.True((await _context.Sessions.SingleAsync(s => s.Id == theirs.SessionId)).IsActive(Now));
        Assert.Empty(_audit.Written);
    }

    /// <summary>Revoking a session that does not exist answers not found.</summary>
    [Fact]
    public async Task Revoking_a_session_that_does_not_exist_answers_not_found()
    {
        var result = await NewHandler().HandleAsync(
            new RevokeSessionCommand(Guid.NewGuid(), _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal("SessionNotFound", result.Error!.Code);
    }
}
