using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Parallel wrong passwords for one login, against real PostgreSQL with the real migrations: every
/// one of them is counted and the lockout engages at exactly the configured number (issue #469).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot live in the module's own project.</b> The protection is a row lock inside an
/// explicit transaction, and the in-memory provider the unit tests run on has neither, so a mutation
/// that removed the lock would leave every unit test green. It is observable only over independent
/// connections to one database, which is also what parallel panel requests are.
/// </para>
/// <para>
/// <b>How the test is made able to LOSE.</b> Before the fix every request loaded the same count and
/// wrote the same count plus one, so N simultaneous failures advanced it by one. The requests are
/// released together through a gate. The handler has no barrier inside it, so one run over the old
/// code could pass by luck; ten requests against the Argon2id verification time make that unlikely,
/// and the accepting controls below prove the lock does not simply refuse everything.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ParallelFailedLoginCountTests : IAsyncLifetime
{
    /// <summary>The panel's default lockout threshold, which the seeded policy row carries.</summary>
    private const int Threshold = 10;

    /// <summary>The login the requests are aimed at.</summary>
    private const string Username = "admin";

    /// <summary>The password the login really has.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ParallelFailedLoginCountTests(PostgresFixture postgres)
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

    /// <summary>Ten simultaneous wrong passwords are ten failures, and the account is locked.</summary>
    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted_and_lock_the_account_at_the_threshold()
    {
        await using var factory = await BootAsync();

        var statuses = await LoginTogetherAsync(factory, Threshold, "wrong password");

        Assert.All(statuses, status => { Assert.Equal(HttpStatusCode.Unauthorized, status); });
        var user = await ReadUserAsync(factory);
        Assert.Equal(Threshold, user.FailedLoginAttempts);
        Assert.NotNull(user.LockedUntil);
    }

    /// <summary>The inverse control: one failure fewer than the threshold counts exactly and does not lock.</summary>
    [Fact]
    public async Task Parallel_wrong_passwords_below_the_threshold_are_counted_exactly_and_do_not_lock()
    {
        await using var factory = await BootAsync();

        await LoginTogetherAsync(factory, Threshold - 1, "wrong password");

        var user = await ReadUserAsync(factory);
        Assert.Equal(Threshold - 1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    /// <summary>The second control: parallel CORRECT passwords record no failure and sign everyone in.</summary>
    [Fact]
    public async Task Parallel_correct_passwords_record_no_failure()
    {
        await using var factory = await BootAsync();

        var statuses = await LoginTogetherAsync(factory, Threshold, Password);

        Assert.All(statuses, status => { Assert.Equal(HttpStatusCode.OK, status); });
        var user = await ReadUserAsync(factory);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    /// <summary>Boots the host and seeds the login.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            // The per-address limiter and ban would answer some of the burst with 429 and hide the
            // per-login counter this test is about. The limiter's ceiling is 100 (RateLimitOptions
            // validates it at start-up), ten times the burst.
            builder.UseSetting("RateLimiting:LoginMaxAttempts", "100");
            builder.UseSetting("BruteForce:MaxFailuresPerAddress", "1000");

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        context.Users.Add(new User(
            Guid.NewGuid(), Username, "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
        await context.SaveChangesAsync();

        return factory;
    }

    /// <summary>Releases <paramref name="count"/> sign-ins for the login at once.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="count">How many requests.</param>
    /// <param name="password">The password every request presents.</param>
    /// <returns>The status each request answered.</returns>
    private static async Task<HttpStatusCode[]> LoginTogetherAsync(
        WebApplicationFactory<Program> factory,
        int count,
        string password)
    {
        var gate = new TaskCompletionSource();
        var requests = Enumerable.Range(0, count).Select(async _ =>
        {
            using var client = factory.CreateClient();
            await gate.Task;
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username, Password = password });
            return response.StatusCode;
        }).ToArray();
        gate.SetResult();

        return await Task.WhenAll(requests);
    }

    /// <summary>Reads the login as the database holds it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The stored row.</returns>
    private static async Task<User> ReadUserAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await context.Users.AsNoTracking().SingleAsync(user => user.Username == Username);
    }
}
