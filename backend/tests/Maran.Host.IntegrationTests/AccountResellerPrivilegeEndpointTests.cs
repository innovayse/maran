using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The real HTTP surface of what a reseller may not do to the way a customer signs in: it may not
/// change an account's owner address, which decides where the next password reset goes (issue #482),
/// and it may not create an account with a login shell or a first password it chose (issue #488).
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountResellerPrivilegeEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountResellerPrivilegeEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A reseller is refused the owner-address route with 403, for its own account and another's, and nothing changes.</summary>
    [Fact]
    public async Task A_reseller_is_refused_the_owner_address_route_and_nothing_changes()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var before = await StoredOwnerEmailAsync(world, world.Alpha.AccountId);

        var own = await alpha.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner-email", new { OwnerEmail = "attacker@example.com" });
        var foreign = await alpha.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Bravo.AccountId}/owner-email", new { OwnerEmail = "attacker@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(before, await StoredOwnerEmailAsync(world, world.Alpha.AccountId));
        Assert.Empty(await world.AuditEntriesAsync(AccountAuditActions.AccountOwnerEmailChanged));
    }

    /// <summary>The accepting control: an administrator may change the owner address of a reseller's account.</summary>
    [Fact]
    public async Task An_administrator_may_change_the_owner_address_of_a_resellers_account()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner-email", new { OwnerEmail = "moved@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("moved@example.com", await StoredOwnerEmailAsync(world, world.Alpha.AccountId));
    }

    /// <summary>A reseller creating an account with a login shell or a first password is refused with 403 and nothing is made.</summary>
    [Fact]
    public async Task A_reseller_creating_an_account_with_a_shell_or_a_first_password_is_refused()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var createdBefore = world.Agent.Created.Count;

        var shell = await alpha.PostAsJsonAsync(
            "/api/v1/accounts",
            new
            {
                Name = "withshell",
                PrimaryDomain = "withshell.example.com",
                PlanId = world.Alpha.PlanId,
                OwnerEmail = "withshell@example.com",
                ShellAccess = true,
            });
        var password = await alpha.PostAsJsonAsync(
            "/api/v1/accounts",
            new
            {
                Name = "withpassword",
                PrimaryDomain = "withpassword.example.com",
                PlanId = world.Alpha.PlanId,
                OwnerEmail = "withpassword@example.com",
                LoginMode = "initialPassword",
                InitialPassword = "correct horse battery staple",
            });

        Assert.Equal(HttpStatusCode.Forbidden, shell.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.Contains("AccountShellAccessAdministratorOnly", await shell.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("AccountInitialPasswordAdministratorOnly", await password.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(createdBefore, world.Agent.Created.Count);
    }

    /// <summary>The accepting control: a reseller creating an account the plain way is not refused.</summary>
    [Fact]
    public async Task A_reseller_creating_an_account_the_plain_way_is_accepted()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("plainway", world.Alpha.PlanId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Reads the owner address stored on one account row.</summary>
    /// <param name="world">The world the test built.</param>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The stored address.</returns>
    private static Task<string?> StoredOwnerEmailAsync(ResellerWorld world, Guid accountId)
    {
        return world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking()
                .Where(account => account.Id == accountId)
                .Select(account => account.OwnerEmail)
                .SingleAsync();
        });
    }
}
