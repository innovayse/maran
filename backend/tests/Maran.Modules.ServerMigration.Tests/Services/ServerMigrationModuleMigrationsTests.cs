using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>What ServerMigrationModuleMigrations promises the Host's migrate verb: which schema it brings up to date.</summary>
public sealed class ServerMigrationModuleMigrationsTests
{
    /// <summary>The service reports the schema of the module's context, which is the manifest's schema.</summary>
    [Fact]
    public void The_service_reports_the_schema_the_context_and_the_manifest_name()
    {
        var migrations = new ServerMigrationModuleMigrations();

        Assert.Equal(ServerMigrationDbContext.SchemaName, migrations.SchemaName);
        Assert.Equal(ServerMigrationManifest.Instance.SchemaName, migrations.SchemaName);
    }

    /// <summary>The module registers the service as the migrations contract, once, as a singleton, and its journal as scoped.</summary>
    [Fact]
    public void The_module_registers_its_migrations_and_its_journal()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=maran;Username=panel",
            })
            .Build();
        var services = new ServiceCollection();

        new ServerMigrationModule().ConfigureServices(services, configuration);

        var registration = Assert.Single(services, descriptor => { return descriptor.ServiceType == typeof(IModuleMigrations); });
        Assert.Equal(typeof(ServerMigrationModuleMigrations), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        var journal = Assert.Single(services, descriptor => { return descriptor.ServiceType == typeof(ServerMigrationAuditJournal); });
        Assert.Equal(ServiceLifetime.Scoped, journal.Lifetime);
    }

    /// <summary>A caller that passes no service provider is refused before any database is touched.</summary>
    [Fact]
    public async Task A_missing_service_provider_is_refused()
    {
        var migrations = new ServerMigrationModuleMigrations();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => { await migrations.MigrateAsync(null!, CancellationToken.None); });
        await Assert.ThrowsAsync<ArgumentNullException>(async () => { await migrations.CurrentMigrationAsync(null!, CancellationToken.None); });
    }
}
