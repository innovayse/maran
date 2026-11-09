using System.Resources;
using Maran.Samples.Hello.Persistence;
using Maran.Samples.Hello.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Samples.Hello;

/// <summary>
/// The sample module's entry point: registers its database context against the <c>hello</c> schema, the
/// migrations the panel's migrate verb runs, and the text its manifest names. Its one controller is
/// discovered by ASP.NET Core's controller model, so there is nothing to map here.
/// </summary>
public sealed class HelloModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Samples.Hello.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return HelloManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        services.AddDbContext<HelloDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Singleton and stateless: it opens a scope of its own per call, so the migrate verb can bring this
        // module's schema up to date through the root provider.
        services.AddSingleton<IModuleMigrations, HelloModuleMigrations>();

        // The pool the panel-wide text provider resolves the manifest's display keys against.
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(HelloModule).Assembly));
    }
}
