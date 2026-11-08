using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Boots the panel for the live dashboard stream tests and reads the stream's events: the host, the
/// seeded logins, and a reader that stops when it has seen what a test asked for.
/// </summary>
/// <remarks>
/// Every read is bounded by a deadline token. A stream has no natural end, so a test that waited for
/// one would hang rather than fail, and a hang is blamed on the runner.
/// </remarks>
public static class DashboardStreamHost
{
    /// <summary>The second administrator seeded beside <c>admin</c>, who has a stream budget of their own.</summary>
    public const string SecondAdministrator = "admin2";

    /// <summary>The customer seeded, who owns one account.</summary>
    public const string Customer = "customer";

    /// <summary>The bound on every stream read, so a failure to arrive is a failed test and not a hang.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Boots the panel against <paramref name="database"/> with the agent and, optionally, the clock replaced.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="agent">The monitoring agent the feed polls.</param>
    /// <param name="clock">A clock the test moves, or <c>null</c> for the real one.</param>
    /// <param name="configure">Changes to the stream's bounds, or <c>null</c> for the defaults.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    public static WebApplicationFactory<Program> CreateFactory(
        TestDatabase database,
        IAgentMonitorClient agent,
        IClock? clock = null,
        Action<DashboardStreamOptions>? configure = null)
    {
        return EndpointHarness.CreateFactory(database, services =>
        {
            services.RemoveAll<IAgentMonitorClient>();
            services.AddSingleton(agent);

            if (clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(clock);
            }

            services.PostConfigure<DashboardStreamOptions>(options =>
            {
                // A short wake, so a lifetime end or a section refresh is noticed in a tenth of a second.
                options.TickMilliseconds = 100;
                configure?.Invoke(options);
            });
        });
    }

    /// <summary>Creates the schemas and seeds two administrators and one customer with an account.</summary>
    /// <param name="factory">The booted host.</param>
    public static async Task PrepareAsync(WebApplicationFactory<Program> factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedSecondAdministratorAsync(factory);

        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "own");
        await EndpointHarness.SeedCustomerAsync(factory, Customer, accountId);
    }

    /// <summary>Opens the stream and returns the response with its body still streaming.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="query">The query string including its leading <c>?</c>, or empty.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The response, headers read.</returns>
    public static async Task<HttpResponseMessage> OpenAsync(
        HttpClient client,
        string query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/stream" + query);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Reads events from an open stream until <paramref name="done"/> accepts them.</summary>
    /// <param name="response">An open stream.</param>
    /// <param name="done">Judges the events read so far; true stops the read.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The events read, in order.</returns>
    /// <remarks>For a test that reads once; one that reads on after a change uses <see cref="DashboardSseReader"/>.</remarks>
    public static async Task<IReadOnlyList<DashboardSseEvent>> ReadUntilAsync(
        HttpResponseMessage response,
        Func<IReadOnlyList<DashboardSseEvent>, bool> done,
        CancellationToken cancellationToken)
    {
        using var reader = await DashboardSseReader.OpenAsync(response, cancellationToken);
        return await reader.ReadUntilAsync(done, cancellationToken);
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
