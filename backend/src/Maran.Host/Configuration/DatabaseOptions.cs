using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Maran.Host.Configuration;

/// <summary>
/// How the panel reaches its PostgreSQL database, as separate settings rather than one connection
/// string. Each part can be set, read and overridden on its own — an operator editing
/// <c>panel.env</c> changes a port without re-quoting a blob, and a value containing a semicolon or
/// an equals sign cannot corrupt the rest of the string.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Host or unix-socket directory. Production uses the socket directory (<c>/var/run/postgresql</c>)
    /// so nothing listens on TCP; development points at the container on <c>localhost</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Host { get; set; } = "/var/run/postgresql";

    /// <summary>TCP port. Ignored when <see cref="Host"/> is a unix-socket directory.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5432;

    /// <summary>Database name.</summary>
    [Required]
    [MinLength(1)]
    public string Database { get; set; } = "maran";

    /// <summary>Role the panel connects as.</summary>
    [Required]
    [MinLength(1)]
    public string Username { get; set; } = "panel";

    /// <summary>
    /// Password. Empty in production, where the panel connects over the unix socket and PostgreSQL
    /// authenticates it by its operating-system user (peer authentication) — there is no password
    /// to leak because there is none.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Builds the Npgsql connection string from the parts. Uses the builder rather than string
    /// concatenation so a value containing a separator is escaped correctly instead of silently
    /// changing what the rest of the string means.
    /// </summary>
    /// <returns>A connection string, or an empty string when no host is configured.</returns>
    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return string.Empty;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Database = Database,
            Username = Username,
        };

        // A unix-socket directory has no port; setting one would make Npgsql attempt TCP.
        if (!Host.StartsWith('/'))
        {
            builder.Port = Port;
        }

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
