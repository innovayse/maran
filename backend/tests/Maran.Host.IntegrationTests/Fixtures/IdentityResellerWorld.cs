using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.Middleware;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the identity-side reseller tests run against (issues #179, #314, #317): an administrator,
/// resellers with their own logins, accounts owned by a reseller with their customer logins, and the
/// helpers that sign in, start a view-as session and read the journal.
/// </summary>
/// <remarks>
/// <para>
/// Everything is stored through the module contexts, the way a state the panel reached by itself would
/// be, so a test starts from a world and exercises the endpoint under test over real HTTP. A reseller
/// created through the administrator's endpoint is the one place a test goes through the shipped
/// subscriber instead (<c>ResellerSignInEndpointTests</c>).
/// </para>
/// <para>
/// Every login has the password <see cref="EndpointHarness.Password"/> and the address
/// <c>&lt;username&gt;@example.com</c>. A reseller's name is also its login name, which is how the panel
/// names a reseller's login.
/// </para>
/// <para>
/// UNOBSERVED HERE: the mail transport. The recording mailer stands in for the SMTP server.
/// </para>
/// </remarks>
public static class IdentityResellerWorld
{
    /// <summary>
    /// Boots the host, applies the migrations, stores the administrator <c>admin</c> and, when asked, the
    /// security policy row that decides whether a reseller must hold a second factor.
    /// </summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="forceTwoFactorForResellers">
    /// The policy's reseller switch, or <c>null</c> to store no policy row at all, so the defaults apply
    /// (which force a reseller to enrol).
    /// </param>
    /// <param name="configure">Extra service replacements for the test, or <c>null</c>.</param>
    /// <param name="panelUrl">
    /// The panel's public address, or <c>null</c>. A configured address makes an invitation carry a link with
    /// <c>token=</c>, which is the shape the person receives on an installed panel.
    /// </param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    public static async Task<WebApplicationFactory<Program>> BootAsync(
        TestDatabase database,
        bool? forceTwoFactorForResellers = false,
        Action<IServiceCollection>? configure = null,
        string? panelUrl = null)
    {
        var factory = EndpointHarness.CreateFactory(database, configure);
        if (panelUrl is not null)
        {
            factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PasswordReset:PanelUrl", panelUrl);
            });
        }

        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        if (forceTwoFactorForResellers is { } forced)
        {
            await SavePolicyAsync(factory, forceTwoFactorForAdmins: false, forced);
        }

        return factory;
    }

    /// <summary>Stores the security policy row with the two two-factor switches given.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="forceTwoFactorForAdmins">Whether administrators must hold a second factor.</param>
    /// <param name="forceTwoFactorForResellers">Whether resellers must hold a second factor.</param>
    public static async Task SavePolicyAsync(
        WebApplicationFactory<Program> factory,
        bool forceTwoFactorForAdmins,
        bool forceTwoFactorForResellers)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.SecurityPolicies.Add(new SecurityPolicy(
            SecurityPolicy.DefaultMinimumPasswordLength,
            forceTwoFactorForAdmins,
            forceTwoFactorForResellers,
            SecurityPolicy.DefaultMaxFailedLoginAttempts,
            SecurityPolicy.DefaultLockoutMinutes,
            now));
        await identity.SaveChangesAsync();
    }

    /// <summary>Stores a reseller with a generous envelope and its active login.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="name">The reseller's name, which is also its login name.</param>
    /// <returns>The reseller and its login.</returns>
    public static async Task<SeededReseller> SeedResellerAsync(WebApplicationFactory<Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var envelope = ResellerEnvelope.Of(
            10, 100_000, 10, 20_000, 10, 10, 10, 10, 10, 10, AccountFeature.All, RuntimeLimits.Unlimited());
        var reseller = new Reseller(Guid.NewGuid(), name, $"{name}@example.com", "en", envelope, now);
        accounts.Resellers.Add(reseller);
        await accounts.SaveChangesAsync();

        var login = User.InviteReseller(Guid.NewGuid(), name, $"{name}@example.com", reseller.Id, now);
        login.Activate(hasher.Hash(EndpointHarness.Password));
        identity.Users.Add(login);
        await identity.SaveChangesAsync();

        return new SeededReseller(reseller.Id, login.Id, name);
    }

    /// <summary>
    /// Stores an account owned by a reseller, on a plan the reseller owns, with an active customer login
    /// that owns it.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="resellerId">The owning reseller.</param>
    /// <param name="accountName">The account's name; the plan is named after it.</param>
    /// <param name="ownerUsername">The customer login that owns the account.</param>
    /// <returns>The account's id.</returns>
    public static async Task<Guid> SeedResellerAccountAsync(
        WebApplicationFactory<Program> factory,
        Guid resellerId,
        string accountName,
        string ownerUsername)
    {
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

            var plan = Plan.Define(
                Guid.NewGuid(), $"{accountName}-plan", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.All, resellerId);
            var account = new Account(
                Guid.NewGuid(),
                accountName,
                $"{accountName}.example.com",
                plan.Id,
                now,
                $"{ownerUsername}@example.com",
                resellerId: resellerId);
            accounts.Plans.Add(plan);
            accounts.Accounts.Add(account);
            await accounts.SaveChangesAsync();
            accountId = account.Id;
        }

        await EndpointHarness.SeedCustomerAsync(factory, ownerUsername, accountId);
        return accountId;
    }

    /// <summary>Stores an administrator-owned account on the shared <c>Base</c> plan with its customer login.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    /// <param name="ownerUsername">The customer login that owns the account.</param>
    /// <returns>The account's id.</returns>
    public static async Task<Guid> SeedAdministratorAccountAsync(
        WebApplicationFactory<Program> factory,
        string accountName,
        string ownerUsername)
    {
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        await EndpointHarness.SeedCustomerAsync(factory, ownerUsername, accountId);
        return accountId;
    }

    /// <summary>Stores the world the view-as edge tests start from: see <see cref="ViewAsWorld"/>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identities the tests address.</returns>
    public static async Task<ViewAsWorld> SeedViewAsWorldAsync(WebApplicationFactory<Program> factory)
    {
        var nova = await SeedResellerAsync(factory, "nova");
        var orbit = await SeedResellerAsync(factory, "orbit");
        var acme = await SeedResellerAccountAsync(factory, nova.ResellerId, "acme", "olive");
        var globex = await SeedResellerAccountAsync(factory, orbit.ResellerId, "globex", "gus");
        var initech = await SeedAdministratorAccountAsync(factory, "initech", "ivan");

        return new ViewAsWorld(nova, orbit, acme, globex, initech);
    }

    /// <summary>Creates a client carrying a bearer token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accessToken">The token.</param>
    /// <returns>A client the caller disposes.</returns>
    public static HttpClient ClientWith(WebApplicationFactory<Program> factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Signs a login in and returns a client carrying both its access token and its refresh cookie.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>A client the caller disposes; it can refresh as the browser would.</returns>
    public static async Task<HttpClient> SignInWithCookieAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = username, Password = EndpointHarness.Password });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        var refreshToken = cookie.Split(';')[0].Split('=', 2)[1];

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={refreshToken}");

        // The header the SPA sets on every request; a cookie-bearing state change without it is refused.
        client.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");
        return client;
    }

    /// <summary>Starts viewing the panel as the owner of an account and returns a client carrying that token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="operatorClient">The operator's own client: an administrator, or the owning reseller.</param>
    /// <param name="accountId">The account to view as.</param>
    /// <returns>A client the caller disposes.</returns>
    public static async Task<HttpClient> StartViewingAccountAsync(
        WebApplicationFactory<Program> factory,
        HttpClient operatorClient,
        Guid accountId)
    {
        var response = await operatorClient.PostAsJsonAsync("/api/v1/view-as", new { AccountId = accountId });
        return await ViewingClientAsync(factory, response);
    }

    /// <summary>Starts viewing the panel as a reseller and returns a client carrying that token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="administrator">The administrator's own client.</param>
    /// <param name="resellerId">The reseller to view as.</param>
    /// <returns>A client the caller disposes.</returns>
    public static async Task<HttpClient> StartViewingResellerAsync(
        WebApplicationFactory<Program> factory,
        HttpClient administrator,
        Guid resellerId)
    {
        var response = await administrator.PostAsJsonAsync("/api/v1/view-as/reseller", new { ResellerId = resellerId });
        return await ViewingClientAsync(factory, response);
    }

    /// <summary>Reads the access token out of a successful start answer.</summary>
    /// <param name="response">The answer to a start request.</param>
    /// <returns>The view-as access token, compact-serialized.</returns>
    public static async Task<string> AccessTokenOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Reads a login's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>The login's id.</returns>
    public static async Task<Guid> UserIdAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.Users.AsNoTracking()
            .Where(candidate => candidate.Username == username)
            .Select(candidate => candidate.Id)
            .SingleAsync();
    }

    /// <summary>Reads an account's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    /// <returns>Its id.</returns>
    public static async Task<Guid> AccountIdAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return await accounts.Accounts.AsNoTracking()
            .Where(account => account.Name == accountName)
            .Select(account => account.Id)
            .SingleAsync();
    }

    /// <summary>Counts the view-as sessions ever issued.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The count.</returns>
    public static async Task<int> ViewAsSessionCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.Sessions.CountAsync(session => session.ViewAs != null);
    }

    /// <summary>Reads every journal entry of one action, oldest first.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="action">The action name.</param>
    /// <returns>The matching entries.</returns>
    public static async Task<IReadOnlyList<AuditEvent>> AuditEntriesAsync(
        WebApplicationFactory<Program> factory,
        string action)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AuditEvents.AsNoTracking()
            .Where(entry => entry.Action == action)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Turns a successful start answer into a client carrying its token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="response">The answer to a start request; it must be a success.</param>
    /// <returns>A client the caller disposes.</returns>
    private static async Task<HttpClient> ViewingClientAsync(
        WebApplicationFactory<Program> factory,
        HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return ClientWith(factory, await AccessTokenOfAsync(response));
    }
}
