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
/// <param name="IsDefault">
/// Whether this version is the host's default CLI PHP, or <c>null</c> when the agent did not
/// establish it. Null and false are different answers and are not conflated here: "not known" must
/// not be rendered as "not the default".
/// </param>
public sealed record PhpVersionDto(string Version, bool? IsDefault);
