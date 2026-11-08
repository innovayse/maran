# The module contract

What a module must be, say about itself and refrain from doing, so that it can be composed into the
panel today and shipped as a package later without a rewrite. Every rule below is held by an
architecture test in `backend/tests/Maran.ArchitectureTests`; the last section maps one to the other.

The contract is the trust boundary the package loader enforces later. Everything that grants a module
a capability is **declared data an administrator reads before installing**, never inferred.

## Two kinds of module

`Manifest.Kind` is a closed vocabulary (`ModuleKind`):

| Kind | Meaning | Examples |
|---|---|---|
| `Core` | Part of the panel. Never ships as a package. Losing a licence never removes it. | the modules the panel cannot work without: identity, licensing, accounts, and the facilities other modules reach through Sdk windows |
| `Packageable` | Can be built, signed and installed separately. Held to every rule below. | the optional server features |

The table of which module is which is `Kind` in each module's `<Name>Manifest.cs`; this document does
not repeat it, so it cannot go stale. The rule for choosing is the one in rules/architecture.md: a
facility is core when anybody would still need it were the module removed.

Rules that only make sense for something that can leave the repository (declared controller policies,
menu entries pointing at the module's own routes, the package allow-list, the SPA ratchet) bind
`Packageable` modules only. Identity, version, schema, permission vocabulary, uniqueness and
dependency rules bind every module, because the Host reads those for all of them.

## What the manifest declares

`Maran.Sdk/Contracts/Manifest.cs`. No security-relevant member has a default: a module that does not
state it does not compile.

| Member | Rule |
|---|---|
| `Id` | Lower-case letters and digits. Equals `IPanelModule.Name`. Unique panel-wide. It is also the schema name. |
| `DisplayNameKey` | A key in the module's own `Resources/DisplayNames` triple, with a value in en, ru and hy. |
| `Version` | A semantic version. |
| `Tier` | A `Core` module is `Included`. |
| `Kind` | `Core` or `Packageable`. |
| `MinPanelVersion` | A semantic version, not above the panel this build is. |
| `SchemaName` | `null` (the module has no tables) or equal to `Id`. |
| `Dependencies` | `(Id, Range)` pairs. Each `Id` names a composed module, not itself, and the graph has no cycle. A `Core` module never depends on a `Packageable` one. |
| `AgentCapabilities` | The parts of the agent the module may drive; enforced by `AgentCapabilityGuard`. |
| `Permissions` | The Sdk authorization policy names the module's controllers use (`AuthorizationPolicies`, `ApiKeyPolicyNames`). A module cannot define a policy of its own: policies are defined by the Host. |
| `Menu` | Entries the SPA shows: key, route name, icon key, label key, order, audience. Keys are unique panel-wide. |
| `Routes` | Route name, path, title key, required module. Names are unique panel-wide. |
| `Audience` | Who the module's screens are for. Presentation only; every endpoint keeps its own authorization. |

Locales are fixed to en, ru and hy by test, not by a field. Menu labels and route titles are resource
keys in the module's own `DisplayNames` triple; the backend resolves them in the request culture. A key
is declared by one module only, because all modules' resources feed one pool.

## Persistence

- A module that stores anything owns **one** PostgreSQL schema named after it and **one** `DbContext`
  whose default schema is that name. No entity is mapped into another schema.
- `SchemaName` says so, and is `null` exactly when the module owns no context.
- A module that owns a context implements `IModuleMigrations` (the seam the Host's migrate verb and
  the package loader both call); a module without one does not.
- Hand-written SQL (`// raw-sql:`) names tables of the module's own schema only.
- Migrations follow expand then contract (rules/architecture.md).

## References

A packageable module's project file references:

- `Maran.Sdk` and `Maran.SharedKernel`;
- `Maran.Agent.Client` only when `AgentCapabilities` is not empty;
- packages and frameworks listed in `backend/tests/Maran.ArchitectureTests/ModuleReferenceAllowList.txt`,
  each with a reason.

Never another module, never the Host, never a bare assembly. Adding a package a module may use is an
edit to the allow-list, which is a visible diff reviewed on its own (rules/security.md item 11).

## The SPA

A packageable module's menu and routes come from its manifest. The SPA's router still lists every
module's screens, so the gate is a ratchet: the number of `meta: { module: '<id>' }` routes for a
packageable module may fall and may never rise (`PackageableSpaRouteBaseline.txt`).

## Adding a module

1. `maran module <Name>` creates the skeleton.
2. Write the manifest with every member; choose `Kind` by the rule above.
3. Put the menu labels and route titles in `Resources/DisplayNames{,.ru,.hy}.resx`.
4. If the module stores data, add the `DbContext`, the migrations and the `IModuleMigrations`
   implementation, all under the one schema.
5. Register the module in `ModuleRegistry`. The architecture tests find it from there; nothing else
   needs editing, and a module compiled in but not registered is named by `ModuleContractTests`.

## Which test holds which rule

| Rule | Test class |
|---|---|
| Every module has a manifest; id, name, version, min panel version, schema, permissions, menu, routes, dependencies, Core and Packageable | `ModuleContractTests` (`ModuleContractLaw`, `ModuleDependencyLaw`) |
| Default schema, entity mapping, `SchemaName` and `IModuleMigrations` match; raw SQL names no foreign schema | `ModuleSchemaOwnershipTests` |
| Display name, menu labels and route titles exist in en, ru and hy and are the module's own | `ModuleResourceContractTests` (beside `ResourceKeyParityTests`, `ModuleDisplayNameLawTests`) |
| Project and compiled references | `ModuleSdkOnlyReferenceTests` |
| No more SPA routes for a packageable module | `PackageableSpaRouteTests` |
| No module references another or the Host | `ModuleIsolationTests` |
| Agent reach is declared | `AgentCapabilityTests` |
| Every `AccountId` entity has a query filter | `TenantScopeTests` |

Each of these carries a planted violation it must flag by rule name and an accepting control it must
not, and a guard that its census examined something.

## What this does not establish

- **Truthfulness.** A `MinPanelVersion` or a `Permissions` list is a human's statement. The tests prove
  it is well-formed and consistent with the code they can read, not that it is true.
- **Permissions on endpoints.** `Permissions` is checked against the controllers' `[Authorize]`
  attributes. A policy applied by route-group metadata or a filter is not an attribute and is not seen.
- **Service location.** A module that asks `IServiceProvider` for an agent client at run time declares
  nothing in its metadata and is not caught by `AgentCapabilityGuard` (rules/security.md item 13).
- **Transitive packages.** The allow-list judges what the project file names, not the closure of what
  those packages bring.
- **Dependency ranges.** Whether a dependency's version satisfies the range is the loader's question
  at install time; the tests read ids.
- **Registration of `IModuleMigrations`.** The tests find the implementation in the assembly; that the
  module registers it is covered where the Host runs the migrations.
- **A packaged module's runtime behaviour.** Loading, signature checks and unloading are not in this
  repository's open build.
