# Marketplace: module contract, package format and loader seam (issues #182, #183, #184) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"; issue #331
tracks finding a second reviewer). This is licence-entitled code loading with signature
verification, and it ends in **downloaded C# running inside the panel process**. The branches
`feature/182-...`, `feature/183-...`, `feature/184-...` may land on branches and MUST NOT merge to
`main` until a second reviewer has read this note and checked every item under "What a reviewer must
check". Written BEFORE the code. The open repository ships interfaces, the verification library and
tests; the loader is closed and lives elsewhere. The agent is not touched (rules/architecture.md
"Agent": it never loads external code).

This file is the working draft for issue #188 and is meant to be merged into it, not to sit beside
it. Task C1 of issue #182 (the Sdk contract: `Manifest`, `ModuleKind`, `ModuleDependency`, menu and
route records, `IModuleMigrations`, `IPanelVersion`, `ManifestValidator`, `SemanticVersion`,
`VersionRange`) is the first code written under it; it loads nothing, downloads nothing and opens no
surface.

## What this surface is

1. A **contract** (`Manifest` extended, `IModuleMigrations`, menu/route records) that says, as data an
   administrator reads before installing, what a module reaches: agent capabilities, authorization
   policies, schema, menu, dependencies, minimum panel version.
2. A **package format** (`.ipm`): a deterministic ZIP whose `ipm.json` pins every other entry by
   SHA-384 and is signed with ECDSA P-384/SHA-384 by a module-signing key.
3. An **open verification library** (`Maran.Packaging`) that reads a package SAFELY (caps,
   containment) and verifies manifest, hashes, signature and compatibility. It is not the loader and
   loads nothing.
4. A **loader seam**: open interfaces (`IModuleLoader`, `IModulePackageSource`, `ILicenceLease`,
   `IModuleRegistry`) plus `NullModuleLoader`. The real loader (closed repo) calls the library, then
   loads.

What it adds to the product: a new way for code to arrive on a server. Before this, every byte of C#
that runs in `maran-api` came from the signed release bundle (release-signing key, `maran update`).

## What task C1 (the contract) changes, and what it deliberately does not

- `Manifest` grows `Kind` (`Core` | `Packageable`), `MinPanelVersion`, `SchemaName`, `Permissions`,
  `Menu` and `Routes`; `Dependencies` changes from ids to `(Id, Range)`. **No new member has a
  default**, so a module that does not state what it reaches does not compile (the same arrangement
  `AgentCapabilities` and `Audience` already have).
- `Permissions` means, for v1, **the names of the authorization policies the module's controllers
  use** (`AuthorizationPolicies`, `ApiKeyPolicyNames`). It is a declaration shown to the
  administrator and checked by a test (controller policies are a subset of the declared list, which is
  a subset of the Sdk vocabulary). It does NOT let a module define a policy of its own; the Host
  defines what each policy requires. A module-defined permission needs a Host registration mechanism
  and is a separate issue (owner decision D2).
- `ManifestValidator` and the version parsers are **pure functions**. They read no file, open no
  socket, load no assembly and hold no key. Their failure mode is a wrong verdict, not an effect.
- `ModuleKind.Core` is the declaration that a module is part of the panel and is never installed from
  a package. It is only a DECLARATION in C1; the loader (issue #184) is what refuses a package that
  claims a Core id (attack 7).
- `IModuleMigrations` lets the Host (the `maran migrate` verb, which is the only place a schema
  changes) iterate every module's migrations instead of discovering contexts. It adds no startup
  migration and no new caller.

## Assets

Panel secrets (`panel.env`: DB credentials, encryption key); PostgreSQL (all tenants, all schemas);
the agent door (`Maran.Agent.Client`, every typed root RPC); administrator sessions and CSRF context;
the lease and licence files; audit integrity; the embedded public keys (`installer/keys/`); the
module-signing PRIVATE key and the marketplace keys (offline, outside this repository); customers'
sites and files via agent capabilities; availability of the core.

## Trust boundaries and the signing chain

- Three keys, three purposes, never shared: licence (Ed25519, `Ed25519LicenceSignatureVerifier`),
  release (ECDSA P-384, `installer/keys/release-signing.pub`), **module signing (ECDSA P-384, new
  `installer/keys/module-signing.pub`)**. A key compromised in one role does not sign in another.
- `ipm.sig` covers the exact bytes of `ipm.json`; `ipm.json` carries `files[{path, sha384, size}]`
  for every entry. A byte changed, a file added that is not listed, a file removed -> refused.
- The public key is INJECTED into `IpmSignatureVerifier`, never embedded in the library, so the open
  library cannot be taught to trust a key by editing it.
- Revocation list and `minVersionFloor` come from the marketplace (served, signed by a key the panel
  already pins); the open repository defines the entry format only (`RevocationList`).
- Encryption (`lib.enc`, key in the lease) is **access control for distribution, not integrity and
  not secrecy against the licensee** (the panel is source-available). The format reserves the entry
  name only; the decrypt step is the closed loader's.

## Attackers (letters match #188)

A network attacker between panel and marketplace; B marketplace/CDN operator; C compromised build
pipeline; D malicious or compromised publisher; E customer (non-admin); F phished administrator; G
local `maran` user without root; H administrator trying to defeat licensing by editing local files.

## What a malicious SIGNED package can do (stated plainly)

**A signed package is trusted code.** In-process, as user `maran`: read `panel.env` (DB password,
secrets); read and write every PostgreSQL schema through the process's connection; act as any
session; resolve any service in the container; exfiltrate over the network; persist across restarts;
tamper with other modules; forge audit entries. It never reaches root except through the typed agent
RPCs the open agent already exposes, and only those named by the capabilities it declared (checked by
`AgentCapabilityGuard` on its types). The controls below reduce WHO can sign and WHAT the loader
accepts. They do not sandbox the code. The manifest is a declaration the author wrote about their own
code: it is a statement for the administrator to read and for tests to hold the shipped modules to,
and it is not a boundary a hostile module is held inside.

## Attack list with controls (open-repo rows; #188 carries the full 20)

| # | Attack | Control | Test (name; inverse control in brackets) |
|---|---|---|---|
| 1 | Tampered package | per-file SHA-384 in signed manifest; verify before any write outside staging | `A_flipped_byte_is_refused` [pristine sample accepted] |
| 2 | Unsigned / wrong-key | `IpmSignatureVerifier` with injected key | `An_unsigned_package_is_refused`, `A_package_signed_by_another_key_is_refused` [right key accepted] |
| 3 | Added/removed/duplicate entry, traversal, symlink, absolute path, case-collision | `IpmReader` containment + manifest census; no extraction before checks | one test per refusal, each beside its good case |
| 4 | Zip bomb | entry-count, total-size, per-entry size and ratio caps | `A_ratio_bomb_is_refused` [a legitimately compressible sample passes] |
| 5 | Downgrade | monotonic version, signed `minVersionFloor`, revocation; downgrade only by explicit admin action with a recorded reason | `A_validly_signed_older_version_is_refused_without_force` [forced+reason accepted] |
| 6 | Package for another panel | `minPanelVersion`/`maxPanelVersion`, unknown `formatVersion` refused; `ManifestValidator` refuses a `MinPanelVersion` above the panel (issue #182) | `A_package_needing_a_newer_panel_is_refused`, `A_minimum_panel_version_above_the_panel_is_found` [equal version accepted] |
| 7 | Id shadowing (`identity`, `licensing`, a shipped `Maran.Sdk`) | reserved ids; forbidden assembly names; `Kind == Core` ids never accepted from a package; `ManifestValidator` finds a duplicate module id in the composed set | `A_package_claiming_a_core_id_is_refused`, `A_package_shipping_its_own_Maran_Sdk_is_refused`, `A_duplicate_module_id_is_found` |
| 8 | Reach beyond declared agent capability | `AgentCapabilityGuard` on the package assembly BEFORE registration; unknown capability refused | `A_package_using_an_undeclared_capability_is_refused` (by capability name) |
| 9 | Service location of agent clients (`IServiceProvider`) | packaged module gets a child container with only Sdk windows and its DECLARED agent clients; root provider not exposed | `A_packaged_module_cannot_resolve_an_undeclared_agent_client` (see residual R3) |
| 10 | Module claims another module's schema | `SchemaName` must equal `Id` (`ManifestValidator`, issue #182); schema ownership check at install | `A_schema_name_that_is_not_the_id_is_found` [no schema accepted], `A_package_mapping_another_schema_is_refused` |
| 11 | Non-admin triggers install | admin-only endpoints (#187), loader has no HTTP surface in this issue | inherited by #187 |
| 12 | Lease lapse removes the core | only `Tier != Included` and uncovered modules go `Degraded`; a `Core` module never depends on a `Packageable` one (`ManifestValidator`) | `Lease_lapse_degrades_only_the_paid_module` [valid lease: module works], `A_core_module_depending_on_a_packageable_one_is_found` |
| 13 | Corrupt installed package blocks boot | per-module try/catch, `Failed` state, panel starts | `A_corrupt_installed_package_does_not_stop_the_panel` [good package reloads] |
| 14 | Time-of-check/time-of-use | verify and load from the same staged read-only bytes; staging directory not writable by anyone but root/installer | review item (cannot be tested portably) |
| 15 | A manifest that over-declares or hides a policy (`Permissions` names a policy the Host does not define, or a controller uses one it did not declare) | `ManifestValidator` refuses a permission outside the Sdk vocabulary; the architecture test compares controller policies with the declared list | `A_permission_outside_the_vocabulary_is_found` [a known policy accepted] |
| 16 | A parser bug in version handling (a crafted version or range string that crashes, hangs or compares wrongly) | `SemanticVersion` and `VersionRange` are strict (length caps, no leading zeros, no overflow, no regex backtracking); parse never throws | `SemanticVersionTests`, `VersionRangeTests` (each refusal beside its accepted case) |

## Residual risks (state, do not hide)

R1 a signed package executes with the panel process's full privileges (no sandbox; per-module DB role
is the open design question D4). R2 trust in the publisher and the signing pipeline (a stolen
module-signing key is the real risk; offline custody and the revocation path are the mitigation). R3
reflection and service location can step past the capability declaration; closing the container
window helps but a module can still call `typeof(...).Assembly` tricks — declared, not sandboxed. R4
bugs in the library's own parsers (ZIP, JSON, SemVer) — hence caps and the refusal tests. R5 time
source for lease expiry and `notBefore`. R6 assembly unload is cooperative; a restart may be needed
to release a module (D5). R7 `Permissions` is a declaration: a hostile module can declare fewer
policies than its controllers use, and the architecture test only sees the assemblies the repository
builds, so for a downloaded package the declaration is advice to the administrator and the loader's
own scan of the assembly is the control.

## Licence-lapse behaviour

Enforced by the server on every request, not by module cooperation. After the grace, modules with
`Tier != Included` that the lease does not cover become `Degraded`: routes answer the existing
licence response, jobs stop, **data is kept**. Core modules (`Kind == Core`) and `Included` modules
are unaffected. A lapse never unloads or deletes anything. Tested end to end (row 12).

## What a reviewer must check (runnable verbatim)

1. Is every byte loaded covered by a signature verified against an injected key? Any path loading
   before verification completes?
2. Archive reading: traversal, absolute path, symlink, duplicate/case-colliding names, entry count,
   total size, ratio — each refused by a named test with a good case beside it.
3. Are licence, release and module-signing keys distinct files with distinct verifier types?
4. Downgrade floor, revocation, `notBefore`, `minPanelVersion` enforced in the SAME code path for
   install and upgrade?
5. Does the loader refuse reserved/Core ids and assemblies named `Maran.Sdk`, `Maran.SharedKernel`,
   `Maran.Agent.Client` or a framework assembly?
6. Is `AgentCapabilityGuard` (or its equivalent) run on the package assembly before any service is
   registered?
7. Lapse test: core endpoint 200 while the paid module answers the licence response.
8. Startup test: a corrupt package does not stop boot; the same package, repaired, loads.
9. Do the contract tests carry a planted violation each (fixture assembly) and an empty-census guard?
10. `maran module verify` and the library agree on the same fixtures (one signing function,
    `sign_release_file`).
11. No new listening port or daemon. The marketplace download is an outbound call: it needs the spec
    §13/§14 amendment and the rules/security.md item 10 exception text BEFORE merge (D1).
12. Secrets: the decryption key is never logged or persisted; the builder never prints or stores a
    private key.
13. The gates were mutation-scored with the whole suite (`maran mutate`) and the named test died.
14. The "could not verify" list below is honest.
15. (Issue #182) `SemanticVersion.TryParse` and `VersionRange.TryParse` never throw: feed each the
    empty string, null, a 10 000-character string, an overflowing number, a leading zero and a
    prerelease with an empty identifier, and read the refusal tests beside each.
16. (Issue #182) Is `Manifest.Dependencies` read by anything that composes modules? Before the
    validator existed nothing read it. The readers are now the validator and, once issue #182's Host
    task lands, the composition root. List them with
    `grep -rnE "\.Dependencies\b" backend/src --include=*.cs | grep -v /obj/`; the positive control is
    that this same command must hit `backend/src/Maran.Sdk/Manifests/ManifestValidator.cs`. The Host must
    run `ManifestValidator.ValidateSet` at composition, and the loader must run it again before it
    registers a package.

## What the author could not verify

- That Wolverine can take new handlers after start (spike S1 not run); that a collectible
  `AssemblyLoadContext` actually unloads with EF Core, Npgsql and Wolverine loaded in the host (leak
  behaviour is empirical).
- That a restricted per-module database role is workable with EF migrations and the panel's DB user
  (D4).
- The behaviour of the closed loader (it is not in this repository).
- Mutation scores (nothing was run for this draft; the contract code of issue #182 was written
  without a build or a test run, by the rule that all builds happen once, after every coder).

## Second review: OUTSTANDING

Owner is told at hand-off: this note carries an outstanding review for licence-entitled code loading
and for package-signature verification.
