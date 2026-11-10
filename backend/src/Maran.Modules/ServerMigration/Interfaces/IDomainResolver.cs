using System.Net;

namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>
/// Resolves a name with the system resolver, for the DNS helper of a migration (issue #197, phase 2, decision D19).
/// </summary>
/// <remarks>
/// A seam so the helper is driven without a network. It answers addresses and nothing else: it never connects to what
/// it resolved, and it is asked only for the domains of a job.
/// </remarks>
public interface IDomainResolver
{
    /// <summary>Resolves a name.</summary>
    /// <param name="domain">A host name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The addresses, or an empty list when the name does not resolve.</returns>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain, CancellationToken cancellationToken);
}
