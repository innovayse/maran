namespace Maran.Sdk.Interfaces;

/// <summary>
/// Tells the app store which sites use an installed app, so uninstalling one a site still runs on can be
/// refused with the list (issue #517). Implemented by the module that owns the sites table, for the
/// reason <see cref="ISiteDirectory"/> is.
/// </summary>
/// <remarks>
/// Server-wide and NOT tenant-scoped, and that is the whole warning: it answers for every account, so it
/// must be reached only from an administrator-only operation. Names only (primary domains), the narrowest
/// value that answers the question, so no account, document root or backend travels with it.
/// </remarks>
public interface ISiteRuntimeUsage
{
    /// <summary>Lists the primary domains of every site, whatever its backend: the sites a web server serves.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The domains in ascending order; empty when no site exists.</returns>
    Task<IReadOnlyList<string>> ListDomainsOnWebServerAsync(CancellationToken cancellationToken);

    /// <summary>Lists the primary domains of the sites bound to one PHP version.</summary>
    /// <param name="version">The PHP version as the agent names it, e.g. <c>8.3</c>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The domains in ascending order; empty when no site uses that version.</returns>
    Task<IReadOnlyList<string>> ListDomainsOnPhpAsync(string version, CancellationToken cancellationToken);

    /// <summary>Lists the primary domains of the sites running on one Node.js major version.</summary>
    /// <param name="version">The Node.js major version, e.g. <c>22</c>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The domains in ascending order; empty when no site uses that version.</returns>
    Task<IReadOnlyList<string>> ListDomainsOnNodeAsync(string version, CancellationToken cancellationToken);
}
