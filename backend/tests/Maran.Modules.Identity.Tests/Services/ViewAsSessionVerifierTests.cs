using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the per-request check of a view-as token's session (issue #317).</summary>
public sealed class ViewAsSessionVerifierTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private async Task<(User Admin, Session AdminSession, User Owner, Session ViewAs)> SeedAsync()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();
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
        return (admin, adminSession, owner, viewAs);
    }

    private Task<bool> IsLiveAsync(Session viewAs, Guid viewedUserId, Guid actorUserId, Guid actorSessionId)
    {
        return new ViewAsSessionVerifier(_fixture.Context, _fixture.Clock)
            .IsLiveAsync(viewAs.Id, viewedUserId, actorUserId, actorSessionId, CancellationToken.None);
    }

    /// <summary>A fresh view-as session whose operator is still signed in is live.</summary>
    [Fact]
    public async Task A_fresh_session_with_a_signed_in_operator_is_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();

        Assert.True(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>The session stops being live once its lifetime passes.</summary>
    [Fact]
    public async Task An_expired_session_is_not_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(15));

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>Signing the operator out everywhere ends the view-as session too.</summary>
    [Fact]
    public async Task A_revoked_operator_family_ends_the_view_as_session()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id);
        stored.Revoke(ViewAsFixture.Now, SessionRevocationReason.LogoutAll);
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A rotation of the operator's own session keeps the view-as session live.</summary>
    [Fact]
    public async Task A_rotation_of_the_operator_session_keeps_viewing_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id);
        stored.Revoke(ViewAsFixture.Now, SessionRevocationReason.Rotated);
        _fixture.Context.Sessions.Add(new Session(
            Guid.NewGuid(),
            admin.Id,
            stored.FamilyId,
            Guid.NewGuid().ToString("N"),
            ViewAsFixture.Now,
            ViewAsFixture.Now.AddDays(14),
            "198.51.100.1",
            "admin-agent"));
        await _fixture.Context.SaveChangesAsync();

        Assert.True(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A token whose subject or actor disagrees with the row is refused.</summary>
    [Fact]
    public async Task A_token_disagreeing_with_its_row_is_refused()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();

        Assert.False(await IsLiveAsync(viewAs, Guid.NewGuid(), admin.Id, adminSession.Id));
        Assert.False(await IsLiveAsync(viewAs, owner.Id, Guid.NewGuid(), adminSession.Id));
        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, Guid.NewGuid()));
    }

    /// <summary>An own session's id is never accepted as a view-as session.</summary>
    [Fact]
    public async Task An_own_session_is_never_a_live_view_as_session()
    {
        var (admin, adminSession, _, _) = await SeedAsync();

        var verifier = new ViewAsSessionVerifier(_fixture.Context, _fixture.Clock);
        Assert.Null(await verifier.FindLiveAsync(adminSession.Id, CancellationToken.None));
        Assert.False(await verifier.IsLiveAsync(adminSession.Id, admin.Id, admin.Id, adminSession.Id, CancellationToken.None));
    }
}
