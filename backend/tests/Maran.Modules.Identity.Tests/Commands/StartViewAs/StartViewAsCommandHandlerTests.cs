using System.Text.Json;
using Maran.Modules.Identity.Commands.StartViewAs;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Modules.Identity.Tests.Commands.StartViewAs;

/// <summary>
/// Behavioural contract of starting a view-as session for an account (issues #317, #179): an
/// administrator may view any account, a reseller only its own, and nobody else at all.
/// </summary>
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
        return new StartViewAsCommandHandler(_fixture.Context, _fixture.Accounts, _fixture.NewStarter());
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
        Assert.Equal("ViewAs", token.GetClaim(PanelClaimTypes.SessionKind).Value);

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

        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.ViewAs != null);
        Assert.Equal(ViewAsFixture.Now.Add(ViewAsLifetime.Duration), stored.ExpiresAt);
        Assert.Equal(stored.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal(admin.Id, stored.ViewAs?.ActorUserId);
        Assert.Equal(adminSession.Id, stored.ViewAs?.ActorSessionId);
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
        Assert.Equal(IdentityAuditActions.ViewAsStarted, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal(admin.Username, entry.ActorUsername);
        Assert.Equal($"account acme as {owner.Username}", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>What the banner is told names the account as the subject, with the account's id.</summary>
    [Fact]
    public async Task The_banner_is_told_the_account_kind_its_id_its_name_and_the_operator()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        var viewing = result.Value.Viewing;
        Assert.Equal(ViewAsKind.Account, viewing.Kind);
        Assert.Equal(accountId, viewing.SubjectId);
        Assert.Equal("acme", viewing.SubjectName);
        Assert.Equal(owner.Username, viewing.Username);
        Assert.Equal(admin.Username, viewing.OperatorUsername);
        Assert.Equal(result.Value.ExpiresAt, viewing.ExpiresAt);
        Assert.Equal(accountId, result.Value.User.AccountId);
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
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
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
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }

    /// <summary>A suspended account cannot be viewed as: its owner's membership is suspended.</summary>
    [Fact]
    public async Task A_suspended_account_cannot_be_viewed_as()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, _) = await _fixture.SeedAccountAsync();
        (await _fixture.Context.AccountMemberships.SingleAsync(m => m.AccountId == accountId)).Suspend();
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }

    /// <summary>A reseller viewing one of its own accounts gets a customer token naming the reseller as actor.</summary>
    [Fact]
    public async Task A_reseller_viewing_its_own_account_gets_a_customer_token_naming_it_as_actor()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme", resellerId);
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));

        var result = await NewHandler().HandleAsync(Command(accountId, login.Id, session.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(result.Value.AccessToken);
        Assert.Equal(owner.Id.ToString(), token.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal(nameof(UserRole.Customer), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(accountId.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
        Assert.False(token.TryGetClaim(PanelClaimTypes.ResellerId, out _));
        using var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value);
        Assert.Equal(login.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(login.Id, entry.ActorUserId);
        Assert.Equal(IdentityAuditActions.ViewAsStarted, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A reseller naming another reseller's account is answered exactly as for an account that does not exist.</summary>
    /// <remarks>
    /// The foreign account really exists and really has an owning login, so the only thing that can
    /// make the answer identical to the random-id one is the directory's scope: both outcomes are
    /// compared field by field, and the second is the accepting control for the first.
    /// </remarks>
    [Fact]
    public async Task A_reseller_naming_another_resellers_account_is_answered_as_for_an_unknown_one()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync("northwind");
        var (otherResellerId, _) = await _fixture.SeedResellerAsync("contoso");
        var (foreignAccountId, _) = await _fixture.SeedAccountAsync("foreign", otherResellerId);
        var (ownAccountId, _) = await _fixture.SeedAccountAsync("mine", resellerId);
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));

        var foreign = await NewHandler().HandleAsync(Command(foreignAccountId, login.Id, session.Id), CancellationToken.None);
        var unknown = await NewHandler().HandleAsync(Command(Guid.NewGuid(), login.Id, session.Id), CancellationToken.None);
        var own = await NewHandler().HandleAsync(Command(ownAccountId, login.Id, session.Id), CancellationToken.None);

        Assert.False(foreign.IsSuccess);
        Assert.NotNull(foreign.Error);
        Assert.NotNull(unknown.Error);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error.Type, foreign.Error.Type);
        Assert.Equal("ViewAsAccountNotFound", foreign.Error.Code);
        Assert.Equal(unknown.Error.Code, foreign.Error.Code);
        Assert.True(own.IsSuccess);
        Assert.Single(await _fixture.Context.Sessions.Where(s => s.ViewAs != null).ToListAsync());
    }

    /// <summary>A reseller cannot view an account an administrator owns.</summary>
    [Fact]
    public async Task A_reseller_cannot_view_an_account_that_has_no_reseller_owner()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (adminAccountId, _) = await _fixture.SeedAccountAsync("admins");
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));

        var result = await NewHandler().HandleAsync(Command(adminAccountId, login.Id, session.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ViewAsAccountNotFound", result.Error!.Code);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }

    /// <summary>A refused start is journalled as a failure with the operator as actor and the id asked for.</summary>
    [Fact]
    public async Task A_refused_start_is_journalled_as_a_failure_naming_the_id_asked_for()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (otherResellerId, _) = await _fixture.SeedResellerAsync("contoso");
        var (foreignAccountId, _) = await _fixture.SeedAccountAsync("foreign", otherResellerId);
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));

        await NewHandler().HandleAsync(Command(foreignAccountId, login.Id, session.Id), CancellationToken.None);

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(IdentityAuditActions.ViewAsStarted, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(login.Id, entry.ActorUserId);
        Assert.Equal(login.Username, entry.ActorUsername);
        Assert.Equal(foreignAccountId.ToString(), entry.Subject);
    }

    /// <summary>A suspended reseller's still-valid token cannot start a view-as session.</summary>
    [Fact]
    public async Task A_suspended_reseller_cannot_start_a_view_as_session()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync(state: ResellerState.Suspended);
        var (accountId, _) = await _fixture.SeedAccountAsync("acme", resellerId);
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));

        var result = await NewHandler().HandleAsync(Command(accountId, login.Id, session.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
        Assert.False(Assert.Single(_fixture.Audit.Written).Succeeded);
    }

    /// <summary>A reseller whose login was suspended cannot start a view-as session even if the directory says active.</summary>
    [Fact]
    public async Task A_reseller_whose_login_is_suspended_cannot_start_a_view_as_session()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (accountId, _) = await _fixture.SeedAccountAsync("acme", resellerId);
        _fixture.Accounts.ActAs(new StubResellerUser(login.Id, login.Username, resellerId));
        login.Suspend();
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, login.Id, session.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
    }

    /// <summary>A reseller the accounts module no longer knows cannot start a view-as session.</summary>
    [Fact]
    public async Task A_reseller_the_directory_no_longer_knows_cannot_start_a_view_as_session()
    {
        var login = User.InviteReseller(Guid.NewGuid(), "ghost", "ghost@example.com", Guid.NewGuid(), ViewAsFixture.Now);
        login.Activate("hash");
        _fixture.Context.Users.Add(login);
        await _fixture.Context.SaveChangesAsync();
        var (accountId, _) = await _fixture.SeedAccountAsync("acme");

        var result = await NewHandler().HandleAsync(Command(accountId, login.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ViewAsAccountNotFound", result.Error!.Code);
    }

    /// <summary>A token naming an operator whose row is gone cannot start a view-as session.</summary>
    [Fact]
    public async Task An_operator_whose_row_is_gone_cannot_start_a_view_as_session()
    {
        var (accountId, _) = await _fixture.SeedAccountAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.False(await _fixture.Context.Sessions.AnyAsync(s => s.ViewAs != null));
        Assert.False(Assert.Single(_fixture.Audit.Written).Succeeded);
    }

    /// <summary>An account whose owning login has not set its password yet answers login unavailable, not a token.</summary>
    [Fact]
    public async Task An_owner_login_that_may_not_sign_in_is_login_unavailable()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var accountId = Guid.NewGuid();
        _fixture.Accounts.Add(accountId, "waiting", maxFtpUsers: 0);
        var invited = User.Invite(Guid.NewGuid(), "waiting-owner", "waiting@example.com", ViewAsFixture.Now);
        _fixture.Context.Users.Add(invited);
        _fixture.Context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), invited.Id, accountId, ViewAsFixture.Now));
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(accountId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("ViewAsLoginUnavailable", result.Error.Code);
        Assert.False(Assert.Single(_fixture.Audit.Written).Succeeded);
    }

    /// <summary>An account with no owning login at all is not found, never a conflict.</summary>
    [Fact]
    public async Task An_account_with_no_owning_login_is_not_found()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var orphanId = Guid.NewGuid();
        _fixture.Accounts.Add(orphanId, "orphan", maxFtpUsers: 0);

        var result = await NewHandler().HandleAsync(Command(orphanId, admin.Id, adminSession.Id), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ViewAsAccountNotFound", result.Error.Code);
    }
}
