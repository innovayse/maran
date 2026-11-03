using System.Text.Json;
using Maran.Modules.Identity.Commands.StartViewAsReseller;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Modules.Identity.Tests.Commands.StartViewAsReseller;

/// <summary>
/// Behavioural contract of starting a view-as session for a reseller (issues #317, #179): only an
/// administrator may, and the token that results holds the reseller's role and none of the
/// administrator's power.
/// </summary>
public sealed class StartViewAsResellerCommandHandlerTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private StartViewAsResellerCommandHandler NewHandler()
    {
        return new StartViewAsResellerCommandHandler(_fixture.Context, _fixture.Resellers, _fixture.NewStarter());
    }

    private static StartViewAsResellerCommand Command(Guid resellerId, Guid operatorId, Guid operatorSessionId)
    {
        return new StartViewAsResellerCommand(resellerId, operatorId, operatorSessionId, "198.51.100.1", "admin-agent");
    }

    /// <summary>An administrator gets a Reseller token that names the reseller and the administrator as actor.</summary>
    [Fact]
    public async Task An_administrator_gets_a_reseller_token_that_names_the_reseller_and_the_actor()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(result.Value.AccessToken);
        Assert.Equal(login.Id.ToString(), token.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal(nameof(UserRole.Reseller), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(resellerId.ToString(), token.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.False(token.TryGetClaim(PanelClaimTypes.AccountId, out _));
        Assert.Equal("ViewAs", token.GetClaim(PanelClaimTypes.SessionKind).Value);

        using var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value);
        Assert.Equal(admin.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());
        Assert.Equal(adminSession.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.SessionId).GetString());
    }

    /// <summary>The administrator keeps no administrator power: nothing in the token says Admin.</summary>
    [Fact]
    public async Task The_token_never_carries_the_administrator_role()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, _) = await _fixture.SeedResellerAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(result.Value.AccessToken);
        Assert.NotEqual(nameof(UserRole.Admin), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.DoesNotContain(nameof(UserRole.Admin), token.Claims.Where(c =>
        {
            return c.Type == PanelClaimTypes.Role;
        }).Select(c =>
{
    return c.Value;
}));
    }

    /// <summary>The session is stored with no account, for the viewed login, and lives the view-as lifetime.</summary>
    [Fact]
    public async Task The_session_is_stored_for_the_reseller_login_with_no_account()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.ViewAs != null);
        Assert.Equal(login.Id, stored.UserId);
        Assert.Null(stored.ActiveAccountId);
        Assert.Equal(admin.Id, stored.ViewAs?.ActorUserId);
        Assert.Equal(adminSession.Id, stored.ViewAs?.ActorSessionId);
        Assert.Equal(stored.ExpiresAt, result.Value.ExpiresAt);
    }

    /// <summary>What the banner is told names the reseller as the subject, with the reseller's id.</summary>
    [Fact]
    public async Task The_banner_is_told_the_reseller_kind_its_id_its_name_and_the_operator()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync("northwind");

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        var viewing = result.Value.Viewing;
        Assert.Equal(ViewAsKind.Reseller, viewing.Kind);
        Assert.Equal(resellerId, viewing.SubjectId);
        Assert.Equal("northwind", viewing.SubjectName);
        Assert.Equal(login.Username, viewing.Username);
        Assert.Equal(admin.Username, viewing.OperatorUsername);
        Assert.Null(result.Value.User.AccountId);
        Assert.Equal(UserRole.Reseller, result.Value.User.Role);
    }

    /// <summary>The start is journalled with the administrator as actor and the reseller as subject.</summary>
    [Fact]
    public async Task The_start_is_journalled_with_the_administrator_and_the_reseller()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync("northwind");

        await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(IdentityAuditActions.ViewAsStarted, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal(admin.Username, entry.ActorUsername);
        Assert.Equal($"reseller northwind as {login.Username}", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A reseller viewing a reseller is not an edge: even its own, it is refused and issues nothing.</summary>
    [Fact]
    public async Task A_reseller_operator_is_refused_even_for_its_own_reseller()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, login.Id, session.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ViewAsResellerNotFound", result.Error.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.False(entry.Succeeded);
        Assert.Equal(login.Id, entry.ActorUserId);
    }

    /// <summary>A customer named as operator is refused and issues nothing.</summary>
    [Fact]
    public async Task A_customer_named_as_operator_is_refused()
    {
        var (_, owner) = await _fixture.SeedAccountAsync();
        var (resellerId, _) = await _fixture.SeedResellerAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, owner.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ViewAsResellerNotFound", result.Error!.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }

    /// <summary>A token naming an operator whose row is gone is refused and the refusal is still journalled.</summary>
    [Fact]
    public async Task An_operator_whose_row_is_gone_is_refused_and_journalled()
    {
        var (resellerId, _) = await _fixture.SeedResellerAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.False(Assert.Single(_fixture.Audit.Written).Succeeded);
    }

    /// <summary>A reseller the directory does not know is not found, journalled with the id asked for.</summary>
    [Fact]
    public async Task An_unknown_reseller_is_not_found_and_journalled_with_the_id_asked_for()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var unknown = Guid.NewGuid();

        var result = await NewHandler().HandleAsync(Command(unknown, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ViewAsResellerNotFound", result.Error.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.False(entry.Succeeded);
        Assert.Equal(unknown.ToString(), entry.Subject);
    }

    /// <summary>A reseller whose login was never issued is not found, never a conflict.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_is_not_found()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var resellerId = Guid.NewGuid();
        _fixture.Resellers.Add(resellerId, "loginless");

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ViewAsResellerNotFound", result.Error.Code);
    }

    /// <summary>A suspended reseller cannot be viewed as, and the refusal is a conflict naming the login.</summary>
    [Fact]
    public async Task A_suspended_reseller_cannot_be_viewed_as()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync("northwind", ResellerState.Suspended);

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("ViewAsLoginUnavailable", result.Error.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.False(entry.Succeeded);
        Assert.Equal($"reseller northwind as {login.Username}", entry.Subject);
    }

    /// <summary>A reseller login that has not set its password yet cannot be viewed as.</summary>
    [Fact]
    public async Task A_reseller_login_waiting_for_its_invitation_cannot_be_viewed_as()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, _) = await _fixture.SeedResellerAsync(claimed: false);

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("ViewAsLoginUnavailable", result.Error.Code);
    }

    /// <summary>A reseller whose login is itself suspended cannot be viewed as, whatever the directory says.</summary>
    [Fact]
    public async Task A_reseller_login_that_is_suspended_cannot_be_viewed_as()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync();
        login.Suspend();
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(resellerId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }
}
