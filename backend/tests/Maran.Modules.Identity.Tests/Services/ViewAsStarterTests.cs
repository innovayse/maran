using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the one place a view-as session starts, for both kinds (issues #317,
/// #179): who may be an operator at all, and what a start stores, signs and journals.
/// </summary>
public sealed class ViewAsStarterTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    /// <summary>An administrator that may sign in is an operator.</summary>
    [Fact]
    public async Task An_administrator_is_an_operator()
    {
        var (admin, _) = await _fixture.SeedAdminAsync();

        var found = await _fixture.NewStarter().FindOperatorAsync(admin.Id, CancellationToken.None);

        Assert.Equal(admin.Id, found?.Id);
    }

    /// <summary>A reseller whose reseller is active and whose login may sign in is an operator.</summary>
    [Fact]
    public async Task An_active_reseller_is_an_operator()
    {
        var (_, login, _) = await _fixture.SeedResellerOperatorAsync();

        var found = await _fixture.NewStarter().FindOperatorAsync(login.Id, CancellationToken.None);

        Assert.Equal(login.Id, found?.Id);
    }

    /// <summary>A reseller whose reseller is suspended is not an operator, though its login row is fine.</summary>
    [Fact]
    public async Task A_reseller_suspended_in_the_directory_is_not_an_operator()
    {
        var (_, login, _) = await _fixture.SeedResellerOperatorAsync(state: ResellerState.Suspended);

        Assert.Null(await _fixture.NewStarter().FindOperatorAsync(login.Id, CancellationToken.None));
    }

    /// <summary>A reseller whose own login is suspended is not an operator, though the directory says active.</summary>
    [Fact]
    public async Task A_reseller_whose_login_is_suspended_is_not_an_operator()
    {
        var (_, login, _) = await _fixture.SeedResellerOperatorAsync();
        login.Suspend();
        await _fixture.Context.SaveChangesAsync();

        Assert.Null(await _fixture.NewStarter().FindOperatorAsync(login.Id, CancellationToken.None));
    }

    /// <summary>A reseller login the accounts module does not know is not an operator.</summary>
    [Fact]
    public async Task A_reseller_login_with_no_reseller_in_the_directory_is_not_an_operator()
    {
        var login = User.InviteReseller(Guid.NewGuid(), "ghost", "ghost@example.com", Guid.NewGuid(), ViewAsFixture.Now);
        login.Activate("hash");
        _fixture.Context.Users.Add(login);
        await _fixture.Context.SaveChangesAsync();

        Assert.Null(await _fixture.NewStarter().FindOperatorAsync(login.Id, CancellationToken.None));
    }

    /// <summary>A customer, and a login that does not exist, are not operators.</summary>
    [Fact]
    public async Task A_customer_and_an_unknown_login_are_not_operators()
    {
        var (_, owner) = await _fixture.SeedAccountAsync();
        var starter = _fixture.NewStarter();

        Assert.Null(await starter.FindOperatorAsync(owner.Id, CancellationToken.None));
        Assert.Null(await starter.FindOperatorAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>An account start stores a session for the account, signs a customer token and journals the start.</summary>
    [Fact]
    public async Task An_account_start_stores_signs_and_journals()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");

        var started = await _fixture.NewStarter().StartAsync(
            admin,
            owner,
            new ViewAsSubject(ViewAsKind.Account, accountId, "acme"),
            adminSession.Id,
            "198.51.100.1",
            "agent",
            CancellationToken.None);

        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.ViewAs != null);
        Assert.Equal(accountId, stored.ActiveAccountId);
        Assert.Equal(owner.Id, stored.UserId);
        Assert.Equal(accountId, started.User.AccountId);
        Assert.Equal(ViewAsKind.Account, started.Viewing.Kind);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(started.AccessToken);
        Assert.Equal(accountId.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal($"account acme as {owner.Username}", entry.Subject);
        Assert.Equal("198.51.100.1", entry.IpAddress);
        Assert.Equal("agent", entry.UserAgent);
    }

    /// <summary>A reseller start stores a session with no account and signs a token with the reseller claim.</summary>
    [Fact]
    public async Task A_reseller_start_stores_no_account_and_signs_the_reseller_claim()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync("northwind");

        var started = await _fixture.NewStarter().StartAsync(
            admin,
            login,
            new ViewAsSubject(ViewAsKind.Reseller, resellerId, "northwind"),
            adminSession.Id,
            "198.51.100.1",
            "agent",
            CancellationToken.None);

        Assert.Null((await _fixture.Context.Sessions.SingleAsync(s => s.ViewAs != null)).ActiveAccountId);
        Assert.Equal(ViewAsKind.Reseller, started.Viewing.Kind);
        Assert.Equal(resellerId, started.Viewing.SubjectId);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(started.AccessToken);
        Assert.Equal(resellerId.ToString(), token.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.Equal($"reseller northwind as {login.Username}", Assert.Single(_fixture.Audit.Written).Subject);
    }

    /// <summary>A subject of one kind over a login of the other throws, signs nothing and journals no start.</summary>
    /// <remarks>
    /// The token issuer refuses a session that disagrees with its login; the starter must not turn
    /// that refusal into a start entry, and the session row is stored only once the token exists, so
    /// no row remains either. Both mismatch directions are covered.
    /// </remarks>
    [Fact]
    public async Task A_subject_that_disagrees_with_the_login_throws_and_journals_nothing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var starter = _fixture.NewStarter();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await starter.StartAsync(
                admin,
                login,
                new ViewAsSubject(ViewAsKind.Account, accountId, "acme"),
                adminSession.Id,
                "198.51.100.1",
                "agent",
                CancellationToken.None);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await starter.StartAsync(
                admin,
                owner,
                new ViewAsSubject(ViewAsKind.Reseller, resellerId, "northwind"),
                adminSession.Id,
                "198.51.100.1",
                "agent",
                CancellationToken.None);
        });

        Assert.Empty(_fixture.Audit.Written);
        Assert.Empty(await _fixture.Context.Sessions.Where(s => s.ViewAs != null).ToListAsync());
    }

    /// <summary>An administrator is never a target: starting over an administrator's login throws.</summary>
    [Fact]
    public async Task An_administrator_login_is_never_a_target()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync("root-admin");
        var (otherAdmin, _) = await _fixture.SeedAdminAsync("other-admin");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.NewStarter().StartAsync(
                admin,
                otherAdmin,
                new ViewAsSubject(ViewAsKind.Account, Guid.NewGuid(), "x"),
                adminSession.Id,
                "198.51.100.1",
                "agent",
                CancellationToken.None);
        });
        Assert.Empty(_fixture.Audit.Written);
    }

    /// <summary>A refusal is journalled as a failed start with the operator named, or empty when there was none.</summary>
    [Fact]
    public async Task A_refusal_is_journalled_as_a_failed_start()
    {
        var (admin, _) = await _fixture.SeedAdminAsync();
        var starter = _fixture.NewStarter();

        await starter.RecordRefusalAsync(admin.Id, admin, "subject-one", "198.51.100.1", "agent", CancellationToken.None);
        await starter.RecordRefusalAsync(Guid.NewGuid(), null, "subject-two", "198.51.100.1", "agent", CancellationToken.None);

        Assert.Collection(
            _fixture.Audit.Written,
            first =>
            {
                Assert.Equal(IdentityAuditActions.ViewAsStarted, first.Action);
                Assert.False(first.Succeeded);
                Assert.Equal(admin.Username, first.ActorUsername);
                Assert.Equal("subject-one", first.Subject);
            },
            second =>
            {
                Assert.False(second.Succeeded);
                Assert.Equal(string.Empty, second.ActorUsername);
            });
    }
}
