using Maran.Sdk.Navigation;

namespace Maran.Sdk.Contracts;

/// <summary>
/// Identity every module (ours and third-party) declares about itself, published through the
/// modules catalogue endpoint (<c>GET /api/v1/modules</c>) so the SPA and the licence system know
/// what is installed without inspecting assemblies (rules/csharp.md "Canonical backend layout").
/// </summary>
/// <param name="Id">
/// Stable machine id: lowercase letters and digits, beginning with a letter. Equals the module's
/// <see cref="Interfaces.IPanelModule.Name"/> and, when the module owns tables, its PostgreSQL schema name.
/// </param>
/// <param name="DisplayNameKey">
/// The resx key resolved (via <see cref="SharedKernel.Interfaces.IErrorTextProvider"/>, in the
/// request culture) for this module's human-readable name. The module's own
/// <c>Resources/Messages*.resx</c> supplies the actual English/Russian/Armenian text — a module is
/// discovered at runtime, so the SPA can never own the label for one it was not built knowing
/// about, and the backend owns all user-facing text regardless (rules/csharp.md "The backend owns
/// all user-facing message text").
/// </param>
/// <param name="Version">Semantic version of this module build.</param>
/// <param name="Tier">The licence tier this module ships under.</param>
/// <param name="Kind">
/// Whether the module is part of the panel (<see cref="ModuleKind.Core"/>) or can be installed from a
/// package (<see cref="ModuleKind.Packageable"/>). There is deliberately no default: a module that
/// does not say which it is must fail to compile rather than be assumed removable.
/// </param>
/// <param name="MinPanelVersion">
/// The oldest panel this module works with, a semantic version. A panel older than this refuses to
/// compose the module. There is no default, so a module states what it was built against.
/// </param>
/// <param name="SchemaName">
/// The PostgreSQL schema the module owns, which must equal <paramref name="Id"/>, or null for a module
/// with no tables of its own. A module that owns a schema also offers
/// <see cref="Interfaces.IModuleMigrations"/>; one that owns none does not.
/// </param>
/// <param name="Dependencies">
/// The modules this module requires to be composed first, each with the versions of it that this
/// module works with (<see cref="ModuleDependency"/>). Read by <c>ManifestValidator</c>: an id that is
/// not composed, a version outside the range and a cycle are all refused.
/// </param>
/// <param name="AgentCapabilities">
/// The areas of the agent's contract this module may drive, and the only ones the panel will let it
/// reach (<see cref="AgentCapability"/>). Empty for a module that never talks to the agent, which is
/// the honest declaration for most of them — Identity, Notifications and Tasks each hold none.
/// </param>
/// <param name="Permissions">
/// The names of the authorization policies the module's controllers use (<see cref="AuthorizationPolicies"/>
/// and <see cref="ApiKeyPolicyNames"/>), shown to the administrator before installing. The Host defines
/// what each policy requires; a module cannot define one of its own, so every entry must be a name in
/// the Sdk's vocabulary. A declaration and not a boundary: the policy on each endpoint is what enforces.
/// </param>
/// <param name="Menu">The entries the module contributes to the navigation; empty for a module whose screens live inside another's.</param>
/// <param name="Routes">The screens the module's interface registers; empty for a module with no screen of its own.</param>
/// <param name="Audience">
/// Who this module's screens are for (<see cref="ModuleAudience"/>). The modules catalogue endpoint
/// reads it to decide what to offer a signed-in customer versus an administrator — presentation
/// only, so it hides nothing that an endpoint would not itself refuse; every route keeps its own
/// authorization check regardless of what the catalogue lists. There is deliberately no default: a
/// module that does not state its audience must fail to compile rather than be silently offered to
/// a customer.
/// </param>
public sealed record Manifest(
    string Id,
    string DisplayNameKey,
    string Version,
    LicenceTier Tier,
    ModuleKind Kind,
    string MinPanelVersion,
    string? SchemaName,
    IReadOnlyList<ModuleDependency> Dependencies,
    IReadOnlyList<AgentCapability> AgentCapabilities,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleMenuItem> Menu,
    IReadOnlyList<ModuleRoute> Routes,
    ModuleAudience Audience);
