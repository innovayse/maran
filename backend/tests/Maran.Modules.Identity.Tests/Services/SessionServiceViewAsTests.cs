using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the session service towards a view-as session (issue #317).</summary>
public sealed class SessionServiceViewAsTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    /// <summary>
    /// A view-as session cannot be rotated even by somebody holding its refresh token, so it can never
    /// outlive its fifteen minutes.
    /// </summary>
    [Fact]
    public async Task A_view_as_session_can_never_be_rotated()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var leaked = RefreshTokenHasher.Generate();
        _fixture.Context.Sessions.Add(Session.StartViewAs(
            Guid.NewGuid(),
            owner.Id,
            accountId,
            admin.Id,
            adminSession.Id,
            RefreshTokenHasher.Hash(leaked),
            ViewAsFixture.Now,
            "198.51.100.1",
            "admin-agent"));
        await _fixture.Context.SaveChangesAsync();
        var service = new SessionService(
            _fixture.Context,
            _fixture.Clock,
            new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));

        var result = await service.RotateAsync(leaked, "198.51.100.1", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, _fixture.Context.Sessions.Count());
    }
}
