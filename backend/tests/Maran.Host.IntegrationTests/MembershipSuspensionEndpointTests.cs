using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A suspension belongs to ONE account over real HTTP (issue #326): a person who owns two accounts
/// loses the suspended one and keeps the other, at sign-in, in the selector and when switching, and an
/// account the person does not own still answers 404 exactly as before.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class MembershipSuspensionEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The audit writer every host in this class journals into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The identity of acme, olive's oldest account, set when the host boots.</summary>
    private Guid _acme;

    /// <summary>The identity of globex, olive's second account, set when the host boots.</summary>
    private Guid _globex;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MembershipSuspensionEndpointTests(PostgresFixture postgres)
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

    /// <summary>The selector no longer offers a suspended account, and offers the other one.</summary>
    [Fact]
    public async Task A_suspended_account_disappears_from_the_selector_and_the_other_stays()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "olive");
        await SuspendAsync(factory, _globex);

        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/me/accounts"));

        var names = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("name").GetString();
        }).ToList();
        Assert.Equal(["acme"], names);
    }

    /// <summary>Switching to a suspended account of one's own answers 409 with its code, and is journalled as refused.</summary>
    [Fact]
    public async Task Switching_to_a_suspended_account_answers_409_and_is_journalled_as_refused()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "olive");
        var globex = _globex;
        await SuspendAsync(factory, _globex);

        var response = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = globex });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AccountMembershipSuspended", body.RootElement.GetProperty("code").GetString());
        var entry = Assert.Single(_audit.Entries, candidate =>
        {
            return candidate.Action == IdentityAuditActions.ActiveAccountSwitched;
        });
        Assert.False(entry.Succeeded);
        Assert.Equal(globex.ToString(), entry.Subject);
    }

    /// <summary>
    /// Someone else's account still answers 404 while the person's own other account is suspended: a
    /// suspension discloses nothing about accounts the person does not hold.
    /// </summary>
    [Fact]
    public async Task Somebody_elses_account_still_answers_404_when_one_of_the_persons_own_is_suspended()
    {
        await using var factory = await BootAsync();
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "initech");
        using var client = await EndpointHarness.SignInAsync(factory, "olive");
        await SuspendAsync(factory, _globex);

        var response = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = stranger });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>With the oldest account suspended, a new sign-in speaks for the other one.</summary>
    [Fact]
    public async Task A_new_sign_in_speaks_for_the_account_that_is_not_suspended()
    {
        await using var factory = await BootAsync();
        await SuspendAsync(factory, _acme);

        using var client = await EndpointHarness.SignInAsync(factory, "olive");

        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/accounts/me"));
        Assert.Equal("globex.example.com", body.RootElement.GetProperty("primaryDomain").GetString());
    }

    /// <summary>A person whose every account is suspended is refused at sign-in exactly like a wrong password.</summary>
    [Fact]
    public async Task A_person_whose_every_account_is_suspended_is_refused_at_sign_in()
    {
        await using var factory = await BootAsync();
        await SuspendAsync(factory, _acme);
        await SuspendAsync(factory, _globex);
        using var client = factory.CreateClient();

        var refused = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "olive", Password = EndpointHarness.Password });
        var wrongPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "olive", Password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("InvalidCredentialsUnauthorized", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// The database refuses a second owner of an account, which is what the dropped unique index on the
    /// login used to guarantee: two owners would be two keys to one account.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_second_owner_of_an_account_but_not_a_second_member()
    {
        await using var factory = await BootAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var other = new User(Guid.NewGuid(), "pat", "pat@example.com", hasher.Hash("x"), UserRole.Customer, now);
        var third = new User(Guid.NewGuid(), "sam", "sam@example.com", hasher.Hash("x"), UserRole.Customer, now);
        var fourth = new User(Guid.NewGuid(), "kim", "kim@example.com", hasher.Hash("x"), UserRole.Customer, now);
        context.Users.AddRange(other, third, fourth);
        context.AccountMemberships.Add(AccountMembership.Admit(Guid.NewGuid(), other.Id, _acme, AccountFeature.None, now));
        context.AccountMemberships.Add(AccountMembership.Admit(Guid.NewGuid(), third.Id, _acme, AccountFeature.None, now));
        await context.SaveChangesAsync();

        context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), fourth.Id, _acme, now));
        var refusal = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await context.SaveChangesAsync();
        });

        Assert.Contains("UX_AccountMemberships_AccountId_Owner", refusal.InnerException?.Message);
    }

    /// <summary>Suspends every membership of an account, as the suspension cascade does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account to suspend.</param>
    private static async Task SuspendAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var membership in await context.AccountMemberships.Where(row => row.AccountId == accountId).ToListAsync())
        {
            membership.Suspend();
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Boots the host with an administrator and a customer, olive, who owns acme and globex.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(_audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, globex) = await EndpointHarness.SeedAccountAsync(factory, "globex");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", acme);
        await EndpointHarness.SeedOwnedAccountAsync(factory, "olive", globex);
        _acme = acme;
        _globex = globex;

        return factory;
    }
}
