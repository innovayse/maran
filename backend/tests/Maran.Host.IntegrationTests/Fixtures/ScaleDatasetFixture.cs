using Maran.Agent.Client.Interfaces;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// One panel booted over the scale dataset, shared by every test of the class that asks for it: the
/// seed is the expensive part, and the list endpoints it serves only read.
/// </summary>
/// <remarks>
/// <para>
/// A class fixture rather than a per-test seed because xUnit builds a new test-class instance per
/// case, and re-seeding tens of thousands of rows for each of them would cost minutes and measure
/// nothing more. Nothing a test does through it writes: every request is a GET, so the cases cannot
/// leak into one another, which is the property the "no shared mutable fixtures" rule
/// (rules/testing.md) exists to protect.
/// </para>
/// <para>
/// It lives on the assembly's one PostgreSQL server (<see cref="PostgresFixture"/>, injected by the
/// collection) in a database of its own, and the class using it joins the shared collection — so it
/// runs serially with every other database test and is never timed while another host is busy.
/// </para>
/// </remarks>
public sealed class ScaleDatasetFixture : IAsyncLifetime
{
    /// <summary>The fixture's own database on the shared server.</summary>
    private readonly TestDatabase _database;

    /// <summary>The booted panel, once <see cref="InitializeAsync"/> has run.</summary>
    private WebApplicationFactory<Program>? _factory;

    /// <summary>The administrator's signed-in client, once <see cref="InitializeAsync"/> has run.</summary>
    private HttpClient? _administrator;

    /// <summary>Binds to the shared server without creating anything yet.</summary>
    /// <param name="postgres">The PostgreSQL server the assembly shares.</param>
    public ScaleDatasetFixture(PostgresFixture postgres)
    {
        _database = new TestDatabase(postgres);
    }

    /// <summary>A client signed in as the administrator, who sees every row of every list.</summary>
    /// <returns>The client.</returns>
    public HttpClient Administrator()
    {
        return _administrator ?? throw new InvalidOperationException("The scale dataset has not been seeded.");
    }

    /// <summary>Creates the database, migrates every module, and writes the whole dataset.</summary>
    /// <returns>A task that completes when the panel is ready to be measured.</returns>
    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        // The one list that asks the agent — the account disk view — reaches a stub, so its figure is
        // the panel's share of the cost; the agent's walk of a home directory is not the panel's.
        _factory = EndpointHarness.CreateFactory(
            _database,
            services => { services.AddSingleton<IAgentMonitorClient>(new StubAgentMonitorClient()); })
            // A budget run sends some five thousand requests from one client in a few minutes; the
            // shipped per-account limit (300 a minute) would answer most of them 429, and a refusal is
            // not a measurement of the list.
            .WithWebHostBuilder(builder => { builder.UseSetting("RateLimiting:ApiPermitLimit", "100000"); });

        using (var scope = _factory.Services.CreateScope())
        {
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
        }

        await EndpointHarness.SeedAdministratorAsync(_factory);

        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var administratorId = await identity.Users
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();

            await ScaleDataset.SeedAsync(scope.ServiceProvider);
            await ScaleJournalDataset.SeedAsync(scope.ServiceProvider, administratorId);
        }

        _administrator = await EndpointHarness.SignInAsync(_factory, "admin");
    }

    /// <summary>Releases the client and the host.</summary>
    /// <returns>A task that completes when the host is disposed.</returns>
    public async Task DisposeAsync()
    {
        _administrator?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }
}
