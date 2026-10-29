using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The one PostgreSQL server this assembly's integration tests share, and a fresh database inside it
/// for each test that asks. The server is the one <see cref="PostgresTestTarget"/> names.
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
///
/// Which server to start is <see cref="PostgresTestTarget"/>'s question and whether the started one is
/// the one declared is <see cref="PostgresServerVersion"/>'s (issue #98); this fixture only starts the
/// container the target names and refuses to hand out a database when the server it got is another major.
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
    private readonly PostgreSqlContainer _container;

    /// <summary>Receives the one line naming the image and server version this run measured.</summary>
    private readonly IMessageSink _diagnostics;

    /// <summary>Distinguishes the databases handed out, so two tests never share one.</summary>
    private int _issued;

    /// <summary>The server this run was asked to measure.</summary>
    public PostgresTestTarget Target { get; }

    /// <summary>The started server's version; <see langword="null"/> until <see cref="InitializeAsync"/> has run.</summary>
    public PostgresServerVersion? ServerVersion { get; private set; }

    /// <summary>Reads the target from the environment and builds, without starting, its container.</summary>
    /// <param name="diagnostics">The sink xUnit injects for messages that belong in the run's log.</param>
    public PostgresFixture(IMessageSink diagnostics)
    {
        _diagnostics = diagnostics;
        Target = PostgresTestTarget.FromEnvironment();

        _container = new PostgreSqlBuilder(Target.Image)
            .WithCommand("-c", "max_connections=300")
            .Build();
    }

    /// <summary>
    /// Starts the server once, before any test in the collection runs, then reports which server it is
    /// and refuses to continue if it is not the declared major.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SHOW server_version_num", connection);
        var version = PostgresServerVersion.Parse((string)(await command.ExecuteScalarAsync() ?? ""));

        _diagnostics.OnMessage(new DiagnosticMessage(
            $"PostgresFixture: image {Target.Image}, server_version_num {version.Number}"));

        // The server was asked, not the image tag trusted: a tag can be retagged or a variable can fail
        // to reach the process, and a leg that measured the wrong server certifies nothing.
        version.Require(Target.ExpectedMajor);
        ServerVersion = version;
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
        // Release what previous tests' pools are still holding. Npgsql's pools are process-global and
        // keyed by connection string, and every test here gets a database of its own — so a pool is
        // dead the moment its test ends, but nothing tells Npgsql that and its connections stay open
        // against this one server. Left alone the assembly's open connection count only ever grows
        // with the number of tests, and the server refuses new ones with `53300: sorry, too many
        // clients already` in whichever test happens to be running when the ceiling is reached rather
        // than in the one that exhausted it. Clearing here — before a new database is handed out,
        // which is exactly once per test — bounds the total at what the running tests hold, and makes
        // the max_connections headroom above a fixed cost rather than a number that has to grow every
        // time the suite does. Safe for a connection in use: Npgsql closes a cleared connection when
        // it is returned, never under its owner.
        NpgsqlConnection.ClearAllPools();

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
