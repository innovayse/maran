using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Maran.Samples.Hello.Persistence;

/// <summary>
/// Lets EF Core design-time tooling (<c>dotnet ef migrations add</c>) construct <see cref="HelloDbContext"/>
/// without booting the panel. Never used at runtime: the module registers the context through DI.
/// </summary>
/// <remarks>
/// Generating a migration needs the shape of the schema and never a live database, so the connection
/// below is only well-formed, and is read from the same <c>Database__*</c> environment the panel reads when
/// a developer has set it.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelloDbContext>
{
    /// <summary>Builds a context for the tooling.</summary>
    /// <param name="args">Unused; required by the <see cref="IDesignTimeDbContextFactory{TContext}"/> contract.</param>
    public HelloDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var section = configuration.GetSection("Database");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Value(section, "Host", "localhost"),
            Database = Value(section, "Database", "maran_dev"),
            Username = Value(section, "Username", "maran_dev"),
            Password = Value(section, "Password", "maran_dev"),
        };

        var optionsBuilder = new DbContextOptionsBuilder<HelloDbContext>();
        optionsBuilder.UseNpgsql(builder.ConnectionString);
        return new HelloDbContext(optionsBuilder.Options);
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
