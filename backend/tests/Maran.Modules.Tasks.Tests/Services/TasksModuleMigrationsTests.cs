using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// What TasksModuleMigrations promises the Host's migrate verb: which schema it brings up to date, and that the
/// module offers it.
/// </summary>
public sealed class TasksModuleMigrationsTests
{
    /// <summary>The service reports the schema of the module's context, which is the manifest's schema.</summary>
    [Fact]
    public void The_service_reports_the_schema_the_context_and_the_manifest_name()
    {
        var migrations = new TasksModuleMigrations();

        Assert.Equal(TasksDbContext.SchemaName, migrations.SchemaName);
        Assert.Equal(TasksManifest.Instance.SchemaName, migrations.SchemaName);
    }

    /// <summary>The module registers the service as the migrations contract, once, as a singleton.</summary>
    [Fact]
    public void The_module_registers_the_service_as_its_migrations_contract()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=maran;Username=panel",
            })
            .Build();
        var services = new ServiceCollection();

        new TasksModule().ConfigureServices(services, configuration);

        var registration = Assert.Single(services, descriptor =>
        {
            return descriptor.ServiceType == typeof(IModuleMigrations);
        });
        Assert.Equal(typeof(TasksModuleMigrations), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    /// <summary>A caller that passes no service provider is refused before any database is touched.</summary>
    [Fact]
    public async Task A_missing_service_provider_is_refused()
    {
        var migrations = new TasksModuleMigrations();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await migrations.MigrateAsync(null!, CancellationToken.None);
        });
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await migrations.CurrentMigrationAsync(null!, CancellationToken.None);
        });
    }
}
