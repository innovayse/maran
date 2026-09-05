using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// What the panel tells its interface about one module: identity plus whether the current licence
/// makes it usable. The SPA renders navigation and guards routes from this list, but the gate is
/// cosmetic — the backend re-checks the licence on every request (rules/architecture.md
/// "Where a module's UI lives").
/// </summary>
/// <param name="Name">Stable machine name, equal to the module's PostgreSQL schema. The SPA keys routes and licence gating on this, never on <paramref name="DisplayName"/>.</param>
/// <param name="Tier">
/// The module's <see cref="LicenceTier"/>, carried as the typed contract value rather than a
/// hand-picked string so the wire shape can never drift from the enum modules actually declare.
/// Serialized as its member name (<c>"included"</c>, <c>"addOn"</c>, <c>"planGated"</c>) via the
/// panel-wide <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> so the SPA
/// never has to track numeric enum values across releases.
/// </param>
/// <param name="DisplayName">
/// The module's human-readable name, resolved server-side in the request's culture from the
/// module's own <c>Resources/Messages*.resx</c> via its <c>Manifest.DisplayNameKey</c>. Modules
/// are discovered at runtime — a paid marketplace module is unknown when the SPA is built — so the
/// SPA can never own this label; the backend owns all user-facing text regardless
/// (rules/csharp.md "The backend owns all user-facing message text").
/// </param>
/// <param name="IsEnabled">
/// Whether the running licence currently permits it. False renders the module's entries as locked
/// rather than hiding the product's existence.
/// </param>
/// <param name="AgentCapabilities">
/// The parts of the root agent this module is permitted to drive, from its own
/// <see cref="Manifest"/>. It is on the wire so that the answer to "what does this module reach"
/// is available to the interface — the question that matters for a module bought from a
/// marketplace, where the description is otherwise the only evidence. No screen renders it yet;
/// the one that does arrives with the install flow, and it will not need an API change to do it,
/// which is the point of carrying it now. Serialized as member names by the
/// panel-wide <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>, for the same
/// reason <paramref name="Tier"/> is. This is a disclosure, never the enforcement:
/// <c>AgentCapabilityGuard</c> refuses the module at composition, before any request exists to
/// check (rules/security.md item 13).
/// </param>
public sealed record ModuleDto(
    string Name,
    LicenceTier Tier,
    string DisplayName,
    bool IsEnabled,
    IReadOnlyList<AgentCapability> AgentCapabilities);
