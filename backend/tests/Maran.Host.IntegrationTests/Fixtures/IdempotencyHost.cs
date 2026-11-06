using System.Net.Http.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The plumbing the idempotency and response-contract tests share: a host whose account, final-backup,
/// cron and SFTP agents are doubles, every module's schema migrated, two administrators to sign in as, and a
/// request builder that attaches an <c>Idempotency-Key</c>.
/// </summary>
public static class IdempotencyHost
{
    /// <summary>The login name of the second administrator, for tests of two callers using one key.</summary>
    public const string SecondAdministrator = "admin2";

    /// <summary>Boots the host against <paramref name="database"/> with the agent doubles in place.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="agent">The account agent double.</param>
    /// <param name="clock">A clock the test moves, or null for the real one.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    public static WebApplicationFactory<Program> CreateFactory(
        TestDatabase database,
        ScriptedAccountsAgent agent,
        IClock? clock = null)
    {
        return EndpointHarness.CreateFactory(database, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton<IAgentAccountsClient>(agent);

            // An account deletion takes a final backup first (spec §12); without a stand-in it reaches
            // the real gRPC client, which has no socket here.
            services.RemoveAll<IAgentBackupClient>();
            services.AddSingleton<IAgentBackupClient>(new StubAgentBackupClient());

            // A suspension and a reactivation are announced to every module that runs something for the
            // account; the Cron and Sftp subscribers each call their agent, which has no socket here.
            services.RemoveAll<IAgentCronClient>();
            services.AddSingleton<IAgentCronClient>(new StubAgentCronClient());
            services.RemoveAll<IAgentSftpClient>();
            services.AddSingleton<IAgentSftpClient>(new LockingSftpAgent());

            if (clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(clock);
            }
        });
    }

    /// <summary>Migrates every module, seeds the backup destination a deletion needs, and seeds both administrators.</summary>
    /// <param name="factory">The booted host.</param>
    public static async Task PrepareAsync(WebApplicationFactory<Program> factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
            await scope.ServiceProvider.GetRequiredService<DefaultBackupDestinationSeeder>()
                .SeedAsync(CancellationToken.None);
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedSecondAdministratorAsync(factory);
    }

    /// <summary>Builds a request, with an <c>Idempotency-Key</c> header when a key is given.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The request path.</param>
    /// <param name="key">The key to send as is, or null to send no header at all.</param>
    /// <param name="body">The JSON body, or null for none.</param>
    /// <returns>The request, which the caller sends.</returns>
    public static HttpRequestMessage Request(HttpMethod method, string url, string? key, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (key is not null)
        {
            // Not through Headers.Add: a deliberately malformed key must reach the server unjudged.
            request.Headers.TryAddWithoutValidation(IdempotencyHeaders.Key, key);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>A creation body whose only variable is the account name.</summary>
    /// <param name="name">The account name to ask for.</param>
    /// <param name="planId">An existing plan.</param>
    /// <returns>The body.</returns>
    public static object CreateBody(string name, Guid planId)
    {
        return new
        {
            Name = name,
            PrimaryDomain = $"{name}.example.com",
            PlanId = planId,
            OwnerEmail = "owner@example.com",
        };
    }

    /// <summary>Stores a second, smaller plan an account can be moved to.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The plan's id.</returns>
    public static async Task<Guid> SeedSmallPlanAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var plan = Plan.Define(Guid.NewGuid(), "Small", 2_048, 2, 1, 1, 0, 0, 4);
        context.Plans.Add(plan);
        await context.SaveChangesAsync();

        return plan.Id;
    }

    /// <summary>Counts the stored accounts of one name, whoever owns them.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="name">The account name.</param>
    /// <returns>The row count.</returns>
    public static async Task<int> StoredAccountsAsync(WebApplicationFactory<Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        return await context.Accounts.IgnoreQueryFilters().CountAsync(account => account.Name == name);
    }

    /// <summary>Reads every idempotency record the panel holds.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The records, oldest first.</returns>
    public static async Task<IReadOnlyList<IdempotencyRecord>> StoredKeysAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();

        return await context.IdempotencyRecords.AsNoTracking().OrderBy(record => record.CreatedAt).ToListAsync();
    }

    /// <summary>Creates an administrator named <see cref="SecondAdministrator"/>.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedSecondAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        context.Users.Add(new User(
            Guid.NewGuid(),
            SecondAdministrator,
            "admin2@example.com",
            hasher.Hash(EndpointHarness.Password),
            UserRole.Admin,
            now));
        await context.SaveChangesAsync();
    }
}
