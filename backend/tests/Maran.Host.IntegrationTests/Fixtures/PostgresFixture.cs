using Npgsql;
using Testcontainers.PostgreSql;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The one PostgreSQL server this assembly's integration tests share, and a fresh database inside it
/// for each test that asks.
/// </summary>
/// <remarks>
/// It exists because the shape it replaces was quietly costing minutes and producing a flaky suite.
/// Every integration class held its own <c>PostgreSqlContainer</c> in an instance field under
/// <c>IAsyncLifetime</c>, and xUnit constructs a NEW INSTANCE of a test class for every test method —
/// so a container was started and destroyed per test, not per class, on the order of ninety container
/// lifecycles for this assembly alone. That was not a reading of the code but a fact forced by it:
/// <c>IX_Users_Username</c> is unique and every one of those tests seeds a user named <c>admin</c>
/// into the container's single database, so a container genuinely shared across a class would fail on
/// its second test. The suite was green, so nothing was shared.
///
/// The cost was Docker, memory and Argon2id all saturating at once, which is what made an unrelated
/// authentication test fail under load while passing alone. A flaky test is a P1 (rules/testing.md),
/// and the fix is the same one that makes the suite fast: one server for the assembly, and a database
/// per test so the seeds cannot collide. The isolation each test had is preserved exactly — a fresh,
/// empty database with no migrations applied — and only the server is shared.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// The server every test in this assembly runs against.
    /// </summary>
    /// <remarks>
    /// <c>max_connections</c> is raised from the image's default of 100. Every test here gets its
    /// own database on this one server, and each database is a separate connection string and
    /// therefore a separate Npgsql pool that holds its connections until the host that owns it is
    /// disposed — so the assembly's peak is a function of how many tests it has, not of how much
    /// work any of them does. At the default the suite sat just under the ceiling and adding two
    /// tests pushed it over, which surfaces as <c>53300: sorry, too many clients already</c> in
    /// whichever unrelated test happened to run at the moment the limit was reached. A limit that
    /// fails a test other than the one that exhausted it is a limit worth having headroom on.
    /// </remarks>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithCommand("-c", "max_connections=300")
        .Build();

    /// <summary>Distinguishes the databases handed out, so two tests never share one.</summary>
    private int _issued;

    /// <summary>Starts the server once, before any test in the collection runs.</summary>
    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    /// <summary>Stops the server after the last test in the collection.</summary>
    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    /// <summary>Creates an empty database and returns the connection string that reaches it.</summary>
    /// <returns>A connection string for a database no other test uses.</returns>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"maran_test_{Interlocked.Increment(ref _issued)}_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();

            // Quoted, and the name is built here from a counter and a GUID rather than from anything
            // a test supplies, because CREATE DATABASE takes no parameters.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
