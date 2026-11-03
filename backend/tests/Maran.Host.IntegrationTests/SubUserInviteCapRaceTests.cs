using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Many sub-user invitations from one owner in the same instant, against real PostgreSQL with the
/// real migrations applied: the account never ends up over its cap, and the requests that lose are
/// told the team is full.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test cannot live in the module's own project.</b> The protection is a row lock taken
/// inside an explicit transaction, and the in-memory provider the unit tests run on has neither, so
/// a mutation deleting the lock would leave every one of them green. It is observable only here, over
/// independent connections to one database, which is also what parallel panel requests are.
/// </para>
/// <para>
/// <b>How the test is made able to LOSE.</b> A dozen requests are released together against a cap of
/// three, so with no lock several of them read a team size under three before any of them has
/// written, and the row count overshoots. There is no barrier inside the handler to force the
/// interleaving (the window is a few statements wide), so a single run over an unprotected handler
/// can still pass by luck; the dozen-to-three ratio is what makes that unlikely, and the
/// accepting control below is what proves the lock does not simply refuse everything.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SubUserInviteCapRaceTests : IAsyncLifetime
{
    /// <summary>How many invitations are released together.</summary>
    private const int Parallel = 12;

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SubUserInviteCapRaceTests(PostgresFixture postgres)
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

    /// <summary>A dozen parallel invitations against a cap of three leave exactly three members.</summary>
    [Fact]
    public async Task Parallel_invitations_cannot_take_an_account_past_its_cap()
    {
        await using var factory = await BootAsync(cap: 3);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var statuses = await InviteTogetherAsync(owner);

        Assert.Equal(3, await MemberCountAsync(factory));
        Assert.Equal(3, statuses.Count(status => { return status == HttpStatusCode.Accepted; }));
        Assert.Equal(Parallel - 3, statuses.Count(status => { return status == HttpStatusCode.Conflict; }));
    }

    /// <summary>The inverse control: the same contention inside a cap that fits every request accepts all of them.</summary>
    [Fact]
    public async Task Parallel_invitations_inside_the_cap_are_all_accepted()
    {
        await using var factory = await BootAsync(cap: Parallel);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var statuses = await InviteTogetherAsync(owner);

        Assert.All(statuses, status => { Assert.Equal(HttpStatusCode.Accepted, status); });
        Assert.Equal(Parallel, await MemberCountAsync(factory));
    }

    /// <summary>
    /// Parallel requests for names that are all taken leave no decoy behind: the team stays empty and
    /// every request is answered as an honoured one.
    /// </summary>
    [Fact]
    public async Task Parallel_requests_for_a_taken_name_leave_no_decoy_login_behind()
    {
        await using var factory = await BootAsync(cap: 3);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var tokensBefore = await TokenCountAsync(factory);
        var usersBefore = await UserCountAsync(factory);

        var statuses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(async index =>
        {
            var response = await owner.PostAsJsonAsync(
                "/api/v1/me/sub-users",
                new { Username = "owner-b", Email = $"probe{index}@example.com", Features = Array.Empty<string>() });
            return response.StatusCode;
        }));

        Assert.All(statuses, status => { Assert.Equal(HttpStatusCode.Accepted, status); });
        Assert.Equal(0, await MemberCountAsync(factory));
        Assert.Equal(tokensBefore, await TokenCountAsync(factory));
        Assert.Equal(usersBefore, await UserCountAsync(factory));
    }

    /// <summary>Releases <see cref="Parallel"/> invitations for distinct people at once.</summary>
    /// <param name="owner">The signed-in owner's client.</param>
    /// <returns>The status each request answered.</returns>
    private static async Task<HttpStatusCode[]> InviteTogetherAsync(HttpClient owner)
    {
        var gate = new TaskCompletionSource();
        var requests = Enumerable.Range(0, Parallel).Select(async index =>
        {
            await gate.Task;
            var response = await owner.PostAsJsonAsync(
                "/api/v1/me/sub-users",
                new { Username = $"member{index}", Email = $"member{index}@example.com", Features = Array.Empty<string>() });
            return response.StatusCode;
        }).ToArray();
        gate.SetResult();

        return await Task.WhenAll(requests);
    }

    /// <summary>Counts the members (not the owner) the first account holds.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The number of member memberships stored.</returns>
    private static async Task<int> MemberCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AccountMemberships.CountAsync(membership => membership.Role == MembershipRole.Member);
    }

    /// <summary>Counts every invitation token stored.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The number of token rows.</returns>
    private static async Task<int> TokenCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.InvitationTokens.CountAsync();
    }

    /// <summary>Counts every login stored.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The number of login rows.</returns>
    private static async Task<int> UserCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.Users.CountAsync();
    }

    /// <summary>Boots the panel with two accounts and an invitation cap of <paramref name="cap"/>.</summary>
    /// <param name="cap">How many sub-users one account may hold.</param>
    /// <returns>The running host.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync(int cap)
    {
        var factory = EndpointHarness.CreateFactory(
                _pg,
                services =>
                {
                    services.AddSingleton<IMailer>(new RecordingMailer(TimeSpan.Zero));
                })
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("SubUsers:MaxPerAccount", cap.ToString(CultureInfo.InvariantCulture));
                builder.UseSetting("RateLimiting:InvitationSendMaxRequests", "1000");
            });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountA) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, accountB) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", accountA);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", accountB);

        return factory;
    }
}
