using Npgsql;
using Testcontainers.PostgreSql;

namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>One PostgreSQL server shared by the assembly, and a fresh database inside it for each demo host.</summary>
public sealed class DemoPostgres : IAsyncLifetime
{
    /// <summary>The image; the panel's floor is 13 and the default is 16 (rules/architecture.md).</summary>
    private const string Image = "postgres:16-alpine";

    /// <summary>The server.</summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    /// <summary>Distinguishes the databases handed out.</summary>
    private int _issued;

    /// <summary>Starts the server.</summary>
    /// <returns>Resolves once it accepts connections.</returns>
    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    /// <summary>Stops the server.</summary>
    /// <returns>Resolves once it is gone.</returns>
    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    /// <summary>Creates an empty database.</summary>
    /// <returns>A connection string reaching a database no other host uses.</returns>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"maran_demo_{Interlocked.Increment(ref _issued)}_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await create.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
