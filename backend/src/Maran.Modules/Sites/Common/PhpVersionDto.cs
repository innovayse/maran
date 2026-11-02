using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Outward view of one PHP runtime installed on the server — the reference data a site's
/// backend form selects from, so the customer never types a version the host does not have
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// A panel-shaped record distinct from the agent client's own version DTO: this one omits the FPM
/// socket directory, which is a filesystem path and therefore operator-facing detail that has no
/// business on a customer's screen (rules/security.md).
/// </remarks>
/// <param name="Version">Two-component version as the packages name it, e.g. <c>8.3</c>.</param>
/// <param name="DefaultStatus">
/// Whether this version is the host's default CLI PHP. <see cref="PhpDefaultStatus.Unknown"/> when
/// the agent did not establish it; that is not the same answer as
/// <see cref="PhpDefaultStatus.NotDefault"/> and is never rendered as it.
/// </param>
public sealed record PhpVersionDto(string Version, PhpDefaultStatus DefaultStatus);
