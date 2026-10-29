using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The plumbing the administrator-surface endpoint tests share: a host booted against a test's own
/// PostgreSQL, the migrations the installer applies before first boot, an administrator and customer
/// to sign in as, and a signed-in client. Kept in one place so a change to how the host is booted
/// for these tests is made once, not in every class that exercises an endpoint.
/// </summary>
public static class EndpointHarness
{
    /// <summary>The password every seeded login signs in with.</summary>
    public const string Password = "correct horse battery staple";

    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Boots the host against <paramref name="database"/>.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="configure">Extra service replacements for the test, or null for none.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    public static WebApplicationFactory<Program> CreateFactory(
        TestDatabase database,
        Action<IServiceCollection>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(database.GetConnectionString()))
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
                // The real SMTP mailer must never be reached from a test.
                services.AddSingleton<IMailer>(new RecordingMailer(TimeSpan.Zero));
                configure?.Invoke(services);
            });
        });
    }

    /// <summary>Applies the migrations of every module these endpoints touch.</summary>
    /// <param name="factory">The booted host.</param>
    public static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Creates an administrator named <c>admin</c>.</summary>
    /// <param name="factory">The booted host.</param>
    public static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        context.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        await context.SaveChangesAsync();
    }

    /// <summary>Creates an active customer login owning an account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The customer's login name.</param>
    /// <param name="accountId">The account the customer owns.</param>
    public static async Task SeedCustomerAsync(
        WebApplicationFactory<Program> factory,
        string username,
        Guid accountId)
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

    /// <summary>Stores a plan and an account on it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    /// <param name="plan">The plan to store, or null for the small operator plan named <c>Base</c>.</param>
    /// <returns>The plan's id and the account's id.</returns>
    public static async Task<(Guid PlanId, Guid AccountId)> SeedAccountAsync(
        WebApplicationFactory<Program> factory,
        string accountName,
        Plan? plan = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        // The default plan is shared by every account a test seeds without naming one: plan names are
        // unique, so a second "Base" would be refused by the database.
        var stored = plan
            ?? await context.Plans.SingleOrDefaultAsync(candidate => candidate.DisplayNameKey == "Base")
            ?? Plan.Define(Guid.NewGuid(), "Base", 5_120, 5, 2, 3, 0, 5, 5);
        if (!await context.Plans.AnyAsync(candidate => candidate.Id == stored.Id))
        {
            context.Plans.Add(stored);
        }

        var account = new Account(Guid.NewGuid(), accountName, $"{accountName}.example.com", stored.Id, now);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        return (stored.Id, account.Id);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>A client the caller disposes.</returns>
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
