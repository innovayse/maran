using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A suspended account closes for EVERY login that acts for it, over real HTTP (issue #331): an
/// invited member, accepted or not, cannot sign in, and an access token issued before the suspension is
/// refused on a tenant endpoint with the same 409 code the other doors answer; resuming the account
/// restores both. The suspension and the resumption are the real subscriber handlers.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SuspendedAccountMemberEndpointTests : IAsyncLifetime
{
    /// <summary>The route every signed-in customer may call whatever their grants, and that carries the guard.</summary>
    private const string TenantRoute = "/api/v1/me/accounts";

    /// <summary>The code a suspended account is refused with.</summary>
    private const string SuspendedCode = "AccountMembershipSuspended";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The identity of acme, set when the host boots.</summary>
    private Guid _acme;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SuspendedAccountMemberEndpointTests(PostgresFixture postgres)
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

    /// <summary>An invited member who has not accepted yet is suspended with the account.</summary>
    [Fact]
    public async Task Suspending_the_account_suspends_the_membership_of_a_member_who_never_accepted()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "pending", accepted: false);

        await SuspendAsync(factory);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var states = await context.AccountMemberships
            .Where(row => row.AccountId == _acme)
            .Select(row => row.State)
            .ToListAsync();
        Assert.Equal(2, states.Count);
        Assert.All(states, state => { Assert.Equal(MembershipState.Suspended, state); });
    }

    /// <summary>A member who accepts their invitation after the suspension is still refused at sign-in.</summary>
    [Fact]
    public async Task A_member_who_accepts_after_the_suspension_is_refused_at_sign_in()
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "pending", accepted: false);
        await SuspendAsync(factory);
        await AcceptInvitationAsync(factory, memberId);
        using var client = factory.CreateClient();

        var refused = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "pending", Password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("InvalidCredentialsUnauthorized", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// A member's access token issued before the suspension is refused on a tenant endpoint with the
    /// suspension code, and the same token worked until then.
    /// </summary>
    [Fact]
    public async Task A_member_token_issued_before_the_suspension_is_refused_on_a_tenant_endpoint()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", accepted: true);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(TenantRoute)).StatusCode);

        await SuspendAsync(factory);

        var refused = await member.GetAsync(TenantRoute);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal(SuspendedCode, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>The owner's earlier token is refused the same way, and an administrator is not affected.</summary>
    [Fact]
    public async Task The_owner_token_is_refused_too_and_an_administrator_is_unaffected()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await SuspendAsync(factory);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(TenantRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/accounts")).StatusCode);
    }

    /// <summary>Resuming the account lets the member sign in and call the tenant endpoint again.</summary>
    [Fact]
    public async Task Resuming_the_account_restores_the_member_sign_in_and_the_tenant_endpoint()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", accepted: true);
        await SuspendAsync(factory);

        await ResumeAsync(factory);

        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(TenantRoute)).StatusCode);
    }

    /// <summary>Runs the real suspension subscriber for acme.</summary>
    /// <param name="factory">The booted host.</param>
    private async Task SuspendAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var handler = new AccountSuspendingHandler(
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<ISessionService>());

        await handler.HandleAsync(new AccountSuspending(_acme, "owner"), CancellationToken.None);
    }

    /// <summary>Runs the real resumption subscriber for acme.</summary>
    /// <param name="factory">The booted host.</param>
    private async Task ResumeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var handler = new AccountResumingHandler(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());

        await handler.HandleAsync(new AccountResuming(_acme, "owner"), CancellationToken.None);
    }

    /// <summary>Stores a member of acme, either still invited or already holding a password.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The member's login name.</param>
    /// <param name="accepted">Whether the invitation has been accepted.</param>
    /// <returns>The member's login id.</returns>
    private async Task<Guid> SeedMemberAsync(WebApplicationFactory<Program> factory, string username, bool accepted)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var member = User.Invite(Guid.NewGuid(), username, $"{username}@example.com", now);
        if (accepted)
        {
            member.Activate(hasher.Hash(EndpointHarness.Password));
        }

        context.Users.Add(member);
        context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, _acme, AccountFeature.None, now));
        await context.SaveChangesAsync();

        return member.Id;
    }

    /// <summary>Accepts a stored invitation: the login gets its password and becomes usable.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="userId">The invited login.</param>
    private static async Task AcceptInvitationAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var member = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        member.Activate(hasher.Hash(EndpointHarness.Password));
        await context.SaveChangesAsync();
    }

    /// <summary>Boots the host with an administrator and an owner of acme.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(new RecordingAuditWriter());
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "owner", acme);
        _acme = acme;

        return factory;
    }
}
