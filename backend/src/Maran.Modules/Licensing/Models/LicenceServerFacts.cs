namespace Maran.Modules.Licensing.Models;

/// <summary>
/// What an administrator needs to name this server to the marketplace: the id a licence is bound to and the
/// link that carries it to the claim page. Read fresh each time, like the rest of the status.
/// </summary>
/// <param name="ServerId">
/// The machine id exactly as the licence and lease comparisons read it (<c>IServerIdentitySource</c>); null when
/// the host has none or the agent could not be asked.
/// </param>
/// <param name="ClaimUrl">
/// The marketplace claim page carrying <paramref name="ServerId"/>; null when there is no id the marketplace
/// accepts or no usable address for the page.
/// </param>
public sealed record LicenceServerFacts(string? ServerId, string? ClaimUrl);
