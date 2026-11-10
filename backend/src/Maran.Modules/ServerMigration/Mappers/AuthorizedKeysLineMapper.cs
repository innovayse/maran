namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Builds the single line an administrator installs in the remote server's <c>authorized_keys</c>: the panel's
/// public key with the options that take away everything but a command-running session, and, when the administrator
/// stated where the panel connects from, a <c>from=</c> restriction.
/// </summary>
/// <remarks>
/// <para>
/// The line is built here, on the server, so the instruction the administrator copies is the one the panel
/// intends, and the SPA only displays it. The options remove port, agent and display forwarding and the terminal.
/// They do NOT make the key read-only: a key that may run commands as the login may run any command that login may,
/// which is why the panel states that limit beside the line and only ever runs its closed set of read commands.
/// </para>
/// <para>
/// <c>allowedFrom</c> reaches this line only after <c>AllowedFromPolicy</c> has rebuilt it from parsed address
/// ranges, so it carries no quote and no separator that could end the option early.
/// </para>
/// </remarks>
public static class AuthorizedKeysLineMapper
{
    /// <summary>The options that remove everything but running a command.</summary>
    private const string RestrictingOptions = "no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty";

    /// <summary>The comment that marks the key as the panel's, so an administrator can find and remove it.</summary>
    private const string Comment = "maran-migration";

    /// <summary>Builds the <c>authorized_keys</c> line for one server.</summary>
    /// <param name="publicKey">The panel's public key for the server, an OpenSSH public key line.</param>
    /// <param name="allowedFrom">The canonical list of addresses the key is restricted to, or <c>null</c> for anywhere.</param>
    /// <returns>The line, without a trailing newline.</returns>
    public static string From(string publicKey, string? allowedFrom)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var options = string.IsNullOrEmpty(allowedFrom)
            ? RestrictingOptions
            : $"from=\"{allowedFrom}\",{RestrictingOptions}";

        return $"{options} {publicKey.Trim()} {Comment}";
    }
}
