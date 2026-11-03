using Maran.Modules.Identity.Commands.EndViewAs;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.EndViewAs;

/// <summary>Behavioural contract of the banner's Back (issue #317).</summary>
public sealed class EndViewAsCommandHandlerTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private EndViewAsCommandHandler NewHandler()
    {
        return new EndViewAsCommandHandler(_fixture.Context, _fixture.Accounts, _fixture.Journal, _fixture.Clock);
    }

    private async Task<(User Admin, User Owner, Session ViewAs)> SeedViewAsAsync()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");
        var viewAs = Session.StartViewAs(
            Guid.NewGuid(),
            owner.Id,
            accountId,
            admin.Id,
            adminSession.Id,
            Guid.NewGuid().ToString("N"),
            ViewAsFixture.Now,
            "198.51.100.1",
            "admin-agent");
        _fixture.Context.Sessions.Add(viewAs);
        await _fixture.Context.SaveChangesAsync();
        return (admin, owner, viewAs);
    }

    /// <summary>Back revokes the view-as session and journals the end with the administrator as actor.</summary>
    [Fact]
    public async Task Back_revokes_the_view_as_session_and_journals_the_administrator()
    {
        var (admin, owner, viewAs) = await SeedViewAsAsync();

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, owner.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == viewAs.Id);
        Assert.Equal(SessionRevocationReason.ViewAsEnded, stored.Revocation?.Reason);

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(IdentityAuditActions.ViewAsEnded, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal(admin.Username, entry.ActorUsername);
        Assert.Contains(owner.Username, entry.Subject, StringComparison.Ordinal);
        Assert.Contains("acme", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>An own session has nothing to go back from, and nothing is revoked or journalled.</summary>
    [Fact]
    public async Task An_own_session_is_not_viewing_and_nothing_changes()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(adminSession.Id, admin.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Null((await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id)).Revocation);
        Assert.Empty(_fixture.Audit.Written);
    }

    /// <summary>An expired view-as session cannot be ended a second time and journals nothing.</summary>
    [Fact]
    public async Task An_expired_view_as_session_is_not_viewing()
    {
        var (_, owner, viewAs) = await SeedViewAsAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(16));

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, owner.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_fixture.Audit.Written);
    }
}
