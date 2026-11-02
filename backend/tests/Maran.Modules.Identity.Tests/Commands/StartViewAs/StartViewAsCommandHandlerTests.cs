using System.Text.Json;
using Maran.Modules.Identity.Commands.StartViewAs;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Modules.Identity.Tests.Commands.StartViewAs;

/// <summary>Behavioural contract of starting a view-as session (issue #317).</summary>
public sealed class StartViewAsCommandHandlerTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private StartViewAsCommandHandler NewHandler()
    {
        return new StartViewAsCommandHandler(
            _fixture.Context,
            _fixture.Accounts,
            _fixture.NewIssuer(),
            _fixture.Journal,
            _fixture.Clock);
    }

    private static StartViewAsCommand Command(Guid accountId, Guid adminId, Guid adminSessionId)
    {
        return new StartViewAsCommand(accountId, adminId, adminSessionId, "198.51.100.1", "admin-agent");
    }

    /// <summary>An administrator starting a view-as session gets a customer token naming them as actor.</summary>
    [Fact]
    public async Task An_administrator_gets_a_customer_token_that_names_them_as_actor()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(result.Value.AccessToken);
        Assert.Equal(owner.Id.ToString(), token.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal(nameof(UserRole.Customer), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(accountId.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
        Assert.Equal(nameof(SessionKind.ViewAs), token.GetClaim(PanelClaimTypes.SessionKind).Value);

        using var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value);
        Assert.Equal(admin.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());
        Assert.Equal(adminSession.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.SessionId).GetString());
        Assert.Equal(admin.Username, actor.RootElement.GetProperty(PanelClaimTypes.Username).GetString());
    }

    /// <summary>The view-as session lives fifteen minutes and the token dies with it.</summary>
    [Fact]
    public async Task The_session_and_its_token_expire_together_after_the_view_as_lifetime()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, _) = await _fixture.SeedAccountAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Kind == SessionKind.ViewAs);
        Assert.Equal(ViewAsFixture.Now.Add(ViewAsLifetime.Duration), stored.ExpiresAt);
        Assert.Equal(stored.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal(admin.Id, stored.ActorUserId);
        Assert.Equal(adminSession.Id, stored.ActorSessionId);
        Assert.Equal(accountId, stored.ActiveAccountId);
    }

    /// <summary>The start is journalled with the administrator as actor and the viewed identity as subject.</summary>
    [Fact]
    public async Task The_start_is_journalled_with_the_administrator_and_the_viewed_identity()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");

        await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(AuditActions.ViewAsStarted, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal(admin.Username, entry.ActorUsername);
        Assert.Contains("acme", entry.Subject, StringComparison.Ordinal);
        Assert.Contains(owner.Username, entry.Subject, StringComparison.Ordinal);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An account the directory does not answer is not found, journalled, and issues nothing.</summary>
    [Fact]
    public async Task An_unknown_account_is_not_found_and_issues_no_session()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();

        var result = await NewHandler().HandleAsync(Command(Guid.NewGuid(), admin.Id, adminSession.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("ViewAsAccountNotFound", result.Error.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.Kind == SessionKind.ViewAs));
        Assert.False(Assert.Single(_fixture.Audit.Written).Succeeded);
    }

    /// <summary>A caller whose row is not an administrator cannot start a view-as session.</summary>
    [Fact]
    public async Task A_customer_named_as_operator_is_refused_and_issues_no_session()
    {
        var (_, owner) = await _fixture.SeedAccountAsync();
        var (otherAccountId, _) = await _fixture.SeedAccountAsync("other");

        var result = await NewHandler().HandleAsync(Command(otherAccountId, owner.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.Kind == SessionKind.ViewAs));
    }

    /// <summary>A suspended login cannot be viewed as.</summary>
    [Fact]
    public async Task A_suspended_login_cannot_be_viewed_as()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        owner.Suspend();
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.Kind == SessionKind.ViewAs));
    }
}
