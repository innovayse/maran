using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Maran.Modules.Databases.Persistence;

/// <summary>
/// Lets EF Core design-time tooling (<c>dotnet ef migrations add</c>, <c>database update</c>)
/// construct <see cref="DatabasesDbContext"/> without booting the Host. Never used at runtime — the
/// Host registers the context through DI instead.
/// </summary>
/// <remarks>
/// EF prefers this factory over the startup project whenever it exists, so a hard-coded connection
/// string here silently wins over the developer's real settings: `database update` then reported an
/// authentication failure against a server nobody had configured. The settings are therefore read
/// from the same environment the panel reads, and only the shape of the schema — never a live
/// database — is required for generating a migration.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DatabasesDbContext>
{
    /// <summary>Environment variable prefix ASP.NET Core uses for configuration overrides.</summary>
    private const string EnvironmentVariablePrefix = "";

    /// <summary>Builds a context from the developer's configuration.</summary>
    /// <param name="args">Unused; required by the <see cref="IDesignTimeDbContextFactory{TContext}"/> contract.</param>
    public DatabasesDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(HostSettingsPath("appsettings.json"), optional: true)
            .AddJsonFile(HostSettingsPath("appsettings.Development.json"), optional: true)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<DatabasesDbContext>();
        optionsBuilder.UseNpgsql(BuildConnectionString(configuration));
        return new DatabasesDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }

    /// <summary>
    /// Resolves a Host settings file relative to this project, because EF runs the tooling with the
    /// module project as its working directory.
    /// </summary>
    /// <param name="fileName">The settings file name.</param>
    /// <returns>An absolute path that may or may not exist; both files are optional.</returns>
    private static string HostSettingsPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Maran.Host", fileName));
    }

    /// <summary>
    /// Assembles the connection string from the same separate <c>Database:*</c> keys the panel uses
    /// (rules/security.md), falling back to the values in <c>docker/docker-compose.dev.yml</c> so a
    /// developer who has only started the dev database needs no further configuration.
    /// </summary>
    /// <param name="configuration">Configuration built from the Host's settings and the environment.</param>
    /// <returns>A connection string for design-time use only.</returns>
    private static string BuildConnectionString(IConfiguration configuration)
    {
        var section = configuration.GetSection("Database");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Value(section, "Host", "localhost"),
            Database = Value(section, "Database", "maran_dev"),
            Username = Value(section, "Username", "maran_dev"),
            Password = Value(section, "Password", "maran_dev"),
        };

        var port = Value(section, "Port", string.Empty);
        if (int.TryParse(port, out var parsedPort))
        {
            builder.Port = parsedPort;
        }

        return builder.ConnectionString;
    }

    /// <summary>Reads one setting, treating an empty value as absent.</summary>
    /// <param name="section">The <c>Database</c> configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The value to use when the setting is missing or empty.</param>
    /// <returns>The configured value, or <paramref name="fallback"/>.</returns>
    private static string Value(IConfigurationSection section, string key, string fallback)
    {
        var value = section[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
