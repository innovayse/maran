using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Cli.Fixtures;

/// <summary>
/// An <see cref="IModuleMigrations"/> that applies nothing real: it records that it was asked, and
/// moves its current migration to a scripted id when it is.
/// </summary>
internal sealed class RecordingModuleMigrations : IModuleMigrations
{
    /// <summary>The migration id reported after a successful run.</summary>
    private readonly string _afterMigrate;

    /// <summary>The migration id reported right now.</summary>
    private string _current;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="schemaName">The schema it claims to migrate.</param>
    /// <param name="current">The migration id reported before any run; empty for none.</param>
    /// <param name="afterMigrate">The migration id reported once <see cref="MigrateAsync"/> has run.</param>
    public RecordingModuleMigrations(string schemaName, string current, string afterMigrate)
    {
        SchemaName = schemaName;
        _current = current;
        _afterMigrate = afterMigrate;
    }

    /// <inheritdoc />
    public string SchemaName { get; }

    /// <summary>How many times <see cref="MigrateAsync"/> was called.</summary>
    public int MigrateCalls { get; private set; }

    /// <inheritdoc />
    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        MigrateCalls++;
        _current = _afterMigrate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        return Task.FromResult(_current);
    }
}
