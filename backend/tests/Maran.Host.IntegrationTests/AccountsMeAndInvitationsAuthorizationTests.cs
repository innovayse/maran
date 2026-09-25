using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The surface Task 13 of the customer-area plan added, over real HTTP against a real database:
/// a customer's own account read, the administrator's invitation-resend action, and the anonymous
/// endpoint that spends an invitation token. None of it was covered by the pre-existing per-module
/// authorization suites — <c>AccountsAuthorizationTests</c> proves the tenant-list module is closed
/// to a customer entirely, and says nothing about the one endpoint a customer IS meant to reach.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>GET /api/v1/accounts/me</c> carries no identifier to forge.</b> The account read comes off
/// the caller's own token
/// (<see cref="Maran.Modules.Accounts.Queries.GetMyAccount.GetMyAccountQueryHandler"/>'s own remarks), so there is no
/// "customer A asks for customer B's account" version of this endpoint — a customer cannot even
/// address a second account by id. The IDOR-shaped question that DOES apply is the one this file
/// asks: two different customers, each reading <c>/me</c> in the same test run, must each see their
/// own account and never see the other's, and it is asserted on the account identifier and the
/// domain BOTH, not merely on "the call succeeded".
/// </para>
/// <para>
/// <b><c>POST /api/v1/invitations/{accountId}/resend</c> is administrator-only, not tenant-scoped,
/// and 404-not-403 does not apply to it.</b> <c>rules/security.md</c> item 6 states the rule for a
/// resource a tenant does or does not own; this route is gated by <c>[Authorize(AdminOnly)]</c> at
/// the controller, which ASP.NET Core evaluates before the action — and therefore before anything
/// reads the route's <c>accountId</c> — runs at all. A customer is refused identically whether the
/// id in the URL is their own account or a stranger's, because the policy never looks at it, and the
/// right refusal is <see cref="HttpStatusCode.Forbidden"/>: an authenticated caller lacking a role is
/// exactly what 403 means, and there is no existence fact about "this account" to protect by lying
/// with a 404 — the existence is not the secret an administrator-only gate exists to hide.
/// </para>
/// <para>
/// <b>The accept-invitation refusal is proved over the wire, not only in the handler.</b>
/// <c>AcceptInvitationCommandHandlerTests</c>, alongside the other Identity unit
/// tests, and the handler's own remarks establish that an unknown, expired and spent token compare
/// <c>Equal</c> as <see cref="Maran.SharedKernel.Results.Error"/> values; this file is the proof that the
/// same equality survives serialization onto an HTTP response, over the real ASP.NET pipeline, the
/// same way <c>PasswordResetEndpointTests</c> proves it for the sibling reset-password endpoint.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountsMeAndInvitationsAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>
    /// Swapped in for the real SMTP mailer, so a resend's mail publish never tries a real outbound
    /// connection: <c>MailQueueDecouplingTests</c> establishes that the send is decoupled from the
    /// request, and this class is not the one proving that again.
    /// </summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountsMeAndInvitationsAuthorizationTests(PostgresFixture postgres)
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

    // ------------------------------------------------------------------------------------------
    // GET /api/v1/accounts/me
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Two different customers, reading <c>/me</c> in the same run, each get their own account back
    /// and never the other's.
    /// </summary>
    [Fact]
    public async Task Two_customers_reading_their_own_account_each_get_their_own_and_never_the_others()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (firstAccountId, secondAccountId) = await SeedTwoAccountsAsync(factory);
        await SeedCustomerAsync(factory, "olive", firstAccountId);
        await SeedCustomerAsync(factory, "pepper", secondAccountId);

        using var firstClient = await SignInAsync(factory, "olive");
        using var secondClient = await SignInAsync(factory, "pepper");

        var firstResponse = await firstClient.GetAsync("/api/v1/accounts/me");
        var secondResponse = await secondClient.GetAsync("/api/v1/accounts/me");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondBody = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());

        Assert.Equal(firstAccountId, firstBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("olive.example.com", firstBody.RootElement.GetProperty("primaryDomain").GetString());

        Assert.Equal(secondAccountId, secondBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("pepper.example.com", secondBody.RootElement.GetProperty("primaryDomain").GetString());

        // The negative half, stated explicitly rather than left to be inferred from the positive
        // one: neither response names the OTHER customer's account at all.
        Assert.NotEqual(secondAccountId, firstBody.RootElement.GetProperty("id").GetGuid());
        Assert.NotEqual(firstAccountId, secondBody.RootElement.GetProperty("id").GetGuid());
    }

    /// <summary>An administrator, owning no account, gets the same 404 as anybody else without one.</summary>
    [Fact]
    public async Task An_administrator_reading_my_account_gets_not_found_because_they_own_none()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAdministratorAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/accounts/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An anonymous caller is refused before the question of ownership is even asked.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_my_account()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/accounts/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------------
    // POST /api/v1/invitations/{accountId}/resend
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A customer calling resend for ANY account — including their own — is refused with 403, the
    /// answer an administrator-only route gives a caller who lacks the role, never a 404: there is
    /// no existence fact about the account for a 404 to protect here, because the gate is the
    /// caller's role and runs before the route's id is ever read.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_customer_calling_resend_is_refused_with_forbidden_whichever_account_they_name(
        bool ownAccount)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (ownAccountId, strangerAccountId) = await SeedTwoAccountsAsync(factory);
        await SeedCustomerAsync(factory, "olive", ownAccountId);

        using var client = await SignInAsync(factory, "olive");
        var target = ownAccount ? ownAccountId : strangerAccountId;

        var response = await client.PostAsJsonAsync($"/api/v1/invitations/{target}/resend", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator may resend an outstanding invitation, and the token is renewed.</summary>
    [Fact]
    public async Task An_administrator_may_resend_an_outstanding_invitation()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (accountId, _) = await SeedTwoAccountsAsync(factory);
        await SeedAdministratorAsync(factory);
        var userId = await SeedInvitedLoginAsync(factory, accountId);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.PostAsJsonAsync($"/api/v1/invitations/{accountId}/resend", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var outstanding = await identity.InvitationTokens
            .Where(token => token.UserId == userId && token.UsedAt == null)
            .ToListAsync();
        Assert.Single(outstanding);
    }

    /// <summary>An anonymous caller is refused before the role check ever runs.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_resend()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (accountId, _) = await SeedTwoAccountsAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/invitations/{accountId}/resend", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------------
    // POST /api/v1/auth/accept-invitation
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// An unknown, an expired, and an already-spent invitation token are refused with the identical
    /// status and body over HTTP — the wire-level proof beside the handler's own equality assertion.
    /// </summary>
    [Fact]
    public async Task An_unknown_an_expired_and_a_spent_invitation_token_are_refused_identically_over_http()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (accountId, _) = await SeedTwoAccountsAsync(factory);
        var userId = await SeedInvitedLoginAsync(factory, accountId);
        using var client = factory.CreateClient();

        var spentToken = InvitationTokenHasher.Generate();
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            identity.InvitationTokens.Add(new InvitationToken(
                Guid.NewGuid(), userId, InvitationTokenHasher.Hash(spentToken), clock.UtcNow));
            await identity.SaveChangesAsync();
        }

        // Spend it once, exactly as the customer who received it would.
        await client.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = spentToken, NewPassword = Password });

        var expiredToken = InvitationTokenHasher.Generate();
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            identity.InvitationTokens.Add(new InvitationToken(
                Guid.NewGuid(),
                userId,
                InvitationTokenHasher.Hash(expiredToken),
                clock.UtcNow - InvitationToken.Lifetime - TimeSpan.FromMinutes(1)));
            await identity.SaveChangesAsync();
        }

        var refusals = new List<(HttpStatusCode Status, string Body)>();
        foreach (var candidate in new[] { spentToken, expiredToken, "a-token-nobody-ever-issued" })
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/accept-invitation", new { Token = candidate, NewPassword = Password });
            refusals.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.Equal(HttpStatusCode.BadRequest, refusals[0].Status);
        Assert.All(refusals, refusal =>
        {
            Assert.Equal(refusals[0].Status, refusal.Status);
            Assert.Equal(WithoutCorrelationId(refusals[0].Body), WithoutCorrelationId(refusal.Body));
        });
    }

    /// <summary>A fresh, unspent, unexpired token is accepted and makes the login usable.</summary>
    [Fact]
    public async Task A_fresh_invitation_token_is_accepted_and_the_login_can_then_sign_in()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (accountId, _) = await SeedTwoAccountsAsync(factory);
        var userId = await SeedInvitedLoginAsync(factory, accountId);
        using var client = factory.CreateClient();

        var token = InvitationTokenHasher.Generate();
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            identity.InvitationTokens.Add(new InvitationToken(
                Guid.NewGuid(), userId, InvitationTokenHasher.Hash(token), clock.UtcNow));
            await identity.SaveChangesAsync();
        }

        var accepted = await client.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "invited-owner", Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ------------------------------------------------------------------------------------------
    // Fixture plumbing
    // ------------------------------------------------------------------------------------------

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailer>(_mailer);
            });
        });
    }

    /// <summary>Applies both modules' migrations, the way the installer does before first boot.</summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();

        // The resend endpoint publishes the invitation mail on the bus, which needs the
        // Notifications module's own schema (its durable outbox) migrated, exactly as
        // PasswordResetEndpointTests migrates it for the sibling reset-password endpoint.
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Creates two accounts on one plan, and returns both their ids.</summary>
    private static async Task<(Guid First, Guid Second)> SeedTwoAccountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        context.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));

        var first = new Account(Guid.NewGuid(), "olive", "olive.example.com", planId, now);
        var second = new Account(Guid.NewGuid(), "pepper", "pepper.example.com", planId, now);
        context.Accounts.AddRange(first, second);
        await context.SaveChangesAsync();

        return (first.Id, second.Id);
    }

    /// <summary>Creates an administrator with the fixed username <c>admin</c>.</summary>
    private static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        context.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        await context.SaveChangesAsync();
    }

    /// <summary>Creates an already-active customer login owning <paramref name="accountId"/>.</summary>
    private static async Task SeedCustomerAsync(
        WebApplicationFactory<Program> factory, string username, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var customer = new User(
            Guid.NewGuid(), username, $"{username}@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(accountId);
        context.Users.Add(customer);
        await context.SaveChangesAsync();
    }

    /// <summary>Creates an invited (passwordless) login owning <paramref name="accountId"/>.</summary>
    /// <returns>The login's identity.</returns>
    private static async Task<Guid> SeedInvitedLoginAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var invited = User.Invite(Guid.NewGuid(), "invited-owner", "invited-owner@example.com", accountId, now);
        context.Users.Add(invited);
        await context.SaveChangesAsync();

        return invited.Id;
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Strips the per-request correlation id, which is not part of what an answer says.</summary>
    /// <param name="body">The response body.</param>
    /// <returns>The body with the correlation id replaced by a fixed marker.</returns>
    private static string WithoutCorrelationId(string body)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            body,
            "\"correlationId\":\"[^\"]*\"",
            "\"correlationId\":\"-\"",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));
    }
}
