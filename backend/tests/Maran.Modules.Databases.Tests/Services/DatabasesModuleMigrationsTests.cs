using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Databases.Tests.Services;

/// <summary>What the Databases module's migrations service promises the Host's migrate verb.</summary>
public sealed class DatabasesModuleMigrationsTests
{
    /// <summary>Builds the container the module registers, as the Host would, without opening a database.</summary>
    /// <returns>A provider holding the module's registrations.</returns>
    private static ServiceProvider Provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=maran;Username=panel",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        new DatabasesModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>The service names the schema its context maps and its manifest declares, all one name.</summary>
    [Fact]
    public void The_schema_it_names_is_the_one_the_context_maps_and_the_manifest_declares()
    {
        var migrations = new DatabasesModuleMigrations();

        Assert.Equal(DatabasesDbContext.SchemaName, migrations.SchemaName);
        Assert.Equal(DatabasesManifest.Instance.SchemaName, migrations.SchemaName);
        Assert.Equal(DatabasesManifest.Instance.Id, migrations.SchemaName);
    }

    /// <summary>The module registers its service, so the Host can find it without knowing the module's types.</summary>
    /// <remarks>
    /// The wiring half of the check: a correct migrations service that nobody registered migrates nothing,
    /// and the Host's migrate verb would report success over a schema it never touched.
    /// </remarks>
    [Fact]
    public void The_module_registers_exactly_one_migrations_service_of_its_own()
    {
        using var provider = Provider();

        var registered = provider.GetServices<IModuleMigrations>().OfType<DatabasesModuleMigrations>().ToList();

        Assert.Single(registered);
    }
}
