using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>
/// The receiving panel's one way to ask another panel for a package (issue #127): an HTTPS GET to an origin an
/// administrator typed, carrying the grant and nothing else.
/// </summary>
public interface IMigrationPullClient
{
    /// <summary>Opens one part of an export on the other panel.</summary>
    /// <param name="origin">The other panel's origin, already normalised by <c>PublicUrlRule</c>.</param>
    /// <param name="part">Whether to ask for the head of the package or the package.</param>
    /// <param name="grant">The grant the exporting administrator issued; sent in the <c>Authorization</c> header only.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response body, to be read forward and disposed; or a typed failure (the address is refused, the other
    /// panel refused the grant, or it could not be reached). The body is not bounded here: the caller bounds it by
    /// the signed length.
    /// </returns>
    Task<Result<Stream>> OpenAsync(string origin, PullPart part, SensitiveString grant, CancellationToken cancellationToken);
}
