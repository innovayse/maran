using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The real HTTP surface of an account's two edits — its plan and its owner address — and of the
/// invitation refusal for a suspended account (issue #216): what is stored, what the login and the
/// host receive, what is journalled, and that a customer is refused all of it.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountEditEndpointTests : IAsyncLifetime
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Everything the panel journals during a test.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountEditEndpointTests(PostgresFixture postgres)
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

    /// <summary>Moving an account to a smaller plan it fits stores the plan, pushes the quota and is journalled.</summary>
    [Fact]
    public async Task Moving_an_account_to_a_smaller_plan_it_fits_stores_the_plan_pushes_the_quota_and_is_journalled()
    {
        var agent = new FixedUsageAgentAccountsClient(usedBytes: 1_024 * Mebibyte);
        await using var factory = CreateFactory(agent);
        var (_, accountId) = await SeedAsync(factory);
        var small = await SeedPlanAsync(factory, Plan.Define(Guid.NewGuid(), "Small", 2_048, 2, 1, 1, 0, 0, 4));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync($"/api/v1/accounts/{accountId}/plan", new { PlanId = small });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Small", body.RootElement.GetProperty("planDisplayName").GetString());
        Assert.Equal([$"olive:{2_048 * Mebibyte}"], agent.QuotasSet);
        Assert.Equal(small, await StoredPlanIdAsync(factory, accountId));
        Assert.Contains(_audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AccountPlanChanged && entry.Succeeded;
        });
    }

    /// <summary>Moving an account to a plan its usage does not fit is refused with 409 and nothing is changed.</summary>
    [Fact]
    public async Task Moving_an_account_to_a_plan_its_usage_does_not_fit_is_refused_with_conflict_and_nothing_is_changed()
    {
        var agent = new FixedUsageAgentAccountsClient(usedBytes: 4_096 * Mebibyte);
        await using var factory = CreateFactory(agent);
        var (planId, accountId) = await SeedAsync(factory);
        var small = await SeedPlanAsync(factory, Plan.Define(Guid.NewGuid(), "Small", 2_048, 2, 1, 1, 0, 0, 4));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync($"/api/v1/accounts/{accountId}/plan", new { PlanId = small });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("AccountPlanQuotaBelowUsage", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(agent.QuotasSet);
        Assert.Equal(planId, await StoredPlanIdAsync(factory, accountId));
    }

    /// <summary>Changing the owner address moves the login's address too, so its mails follow it.</summary>
    [Fact]
    public async Task Changing_the_owner_address_moves_the_logins_address_too_so_its_mails_follow_it()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (_, accountId) = await SeedAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId, "olive", "old@example.com");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{accountId}/owner-email", new { OwnerEmail = "new@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var login = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .AsNoTracking()
            .SingleAsync(user => user.AccountId == accountId);
        Assert.Equal("new@example.com", login.Email);
        var account = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Accounts
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == accountId);
        Assert.Equal("new@example.com", account.OwnerEmail);
        var entry = Assert.Single(_audit.Entries, candidate =>
        {
            return candidate.Action == AuditActions.AccountOwnerEmailChanged;
        });
        Assert.Equal("olive", entry.Subject);
    }

    /// <summary>An owner address another login already holds is refused with 409 and changes nothing.</summary>
    [Fact]
    public async Task An_owner_address_another_login_already_holds_is_refused_with_conflict_and_changes_nothing()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (_, accountId) = await SeedAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId, "olive", "old@example.com");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        // admin@example.com is the administrator's own login address.
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{accountId}/owner-email", new { OwnerEmail = "admin@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("EmailTaken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var account = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Accounts
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == accountId);
        Assert.Equal(string.Empty, account.OwnerEmail);
    }

    /// <summary>A malformed owner address is refused with 400 before anything is asked.</summary>
    [Fact]
    public async Task A_malformed_owner_address_is_refused_with_bad_request()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (_, accountId) = await SeedAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{accountId}/owner-email", new { OwnerEmail = "Ops <ops@example.com>" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Both edits of an account that does not exist answer not found.</summary>
    [Fact]
    public async Task Both_edits_of_an_account_that_does_not_exist_answer_not_found()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (planId, _) = await SeedAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var missing = Guid.NewGuid();

        var plan = await admin.PutAsJsonAsync($"/api/v1/accounts/{missing}/plan", new { PlanId = planId });
        var email = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{missing}/owner-email", new { OwnerEmail = "new@example.com" });

        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, email.StatusCode);
    }

    /// <summary>A customer is refused both edits of their own account with 403, and nothing changes.</summary>
    [Fact]
    public async Task A_customer_is_refused_both_edits_of_their_own_account_and_nothing_changes()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (planId, accountId) = await SeedAsync(factory);
        var other = await SeedPlanAsync(factory, Plan.Define(Guid.NewGuid(), "Other", 5_120, 2, 1, 1, 0, 0, 4));
        await EndpointHarness.SeedCustomerAsync(factory, "olivecustomer", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "olivecustomer");

        var plan = await customer.PutAsJsonAsync($"/api/v1/accounts/{accountId}/plan", new { PlanId = other });
        var email = await customer.PutAsJsonAsync(
            $"/api/v1/accounts/{accountId}/owner-email", new { OwnerEmail = "mine@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, email.StatusCode);
        Assert.Equal(planId, await StoredPlanIdAsync(factory, accountId));
    }

    /// <summary>A suspended account's invitation can be neither linked nor resent, with 409.</summary>
    [Fact]
    public async Task A_suspended_accounts_invitation_can_be_neither_linked_nor_resent()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (_, accountId) = await SeedAsync(factory);
        var login = await SeedInvitedLoginAsync(factory, accountId, "olive", "old@example.com");
        await SuspendLoginAsync(factory, login);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var link = await admin.PostAsync($"/api/v1/invitations/{accountId}/link", null);
        var resend = await admin.PostAsync($"/api/v1/invitations/{accountId}/resend", null);

        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Contains("InvitationAccountSuspended", await link.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, resend.StatusCode);
        Assert.Contains("InvitationAccountSuspended", await resend.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>A suspended account with no login is refused an invitation with 409, and no login is created.</summary>
    [Fact]
    public async Task A_suspended_account_with_no_login_is_refused_an_invitation_and_no_login_is_created()
    {
        await using var factory = CreateFactory(new FixedUsageAgentAccountsClient(usedBytes: 0));
        var (_, accountId) = await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            (await accounts.Accounts.SingleAsync(account => account.Id == accountId)).Suspend();
            await accounts.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var link = await admin.PostAsync($"/api/v1/invitations/{accountId}/link", null);
        var resend = await admin.PostAsync($"/api/v1/invitations/{accountId}/resend", null);

        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Contains("InvitationAccountSuspended", await link.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, resend.StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .AnyAsync(user => user.AccountId == accountId));
    }

    /// <summary>Boots the host with the given agent double and this test's journal.</summary>
    /// <param name="agent">The agent double that owns the accounts' quotas.</param>
    private WebApplicationFactory<Program> CreateFactory(IAgentAccountsClient agent)
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton(agent);
            services.RemoveAll<IAuditWriter>();
            services.AddSingleton<IAuditWriter>(_audit);
        });
    }

    /// <summary>Migrates, seeds the administrator and an account <c>olive</c> on a 5 GB plan.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task<(Guid PlanId, Guid AccountId)> SeedAsync(WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        return await EndpointHarness.SeedAccountAsync(factory, "olive");
    }

    /// <summary>Stores one more plan and returns its id.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="plan">The plan to store.</param>
    private static async Task<Guid> SeedPlanAsync(WebApplicationFactory<Program> factory, Plan plan)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        context.Plans.Add(plan);
        await context.SaveChangesAsync();
        return plan.Id;
    }

    /// <summary>Stores an invited login owning the account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account the login owns.</param>
    /// <param name="username">The login name.</param>
    /// <param name="email">The login's address.</param>
    private static async Task<Guid> SeedInvitedLoginAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string username,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var login = User.Invite(Guid.NewGuid(), username, email, accountId, now);
        context.Users.Add(login);
        await context.SaveChangesAsync();
        return login.Id;
    }

    /// <summary>Marks a login's account suspended, exactly as the suspension cascade does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="loginId">The login to suspend.</param>
    private static async Task SuspendLoginAsync(WebApplicationFactory<Program> factory, Guid loginId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var login = await context.Users.SingleAsync(user => user.Id == loginId);
        login.Suspend();
        await context.SaveChangesAsync();
    }

    /// <summary>Reads the plan an account is stored on.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account.</param>
    private static async Task<Guid> StoredPlanIdAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Accounts
            .AsNoTracking()
            .SingleAsync(account => account.Id == accountId)).PlanId;
    }
}
