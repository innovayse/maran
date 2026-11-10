using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// What the panel tells its interface about one module: identity plus whether the panel lets it be
/// used. The SPA renders navigation and guards routes from this list, but the gate is
/// cosmetic — the backend re-checks the licence on every request (rules/architecture.md
/// "Where a module's UI lives").
/// </summary>
/// <param name="Name">Stable machine name, equal to the module's PostgreSQL schema. The SPA keys routes and licence gating on this, never on <paramref name="DisplayName"/>.</param>
/// <param name="Tier">
/// The module's <see cref="LicenceTier"/>, carried as the typed contract value rather than a
/// hand-picked string so the wire shape can never drift from the enum modules actually declare.
/// Serialized as its member name (<c>"included"</c>, <c>"addOn"</c>, <c>"planGated"</c>) via the
/// panel-wide <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> so the SPA
/// never has to track numeric enum values across releases. The words an operator reads are
/// <paramref name="TierDisplayName"/>; this member is for keys and behaviour only.
/// </param>
/// <param name="TierDisplayName">
/// The same tier as an operator reads it, resolved server-side in the request's culture by
/// <see cref="LicenceTierDisplayNames"/>. It travels beside the machine value for the same reason
/// <paramref name="DisplayName"/> travels beside <paramref name="Name"/>: the SPA owns no words for
/// a server-side concept (rules/vue.md "Data comes from the backend; the SPA only displays it" —
/// "no client-side lists of plans, statuses, tiers or limits"). Before this member existed the
/// upgrade screen interpolated <paramref name="Tier"/> into a translated sentence, so a Russian
/// panel read "Он доступен в тарифе addOn."
/// </param>
/// <param name="DisplayName">
/// The module's human-readable name, resolved server-side in the request's culture from the
/// module's own <c>Resources/Messages*.resx</c> via its <c>Manifest.DisplayNameKey</c>. Modules
/// are discovered at runtime — a paid marketplace module is unknown when the SPA is built — so the
/// SPA can never own this label; the backend owns all user-facing text regardless
/// (rules/csharp.md "The backend owns all user-facing message text").
/// </param>
/// <param name="State">
/// Whether the module may be used right now, decided in one place by
/// <see cref="ModuleStateResolver"/>. <see cref="ModuleState.Locked"/> renders the module's entries
/// as locked and leads them to the upgrade page rather than hiding the product's existence.
/// Serialized as its member name (<c>"enabled"</c>, <c>"lapsed"</c>, <c>"locked"</c>) by the panel-wide
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>. A module the panel did not
/// compose is not in the list at all, so there is no third value.
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
/// <param name="MinPanelVersion">
/// The oldest panel version the module runs on, from its <see cref="Manifest"/>. It is a disclosure
/// for the administrator reading the catalogue; the refusal to compose a module that needs a newer
/// panel is made at composition, not here.
/// </param>
/// <param name="Kind">
/// Whether the module is part of the core every panel needs or one that can be installed separately
/// (<see cref="ModuleKind"/>). Serialized as its member name by the panel-wide
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>.
/// </param>
/// <param name="KindDisplayName">
/// The same kind as an operator reads it, resolved server-side in the request's culture by
/// <see cref="ModuleKindDisplayNames"/>, for the reason <paramref name="TierDisplayName"/> travels
/// beside <paramref name="Tier"/>: the SPA owns no words for a server-side concept.
/// </param>
/// <param name="Permissions">
/// The authorization policy names the module's endpoints use, from its <see cref="Manifest"/>, so an
/// administrator sees what the module asks for beside the agent capabilities. A disclosure, never the
/// enforcement: each endpoint carries its own policy.
/// </param>
/// <param name="Menu">
/// The navigation entries the module contributes that the caller is offered, localized in the
/// request's culture by <see cref="ModuleMenuLocalizer"/> and ordered by their declared order. Empty
/// for a module whose screens live inside another module's.
/// </param>
/// <param name="Routes">The screen routes the module contributes, with titles localized in the request's culture.</param>
/// <param name="ValidUntil">
/// When the licence's cover of the module ends (or ended, for a lapsed module), from the licence window; null
/// for an included module, which is covered by no licence, and for a module nothing covers.
/// </param>
/// <param name="Version">
/// The version of the module composed into this panel, the manifest's semantic version. It is the version
/// the module's frame is served for, so a screen can never be from another release than the running module.
/// </param>
/// <param name="Lifecycle">
/// Where the module stands (<see cref="ModuleLifecycleState"/>): <c>"active"</c> unless the loader reports
/// it <c>"degraded"</c> or <c>"failed"</c>. A module that is installed but not yet composed is not in the
/// list at all, so <c>"installed"</c> and <c>"removed"</c> do not occur here. Serialized as its member name
/// by the panel-wide <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>. Distinct from
/// <paramref name="State"/>, which is the licence's verdict; a screen is framed only when the module is
/// active.
/// </param>
/// <param name="IsPackaged">
/// Whether the module arrived as a package rather than shipping with the panel. Presentation only: a
/// packaged module's screen runs in a sandboxed frame, a shipped module's in the shell itself.
/// </param>
/// <param name="Spa">
/// How the shell reaches the module's screen (<see cref="ModuleSpaDto"/>), or null when the module is not
/// framed: it shipped with the panel, is not active, or holds no screen bundle for the running version.
/// </param>
/// <param name="FailureDisplay">
/// The sentence an operator reads for a degraded or failed module, resolved server-side in the request's
/// culture from the loader's coded cause, for the reason <paramref name="DisplayName"/> is resolved here.
/// Null for a module that is active, and for one whose cause the loader did not state; never exception
/// text, a path or tool output.
/// </param>
public sealed record ModuleDto(
    string Name,
    LicenceTier Tier,
    string TierDisplayName,
    string DisplayName,
    ModuleState State,
    IReadOnlyList<AgentCapability> AgentCapabilities,
    string MinPanelVersion,
    ModuleKind Kind,
    string KindDisplayName,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleMenuItemDto> Menu,
    IReadOnlyList<ModuleRouteDto> Routes,
    string Version,
    ModuleLifecycleState Lifecycle,
    bool IsPackaged,
    ModuleSpaDto? Spa,
    string? FailureDisplay,
    DateTimeOffset? ValidUntil);
