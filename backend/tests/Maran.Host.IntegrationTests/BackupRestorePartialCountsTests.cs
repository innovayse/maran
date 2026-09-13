using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A partial restore's measured counts on the real wire: the composed panel answers the failure
/// with a problem response whose <c>restore</c> extension carries exactly what the agent said it
/// had replaced — and an ending that measured nothing carries no such member at all.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the whole convention can be observed as the SPA observes it: handler →
/// <c>Result&lt;T&gt;.Fail(error, extension)</c> → <c>ApiResultExtensions</c> → MVC's serializer →
/// the JSON body. The unit tests prove each link; a camelCase policy dropped from the Host's
/// serializer, or an MVC problem converter that ignores extension data, is visible only here.
/// </para>
/// <para>
/// The agent is stubbed at its seam (<see cref="PartialRestoreAgentBackupClient"/>) because a
/// partial restore is unreachable through the real agent — it rolls back and reports failure — so
/// the stub states the terminal outcome whose figures do not add up, which is the contract's
/// partial shape whether or not today's agent can produce it.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupRestorePartialCountsTests : IAsyncLifetime
{
    /// <summary>The password every seeded login is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A base64 key long enough for the host's startup validation of both key settings.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The system user name of the seeded account, and the confirmation a restore must type.</summary>
    private const string AccountName = "own";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupRestorePartialCountsTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>The task that creates this class's database.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>A completed task; the shared server outlives this class.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A partial restore answers its measured counts in the problem's restore extension.</summary>
    [Fact]
    public async Task A_partial_restore_answers_the_measured_counts_on_the_wire()
    {
        // One of two databases loaded, files already swapped: the exact partial the operator must
        // be able to read off the failure.
        await using var factory = CreateFactory(new AgentRestoreOutcome(true, 1, 2));
        await MigrateAsync(factory);
        var backupId = await SeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/backups/{backupId}/restore", new { confirmAccountUsername = AccountName });

        // RestorePartial is ErrorType.Failure: the SERVER left the account half-replaced.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RestorePartial", body.RootElement.GetProperty("code").GetString());

        // The convention's wire shape: a TOP-LEVEL member named for the extension, its payload
        // serialized camelCase — exactly what the SPA's error decoding hands the dialog.
        var counts = body.RootElement.GetProperty("restore");
        Assert.True(counts.GetProperty("filesRestored").GetBoolean());
        Assert.Equal(1u, counts.GetProperty("databasesRestored").GetUInt32());
        Assert.Equal(2u, counts.GetProperty("databasesTotal").GetUInt32());
    }

    /// <summary>A refusal that touched nothing answers no restore member at all.</summary>
    /// <remarks>
    /// The "never 0 of 0" half, on the wire. A wrong confirmation is refused before the agent is
    /// asked, so there are no measurements — and a problem body carrying zeros there would tell an
    /// operator that nothing was replaced when the truth is that nothing was attempted. The partial
    /// test above is this one's positive control: same route, same seed, and the member IS present
    /// when the agent measured.
    /// </remarks>
    [Fact]
    public async Task A_refused_restore_answers_no_counts_member()
    {
        await using var factory = CreateFactory(new AgentRestoreOutcome(true, 1, 2));
        await MigrateAsync(factory);
        var backupId = await SeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/backups/{backupId}/restore", new { confirmAccountUsername = "somebody-else" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RestoreConfirmationMismatch", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("restore", out _));
    }

    /// <summary>Applies every module's migrations the restore path writes through, as the installer does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The task that migrates them.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one account, its customer login, a default destination and one completed backup.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifier of the completed backup the tests restore from.</returns>
    private static async Task<Guid> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var own = new Account(Guid.NewGuid(), AccountName, "own.example.com", planId, now);
        accounts.Accounts.Add(own);
        await accounts.SaveChangesAsync();

        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        // The restore resolves the row's null destination to the server's default; the seeder that
        // writes one in production may or may not have run in this booted host, so the seed
        // guarantees exactly one default exists rather than assuming either answer.
        if (!await backups.BackupDestinations.AnyAsync(row => row.IsDefault))
        {
            backups.BackupDestinations.Add(new BackupDestination(
                Guid.NewGuid(), "Local storage", BackupDestinationKind.Local, string.Empty, isDefault: true, now));
        }

        var backup = new Backup(Guid.NewGuid(), own.Id, destinationId: null, BackupKind.Manual, now);
        backup.Completed(4096, new string('a', 64), 1, now);
        backups.Backups.Add(backup);
        await backups.SaveChangesAsync();

        return backup.Id;
    }

    /// <summary>Signs the seeded customer in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A client authenticated as the account's customer.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "customer", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent's backup seam scripted.</summary>
    /// <param name="outcome">The terminal outcome every restore stream will state.</param>
    /// <returns>The booted factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(AgentRestoreOutcome outcome)
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
                // Only the agent is replaced, and only because it cannot be present: it is a
                // separate root process. Everything else in the request path is the real panel.
                services.RemoveAll<IAgentBackupClient>();
                services.AddSingleton<IAgentBackupClient>(new PartialRestoreAgentBackupClient(outcome));
            });
        });
    }
}
