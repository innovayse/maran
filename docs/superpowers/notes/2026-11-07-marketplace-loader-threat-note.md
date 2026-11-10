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

- Three keys, three purposes, never shared (the marketplace slice adds a fourth, the metadata key: see "Marketplace: the service client" below): licence (Ed25519, `Ed25519LicenceSignatureVerifier`),
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

## Packaged module screens (#185): the frame, the bridge and the scope middleware

Status: written together with the code, which was produced in one pass without a build or a test run
(the repository's code-first rule), so every control below is "present and tested by a named test that
has not been run yet". Second review: OUTSTANDING (#331).

**What changes.** A module that arrives as a package can have a screen. Decision D3(b) of the plan:
the screen is NOT imported into the SPA. The shell hosts it in an iframe with `sandbox="allow-scripts"`
and no `allow-same-origin`, so the frame is an opaque origin; the panel serves the frame from
`/module-frames/{id}/{version}/`; the frame and the shell talk over a `MessageChannel` after a nonce
handshake; the shell performs the frame's API calls with its own session and marks each with
`X-Maran-Frame: <moduleId>`; the backend refuses a framed call to an endpoint that module does not own.
The privileged surfaces touched: the shell's session/CSRF handling (it now relays requests for third-party
code), the CSP and framing headers (an nginx location, a privileged installer file), and a new
anonymous route on the API.

**Assets.** The administrator's session (cookie, access token, CSRF header) held by the shell; the data
every endpoint of every other module returns; the panel's framing protections (`DENY`, `frame-ancestors
'none'`) which protect every other screen from clickjacking.

**Trust boundary.** The package's entry script is attacker-influenced code even when signed (a
signed package can still be malicious, section "What a malicious SIGNED package can do"). Everything it can
reach from the frame must therefore be reachable by a signed, malicious module anyway, and no further:
its own endpoints, as the administrator, through the shell.

| # | Attack | Control | Test (inverse control in brackets) |
|---|---|---|---|
| F1 | The frame reads the shell's cookies, storage or DOM | opaque origin: `sandbox` fixed inside `UiSandboxFrame` to `allow-scripts`, not a prop | `frame-isolation.spec.ts` probe inside the frame [a positive control probe in a non-sandboxed fixture page CAN read the same storage, so the probe is able to see] |
| F2 | The frame calls another module's endpoint, or an ownerless one (catalogue, search, dashboard), as the administrator | `ModuleFrameScopeMiddleware`: header names a module, the matched endpoint must be owned by it, else 403 `ModuleFrameOutOfScope`; fails closed on a malformed or repeated header and on an endpoint no module owns | `ModuleFrameScopeMiddlewareTests.A_framed_call_to_another_modules_endpoint_is_refused` [its own endpoint served; no header = a normal call] |
| F3 | The frame sends the call WITHOUT the header | The frame has no network (`connect-src 'none'`, opaque origin, no `allow-same-origin`): it can only ask the shell, and the shell always adds the header on a relay | `frame-hostile.spec.ts` asserts the relayed request carries `X-Maran-Frame` [a request outside the bridge is the shell's own and carries none] |
| F4 | Spoofed or repeated `ready`, a message from another window | `event.source === frame.contentWindow`, a per-mount 128-bit nonce read from the frame address, one accepted `ready`; the rest are dropped and counted | `bridge-messages.spec.ts` [the genuine frame's handshake completes] |
| F5 | Oversized, malformed or flooding traffic | schema validation both ways, 64 KiB per message, 1 MiB `api` body, 30 `api` per second, three violations unmount the frame, ten silent seconds unmount it | one spec per limit beside an at-the-limit accepted case |
| F6 | A signed package serves a script as an "asset", or an HTML document to be framed | allow-list of extensions with FIXED content types, `nosniff`, `.html` and everything else 404; the frame document is generated by the Host | `ModuleFramePathRuleTests`, `ModuleFrameEndpointTests.An_html_file_inside_spa_is_not_served` [a `.js` is]; the package validator also refuses `.html` under `spa/` |
| F7 | The frame framing the panel, or the panel being framed by a hostile site, after the nginx change | nginx: `/module-frames/` has its own header set; `/api/` and `/` keep `DENY`; the frame CSP says `frame-ancestors 'self'` | polygon assertion `assert_the_module_frame_location_has_its_own_headers_and_api_keeps_deny` [a vhost that dropped `DENY` everywhere must fail] |
| F8 | Enumeration of installed modules and versions through the anonymous route | 404 for unknown module, wrong version, non-packaged, not active; never 403 | `ModuleFrameEndpointTests.An_unknown_version_is_404_not_403` |
| F9 | A degraded or failed module still serves its UI | `ModuleFrameRule` is the one judgement for catalogue and route: only an active, packaged module whose description version equals the running version is framed | `A_degraded_module_is_not_framed` [an active one is] |
| F10 | The frame learns who the administrator is, or holds a credential | the shell sends only `init` (locale, theme, role name, the module's own route), `locale`, `theme` and `api-result`; the relay answer carries status and body, never headers | `bridge-messages.spec.ts` reads every message the fixture frame received |

**Residual risks (stated).**

- R-F1. A malicious signed module's frame can ask the shell to perform ANY call to ITS OWN endpoints with the
  administrator's authority, including destructive ones. The package signature and the administrator's
  decision to install are what stand between that and harm; the frame boundary does not make a module's
  backend safe (rules/security.md item 14).
- R-F2. A framed `api` call to the module's endpoints passes through the shell's session refresh like any
  call; a module that spams it is held by the 30 per second limit per mount, not globally.
- R-F3. The `X-Maran-Frame` header is a declaration by the shell. A script inside the SPA's own realm (an XSS
  in the shell) could omit it. That is not a new capability: the same script already holds the session.
- R-F4. The frame document is served unauthenticated and cached `immutable` by version; anyone who can
  reach the panel can read a module's bundle. The assets are signed and are not secret by design.
- R-F5. A module that is not framed (degraded, failed, installed-not-composed) simply has no route in
  the SPA; the 404 of the frame route is the enforcement, the missing route is the courtesy.

**What a reviewer must check (runnable verbatim; each negative result owes its positive control).**

17. The only `<iframe>` in the SPA: `grep -rnE "<iframe" frontend/src --include=*.vue | grep -v /node_modules/`
    must print exactly the `UiSandboxFrame.vue` lines; positive control: the same command without the
    `grep -v` hits that file (tool: `/usr/bin/grep`). `vue/no-restricted-html-elements` lists `iframe`
    (`grep -n "element: 'iframe'" frontend/eslint.config.ts`).
18. The sandbox is never widened: `grep -rn "allow-same-origin" frontend/src backend/src installer`
    must print only comments that say it is forbidden; any attribute value containing it fails the review.
19. The frame CSP is spelled in one file: `grep -rn "connect-src 'none'" backend/src --include=*.cs | grep -v /obj/`
    must print `ModuleFrameHeaders.cs` only (positive control: that file is printed).
20. Every framed relay carries the header: `grep -rn "X-Maran-Frame" frontend/src` must hit
    `composables/apis/useModuleBridgeApi.ts` and no other writer; `ModuleFrameScopeMiddleware.HeaderName`
    is the one server-side spelling.
21. The Host's anonymous route is pinned: `AnonymousEndpointPin` names `/module-frames/` with its reason.
22. The scope middleware sits after authentication and routing: read `Program.cs` and confirm
    `UseModuleFrameScope()` follows `UsePanelAuthentication()`; its test must still pass with the
    authenticated administrator as the caller.
23. The nginx location carries `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy` and
    NO `X-Frame-Options` and NO CSP: read `installer/nginx/maran.conf` `location /module-frames/` and the
    polygon assertion's output, which prints its own blind spots.

**What the author could not verify (for this surface).** Spike S2 (an opaque-origin frame loads a classic
script with Subresource Integrity over CORS in Chromium, Firefox AND WebKit, and the `MessageChannel`
handshake completes in each) was not run; the Playwright project is Chromium only. Spike S3 (whether the
shell's `default-src 'self'` already permits the same-origin frame, so no `frame-src` line is needed) was
not run; the shell CSP is unchanged and a browser must confirm the frame loads under it. Spike S4 (what a
panel restart looks like to the SPA) was not run. nginx 1.25.1+ parsing of the vhost is not observed (the
vhost's own disclaimer). The clock-driven specs (ten-second handshake deadline) use `page.clock` and have
not been run. No mutation score exists for the middleware's header comparison, the sandbox string or the
nginx header (the last two have no .NET mutant; the Playwright probe and the polygon assertion are their
only witnesses). The closed loader's implementation of `IModuleSpaAssets` and its checks that served
bytes match the pinned integrity are outside this repository.

## Marketplace: the service client, the download path and the panel API (#186, #187, #188)

Status: **Second review: OUTSTANDING (#331)**. This section is the working text of issue #188 for the marketplace
slice. It was written in the same pass as the code and, like the frame section above, without a build or a test run
(the repository's code-first rule): every control below is "present, and held up by a named test that has not been
run yet". The module is `backend/src/Maran.Modules/Marketplace`; its contract with the service is
`docs/marketplace/api-v1.md`; the operator's description is `docs/operations/marketplace.md`.

**What changes.** The panel process (`maran-api`, user `maran`) gains a SECOND documented outbound destination, the
marketplace host (`https://api-marketplace-maran.innovayse.com` by default, one configured host only), and a way for downloaded C#
to be staged for the closed loader. It adds no listening port, no daemon, no agent RPC and no proto change. The
privileged surfaces touched: a new outbound call (rules/security.md item 10), a new pinned verification key, the path a
package takes from the network to the loader, the re-entry of an administrator's password (auth), and two installer
steps (a directory and a key file).

**Corrections carried from the earlier draft and the issue text, which are stale on these points.**

- The catalogue and every service answer are signed with **ECDSA P-384 / SHA-384** (DER), not Ed25519. Ed25519 is the
  licence scheme only.
- The package manifest pins every file with **SHA-384**. The catalogue's whole-file `sha256` is a download-integrity
  value the panel compares; it does not replace the pins.
- Activation is **restart-to-activate**: an install ends in the state *installed*, and the module serves after the next
  panel start. "Routes appear without a restart" is not the design.
- The lease credential is attached by an Sdk seam (`ILeaseRequestAuthorizer`) that the marketplace module cannot see
  through. When this section was first written the open repository had no implementation that attached one; the
  Licensing module's `LeaseRequestAuthorizer` now does (see R9 and "Amendment: the lease credential, the open operation
  and the kept name").

### Assets (additional to "Assets" above)

The pinned marketplace metadata key and the file that holds it (`/usr/local/maran/keys/marketplace-metadata.pub`); the
single-use download grant and its address; the marketplace switch row (`marketplace."MarketplaceSettings"`); the
per-module operation lock rows; the installed-package ledger and its monotonic version floor
(`marketplace."InstalledPackages"`, `marketplace."ModuleVersionFloors"`); the package directory
(`/var/lib/maran/marketplace`) while a download sits in it; the administrator's password at the moment of re-entry; the
audit log entries for the marketplace's actions.

### Trust boundaries and the signing chain: FOUR keys

| Key | Scheme | Signs | Custody | Public half | Verified by |
|---|---|---|---|---|---|
| Licence | Ed25519 | licences | the licence service | embedded in the panel | `Ed25519LicenceSignatureVerifier` |
| Release | ECDSA P-384 / SHA-384 | release manifests | offline | `installer/keys/release-signing.pub` | `installer/lib/50-artifacts.sh`, `maran update` |
| Module signing | ECDSA P-384 / SHA-384 | each `.ipm` manifest | offline, never on a build host | `installer/keys/module-signing.pub` (owner supplies) | `IpmSignatureVerifier` (injected key), in the loader |
| Marketplace metadata | ECDSA P-384 / SHA-384 | catalogue, module details, updates report, key list, download grant | the ONLINE service | `installer/keys/marketplace-metadata.pub` (owner supplies) | `CatalogueSignatureChecker` |

Four keys, two schemes, four purposes; a reviewer checks the one the code under review actually uses. The two P-384
verifiers share one implementation, `SharedKernel/Utilities/Signatures/EcdsaP384Signature` (key bytes and data in, a
bool out, never throws), so they cannot drift. The metadata key is the hot one: it signs every response. It therefore
must not be able to make code acceptable, and it cannot: a package is accepted only on the module-signing key's
signature over its manifest, which the loader checks. A stolen metadata key lets an attacker lie about what exists, who
is entitled and which digest a download should have; the panel's digest comparison and the loader's package signature
bound what that lie can install (it cannot make the panel run an unsigned or foreign-signed package, and it cannot make
a signed package run without an administrator's password). The key file is read at every check; a missing, unreadable or
malformed file, or any block in it that is not a P-384 public key, is `CatalogueKeyMissing` and the marketplace
believes nothing. Rotation: a key list from `/keys` is accepted only when a key trusted BEFORE it signed it, at most eight
keys, all P-384, held in memory until restart; the pinned file is the root again after a restart.

### The 20 attacks of issue #188, against the implemented slice

"Closed" marks a control that lives in the separately distributed loader, which this repository cannot show; for those
rows the open evidence is named and the rest is on the list of what the author could not verify. Test names are the
methods that exist in the tree; none has been run.

| # | Attack | Control | Test (inverse control in brackets) |
|---|---|---|---|
| 1 | Tampered package in transit or storage | TLS; the panel compares the downloaded file's SHA-256 and size with the signed catalogue's; the loader verifies the signature and every SHA-384 pin before anything is loaded (closed; the library is open) | `PackageDownloaderTests.A_digest_mismatch_is_refused`, `A_download_longer_than_declared_is_refused` [`MarketplaceClientTests.A_package_is_downloaded_through_the_grant_and_deleted_with_its_source`]; Packaging `A_flipped_byte_is_refused` |
| 2 | Unsigned, or signed by an unknown or revoked key | catalogue: pinned P-384 key set, signature checked before the JSON is parsed; package: module-signing key (closed loader) | `CatalogueSignatureCheckerTests.A_catalogue_signed_by_another_key_is_refused`, `An_unsigned_catalogue_is_refused` [`A_catalogue_signed_by_the_pinned_key_is_accepted`] |
| 3 | Valid signature, content swapped; archive tricks | `IpmReader` containment and the manifest census (open library); extraction is the loader's | existing Packaging rows 3 and 4 above |
| 4 | Downgrade to an older vulnerable signed version | a monotonic `HighestVersionSeen` per module that a replayed old catalogue cannot lower; the loader's `IpmDowngradeRefused` (closed); `notBefore` in the manifest | `ModuleVersionFloorTests`, `InstalledPackageLedgerTests`; `InstallEligibility` refuses a version under the floor |
| 5 | Replay of an old catalogue or an old grant | `signedAt` older than `CatalogueMaxAgeHours` (24 h default), `signedAt` more than five minutes ahead, or `validUntil` passed is `CatalogueStale`; a grant is refused when `expiresAt` is past; single use and binding to licence + server identity are the SERVICE's (not verifiable here) | `CatalogueSignatureCheckerTests.A_catalogue_older_than_24h_is_stale`, `A_catalogue_past_its_validity_is_stale`, `A_catalogue_signed_in_the_future_is_stale`; `PackageDownloaderTests.An_expired_grant_is_refused_before_any_request`; `MarketplaceClientTests.A_not_modified_answer_does_not_revive_a_stale_page` |
| 6 | Entitlement bypass | the grant request needs the lease credential and is refused by the panel without one; entitlement is shown from the signed answer and judged again by the service at the grant; the panel decides `canInstall` in one place (`InstallEligibility`) and the install command asks it again | `MarketplaceClientTests.A_download_without_a_lease_requests_no_grant`, `MarketplaceHttpTransportTests.A_request_needing_a_lease_is_not_sent_without_one` [`The_lease_is_attached_only_to_a_request_that_asks_for_it`] |
| 7 | Module id collision or shadowing | reserved ids, `Kind == Core` never from a package, forbidden assembly names: the loader (closed) and `PackagedModuleComposer` (open: `NotPackageable`, `IdTaken`, `AssemblyReserved`) | existing row 7 |
| 8 | Malicious module reaches the agent beyond its declaration | `AgentCapabilityGuard` runs on a packaged module's types at composition (`PackagedModuleComposer.JudgeCapabilities`); stated limit R3 | existing row 8 |
| 9 | Malicious module reads secrets or other tenants' data | **No control.** There is no per-module database role (decision list item 77, option (a): residual declared). A module reads `panel.env` and every schema as the panel does. This is R1, stated, not mitigated | none; see R1 |
| 10 | Malicious migrations | **Limited control.** A module's migrations run at the next start through the `maran migrate` path (`IModuleMigrations`), under the panel's connection, with `maran migrate guard` rules for expand-then-contract and the manifest's `SchemaName == Id`. A hostile migration is as powerful as a hostile assembly (R1) | existing row 10 |
| 11 | Customer or non-admin triggers install, remove, enable | every marketplace endpoint is `[Authorize(AdminOnly)]` and rate limited; the module is `Audience: AdministratorOnly`, hidden from other callers' catalogue. **The controller was not in the tree when this was written**: checklist item M2 is how a reviewer proves it | to be named by the controller's authorization tests; `ControllerAuthorizationDeclarationTests` must list it |
| 12 | Phished administrator installs a rogue package | only the configured host is contacted; the install dialog shows the SERVER's copy of the signed manifest summary, never package text; a ticked acknowledgement and the current password are required (`InstallNotAcknowledged`, `ReauthenticationFailed`); every request and refusal is audited; R2 stays (a legitimately signed, malicious publisher) | `PasswordReauthentication` tests in Identity; install-without-acknowledgement and install-without-password tests of the command (to be named once the command is in the tree) |
| 13 | A package's SPA bundle runs in the administrator's session | the frame, not an import: see "Packaged module screens" above. Decision D3 is **NOT RECORDED** in this note (slot below) | rows F1-F10 above |
| 14 | Lease lapse removes the core or data | see "Licence-lapse behaviour" below; the marketplace itself is an `Included`, Core module that does not depend on the lease to list or remove | existing row 12 |
| 15 | Loader failure or crash loop | `PackagedModuleComposer` turns every refusal into data; a failed run changes nothing that runs (`ModuleOperationRunner`: the previous version stays); the marketplace has no start-up call and no hosted service | `MarketplaceOutboundGateTests.Composing_the_registrations_with_the_switch_on_starts_no_work_and_sends_nothing`; existing row 13 |
| 16 | Unload leaks | activation and removal take effect at a start (restart-to-activate, D5/#82 option a); whether the closed loader's `UnloadAsync` stops a module sooner is its own (R6) | none open |
| 17 | Clock rollback to extend a lease or revive a catalogue | freshness uses the injected `IClock`, and a `signedAt` in the future is itself stale; a rolled-back clock can make an old answer look fresh (R5) | `A_catalogue_signed_in_the_future_is_stale` |
| 18 | Information leakage to the service | the request carries only the panel version, language, channel, the administrator's search text and cursor, the module id and version, on the update check the id, version and package digest of each module asked about, and the lease credential where the contract allows; **the search text typed by the administrator is a field the first draft of the item 10 text did not list; it is listed in the final text** | `MarketplaceHttpTransportTests.The_request_sends_the_language_and_the_panel_version_and_nothing_identifying`, `MarketplaceClientTests.The_update_check_sends_only_the_channel_and_the_installed_triples` |
| 19 | Log or audit forgery by a module | audit entries are constructed only by a module's own journal (`maran structure`); the loader stamping the source module id is closed; residual | C2 below [the positive control hits another module's journal] |
| 20 | Key compromise | metadata key: rotation by a list signed by an already trusted key, offline module-signing key, the pinned file is the root after restart; a compromised metadata key cannot sign a package. The operator's recovery procedure for a compromised key is NOT written here (owner) | `A_key_rotation_signed_only_by_the_new_key_is_refused` [`A_rotation_signed_by_a_pinned_key_makes_the_new_key_trusted`] |

**N1-N9, the rows the 20 do not name.**

| # | Attack | Control | Test (inverse control in brackets) |
|---|---|---|---|
| N1-N5 | The frame and bridge attacks (out-of-scope calls, reading the shell's storage, message spoofing, flooding, a package serving a script or document as an "asset") | rows F2-F6 of "Packaged module screens" above, owned by the frame work | named there |
| N6 | Catalogue replay, or a key swap through `/keys` | `signedAt`/`validUntil`; a rotation is accepted only when a previously trusted key signed it; a list with any non-P-384 entry, or more than eight entries, is refused whole | `A_key_rotation_signed_only_by_the_new_key_is_refused`, `An_announced_key_that_is_not_p384_refuses_the_list`, `MarketplaceClientTests.Refreshing_the_keys_accepts_only_a_list_a_trusted_key_signed` |
| N7 | Grant replay, or a grant to another server, or a grant aimed at another host (SSRF) | single use and binding are the service's; the panel refuses a grant whose address is not https on the configured host and port, with no user information, and whose size or digest disagrees with the signed catalogue | `PackageDownloaderTests.A_grant_url_on_another_host_is_refused`, `A_grant_url_that_is_not_exactly_the_configured_host_is_refused`, `A_grant_that_disagrees_with_the_catalogue_is_refused_before_any_request` [`MarketplaceClientTests.A_package_is_downloaded_through_the_grant_and_deleted_with_its_source`] |
| N8 | The administrator is phished into installing an attacker's listing | as row 12 | as row 12 |
| N9 | The marketplace is switched on silently by an update or an environment variable | default off; a database row, not a setting; audited; no environment path exists | `MarketplaceOutboundGateTests.With_the_marketplace_off_a_whole_session_makes_no_outbound_call` [`With_the_marketplace_on_the_same_session_reaches_the_wire`] |

### The download and grant path, as implemented

1. An administrator asks to install or update (a task is created; the work runs in `ModuleOperationRunner`).
2. `DownloadPackageAsync` asks the switch first. Off: `DisabledMarketplaceTransport` answers `MarketplaceDisabled` and
   the HTTP client factory is never touched.
3. The module's details are fetched (`GET /modules/{id}`), their signature verified over the exact bytes and their age
   judged; the answer must be about the requested module. The requested version must be listed and neither it nor the
   module revoked.
4. `POST …/download-grant`, lease credential REQUIRED (no credential: `LeaseRequired` before any request), never
   retried, never redirected. The answer is signature-verified before it is parsed.
5. `PackageDownloader` judges the grant before any request for the package: the address is https on the configured host
   and port; `expiresAt` is future; the size equals the signed catalogue's and is within `MaxPackageBytes`; the digest
   equals the signed catalogue's (compared as bytes, in constant time).
6. The package is fetched from the grant's address with `Accept` only: no lease, no panel version. Redirects: GET only,
   at most three, only while the next address is https on the configured host. Status must be 200; a declared
   `Content-Length` must equal the grant's size.
7. The body streams to `PackageDirectory/<random>.ipm`, created exclusively, mode 0600, SHA-256 computed as it is
   written; the read stops at one byte beyond the declared size; each read has a stall deadline. A short read, a long
   read or a digest mismatch discards the file; a dropped connection leaves no file.
8. The file is handed to the loader as an `IVerifiedModulePackageSource` carrying the `ExpectedModulePackage` (id,
   version, SHA-256, size) so the loader can refuse a package whose own signed manifest disagrees with what the
   catalogue announced (`ModulePackageNotAsAnnounced`). **The cross-check itself is the closed loader's**; the open
   `NullModuleLoader` refuses everything.
9. The loader verifies the package's signature against the module-signing key, its pins, its fit and its downgrade
   rules, stages it and asks for a restart. The temporary file is deleted when the source is disposed.

Nothing in steps 2 to 9 can change a module that is already running: a failure at any step leaves the previous version
running, because nothing is applied before the loader has staged and staging takes effect at the next start.

### The reauthentication window (auth surface)

Enabling or disabling the marketplace, install, update, remove and rollback ask for the administrator's current
password through the Sdk window `IReauthentication`, implemented by Identity (`PasswordReauthentication`). What it does,
read from the code:

- A wrong password, a locked login and an unknown user all produce the same failure, `ReauthenticationFailed`, of kind
  `Validation` (HTTP 400, never 401: the SPA signs a user out on 401, and a mistyped password is not a lost session).
- A locked login is refused BEFORE its password is checked, so the lock window is not a window of free guesses.
- A wrong password is fed to the same `IFailedLoginRecorder` as a failed sign-in, under the same row lock and with the
  operator's threshold, so it counts toward the sign-in lockout.
- Only a failure is journalled (`IdentityAuditActions.ReauthenticationFailed`), against the user's id. The entry carries
  no address and no user agent, because the seam is handed no request, and never the password. A success is recorded by
  the caller's own action entry.
- A correct password clears an earlier failed-attempt count, exactly as a successful sign-in does.

**Not controlled.** An unknown user and a locked login return without hashing a password, a wrong password does the
Argon2id verification: a small, measurable timing difference tells a caller holding a valid access token for a user id
that exists whether the account is locked or exists at all. The caller already holds that token, so what it learns is
the state of its own account; it is noted, not fixed. There is no per-request address on the failure entry, so a
guesser's origin is only in the access log. The per-address rate limit on the endpoint is what stands in front of a
stolen token.

### Licence-lapse behaviour (marketplace specifics)

The earlier "Licence-lapse behaviour" holds: after the grace, only modules with `Tier != Included` that the lease does
not cover degrade; data is kept; nothing is unloaded or deleted. For the marketplace: it is an `Included` Core module
and keeps listing, checking updates and removing without a lease. What a missing or lapsed lease takes away is the
grant, so a lapsed or absent lease means the marketplace can browse and cannot install. An entitlement word the service
returns (`LeaseExpired`, `NotPurchased`) is display and a prediction; the service decides at the grant.

### Supply chain

- **The cloud service.** It is outside this repository and the panel cannot verify it. The panel trusts its answers only
  as far as the metadata key's signature, the freshness window and the digests go; a compromised service that holds the
  metadata key can lie about the catalogue, hide revocations (for up to the freshness window) and refuse service, and
  cannot sign code.
- **Mirrors and object storage.** The download address must be on the configured host; a CDN behind that host is the
  service's business, and a substituted file is caught by the signed digest and the package signature.
- **A TLS-terminating middlebox** between panel and host sees the lease credential in transit (it is attached to
  catalogue, details, updates and grant requests) and can alter any answer, but cannot make an altered answer verify
  and cannot substitute a package. It can replay the last verified answer until it is 24 hours old, and it can learn
  which modules this server looks at and installs. The panel does not pin the TLS certificate; it relies on the
  operating system's trust store.
- **Typo-squatted ids and publisher account takeover** are the service's and the loader's (ids namespaced by publisher,
  `innovayse.*` reserved is the plan; v1 serves Innovayse-published modules only).
- **Build and signing infrastructure.** The module-signing key is offline and the metadata key online; their custody is
  the owner's and is not described by anything in this repository.

### Residual risks, extended

R1 to R7 above stand. Added or sharpened by the marketplace slice:

- **R1 (sharpened).** There is no per-module database role (decision item 77, option (a)); a signed module reads
  `panel.env` and every schema. The controls are the signature, the declaration shown to the administrator and the
  password, not a sandbox.
- **R8.** The frame bridge's residuals R-F1 to R-F5 above.
- **R9 (amended).** The lease credential. It is now defined (`docs/marketplace/api-v1.md` "Authentication") and
  implemented (`LeaseRequestAuthorizer`): `Authorization: Bearer base64url(payload) "." base64url(signature)`, the lease
  the licence service issued, handed back unchanged, attached only while the licence verifies and its lease stands Fresh
  or RenewalOverdue. What remains: the credential is a bearer, so whoever sees it can replay it until `validUntil` (14
  days at most) to obtain a grant, limited by TLS in transit and by the service binding a grant to the licence and the
  lease's `server` member, which a lease bound to no server does not carry; the panel cannot make a request-bound proof
  because it holds no private key. The component that fetches and writes `lease.json` is not in this repository, so a
  panel holds a lease only when one was placed there, and until then every grant request is refused by the panel as
  `LeaseRequired`.
- **R10.** The metadata key is online and signs everything; its compromise is an integrity failure of the catalogue (not
  of code) lasting until a panel release ships a new pinned key, because a rotation list is forgotten at restart.
- **R11.** A rolled-back server clock makes an expired answer look fresh (R5 applied to the catalogue).
- **R12.** The search text an administrator types is sent to the service.

### Amendment: the lease credential, the open operation and the kept name (follow-ups to #186 and #187)

Written with the code, without a build or a test run (coding-phase lock). Second review: OUTSTANDING (#331).

**What changed and what an attacker could do with it.**

1. *The lease credential is real.* `LeaseRequestAuthorizer` (Licensing) attaches `Authorization: Bearer
   <base64url(payload)>.<base64url(signature)>` built by `LeaseBearerToken` from the verified bytes of `lease.json`
   (`FileLeaseSource.ReadSignedAsync`, which returns them only for a lease whose Ed25519 signature held against the
   pinned lease key). It is attached only when `LicenceCoverageCache` says the licence is valid and the lease stands
   Fresh or RenewalOverdue. It adds no other header, body or query member, and it holds nothing the service did not
   itself put in the lease (licence id, instants, the bound machine id). *Attacker E (non-admin)*: the authorizer is
   reachable only from the marketplace transport, which only an administrator's action drives; no endpoint returns the
   token. *Attacker A (network)*: sees the bearer only if TLS is broken; a replay yields a grant bound by the service to
   the licence and server, and the package then still has to match the signed digest. *Attacker H (administrator
   editing local files)*: a forged or edited lease fails the signature and is never presented; a lease from another
   licence is never presented. *Leak*: the token is built per request from the file, never stored in a field, logged or
   journalled; `SignedLeasePayload` states that. The licence's decryption key remains out of this path.
2. *The open operation is stored, not remembered.* The module's claim row (`marketplace."ModuleOperationLocks"`) now
   names the operation, version and task (nullable columns, expand-only migration `KeepOperationTaskAndModuleName`).
   A retry of the same request is answered with the task from the row, after a restart or from another process, and
   creates nothing; a different operation or version is still `ModuleOperationInProgress`; a claim older than thirty
   minutes is stale and is not a repeat. The row holds no secret: module id, one of four operation names, a version and
   a task id. *Risk*: a repeat is answered only after the administrator's password was re-checked, so the task id is not
   disclosed to anyone who could not start the operation.
3. *The installed list shows the catalogue's name.* `InstalledPackages.DisplayName` (nullable, 128) keeps the signed
   catalogue's name for the module at staging, shortened by `ColumnText.Fit`; the installed list reads it from the
   ledger, so the screen still makes no marketplace call. It is display text from a signed answer, rendered by the SPA as
   text; a module the ledger never named is shown by its id.

**Differences a reviewer must read.** (a) `OpenModuleOperations` (process memory) is deleted; its tests moved to the
gate's. (b) `ModuleOperationRequested` gained a `DisplayName` member; it travels the local in-memory queue only. (c) A
claim that went stale and was taken over cannot name its task (`AttachTaskAsync` answers false) and then a repeat meets
the conflict, which is the safe side.

**What the author could not verify.** Nothing here was built or run. Whether `Base64Url` and the header assignment behave
as written against a real service is unmeasured; the service side does not exist in this repository. The EF migration and
its designer were written by hand to match the model snapshot, which only `maran migrate check` can confirm.

### Decisions and approvals

| Slot | State |
|---|---|
| Owner approval of the rules/security.md item 10 change (the marketplace as a second documented outbound destination, host `api-marketplace-maran.innovayse.com`) and of the spec section 13 wording (decision list items 46 and 47) | **RECORDED (decision list items 46, 47 option a, 2026-11-09)** |
| The ACME paragraph added at the end of item 10 (operator-configured destinations are not Innovayse destinations) | an observation for the owner to confirm; NOT separately recorded |
| D3, how a packaged module's screen runs (isolated frame vs. same-origin import) | **NOT RECORDED** (the frame work owns this slot) |
| D4, remove keeps the module's data (no delete option) | applied as the recommended option of the decision list; no separate owner record in this repository |
| D5(b), the current password re-entered for enable, install, update, remove, rollback | applied as the recommended option of the decision list; no separate owner record |
| D6, a separate P-384 metadata key, `installer/keys/marketplace-metadata.pub` supplied by the owner | applied as recommended; **the owner has not yet supplied the file** |
| D7, the switch off by default, a database row | applied as recommended |
| The daily security-patch timer is a different exception and stays NOT RECORDED in its own note | unchanged |

### The installer's privileged step (key directory and package directory)

Installer steps are a sensitive surface (rules/security.md). Two additions, both in `installer/lib/40-user.sh`:

- `create_service_owned_directory /var/lib/maran/marketplace 0700`. The parent belongs to the unprivileged service
  account, which can therefore plant a symbolic link at this name aimed at a root-owned directory; a plain
  `install -d -o` run by root would follow it and re-own the target. The helper unlinks anything that is not a real
  directory, creates the directory with its final mode, uses `chown -h` (which never dereferences), and aborts the
  install unless the result is a real directory owned by the service account with exactly the mode asked for. A narrow
  window between its final test and the `chmod` is stated in the helper; `chmod` can only tighten to 0700 and the gate
  reads the result again. The panel also creates the directory itself, mode 0700, on first use.
- `install_marketplace_key`. Copies `installer/keys/marketplace-metadata.pub` to `/usr/local/maran/keys/` (directory
  root:root 0755, file root:root 0644) ONLY when the bundle carries it. When the bundle does not and no key is already
  installed it prints "marketplace key not bundled: the marketplace will show catalogues as unverified" and continues.
  The service account cannot write the directory or the file.

Uninstall: `/var/lib/maran` and `/usr/local/maran` are removed whole by `installer/uninstall.sh`, which takes the
package directory and the key with them; no line of its own was needed. The polygon's directory census
(`docker/polygon/asserts/parts/directory-layout.sh`) lists `/var/lib/maran/marketplace` as owned by the service account
with mode 0700, and its own count guard (`checked -eq 17`) was raised from 16 with the list. The key install has no
polygon assertion; it is not observed there.

### The four locks (the marketplace ships behind the spec change)

- **G1, the host comes only from the options.** The default is the documented host; there is no second place a host
  can come from, and a grant or redirect address is judged against it (`MarketplaceHostPolicy.IsConfiguredHost`: https,
  no user information, same IDN host, same port).
- **G2, the switch selects the transport.** Off: `DisabledMarketplaceTransport`, which cannot reach the network. The
  cache is not read while off.
- **G3, invalid options reach no socket.** (As implemented, which differs from the plan's wording.) The named HTTP
  client is registered whenever the module is composed; what is lazy is the options check: `MarketplaceOptionsReader`
  validates before every request and `MarketplaceHttpTransport` fails with `MarketplaceHostNotConfigured` before it asks
  the factory for a client. Start-up never fails because of it.
- **G4, the architecture test `MarketplaceOutboundIsGatedTests`:** only `Services/MarketplaceHttpTransport.cs` touches an
  HTTP client type; only the registration extension calls `AddHttpClient`; the module has no code that starts work on its
  own; each with a planted-source positive control. `MarketplaceOutboundGateTests` (module tests): a scripted session
  with the switch off makes zero calls and the same session switched on reaches the wire; composing the registrations
  with the switch on sends nothing.

Evidence commands, each with its positive control (a search that found nothing is a check only if the same search can
find something):

- C1, nothing but the transport names an HTTP client type in the module:
  `grep -rnE "\b(HttpClient|IHttpClientFactory|SocketsHttpHandler|HttpClientHandler)\b" backend/src/Maran.Modules/Marketplace --include=*.cs | grep -v /obj/ | grep -vE ':[0-9]+:\s*///?' | grep -v "Services/MarketplaceHttpTransport.cs"`
  prints nothing; the positive control is the same command without the last `grep -v`, which prints the transport's lines
  (6 when this was written).
- C2, audit entries are constructed in one place: `grep -rn "new AuditEntry" backend/src/Maran.Modules/Marketplace --include=*.cs | grep -v /obj/`
  prints exactly `Services/MarketplaceAuditJournal.cs`; the positive control is the same search over
  `backend/src/Maran.Modules`, which also prints other modules' journals.
- C3, the module references only the Sdk and the SharedKernel: `grep -E "ProjectReference" backend/src/Maran.Modules/Marketplace/Maran.Modules.Marketplace.csproj`
  prints two lines, `Maran.Sdk` and `Maran.SharedKernel`; the positive control is the same command on
  `backend/src/Maran.Modules/Dns/Maran.Modules.Dns.csproj`, which prints those two and the agent client as a third.
- C4, no key file is committed: `ls installer/keys` prints `release-signing.pub` only (no `marketplace-metadata.pub`, no
  `module-signing.pub`); the positive control is that it prints that one file.
- C5, nothing in the module starts background work: `grep -rnE "IHostedService|BackgroundService|PeriodicTimer|new Timer\(" backend/src/Maran.Modules/Marketplace --include=*.cs | grep -v /obj/`
  prints nothing; the positive control is the planted-source test above, and the same command over
  `backend/src/Maran.Modules` printing other modules' hosted services.

### What a reviewer must check: issue #188's items 1-14, answered for this slice

| # | Question | Answer for the marketplace slice |
|---|---|---|
| 1 | Is every byte loaded covered by a signature verified against a key in the embedded set? | The panel loads nothing. Bytes it hands to the loader are covered by the signed digest; the loader's signature check is closed (not verified here). |
| 2 | Does any code path load before verification completes? | The panel's side: no. The loader's side is closed. |
| 3 | Are root, release and licence keys distinct, and is rotation tested? | Four keys, table above; rotation tests listed in N6. |
| 4 | Archive extraction | Not the panel's; the open `IpmReader` is covered by the earlier rows. |
| 5 | Reserved ids and Sdk/framework shadows | `PackagedModuleComposer` (open) and the loader (closed). |
| 6 | Downgrade floor, revocation and `notBefore` in one code path | The floor and revocation are in `InstallEligibility`, asked by the screen AND by the install command; `notBefore` is the loader's. |
| 7 | Is the lapse path tested end to end? | Row 12 above; the marketplace's own behaviour on a missing lease is held by `A_download_without_a_lease_requests_no_grant`. |
| 8 | Is every endpoint AdminOnly and audited, including refusals? | Audit: `MarketplaceAuditJournal` is the only constructor; AdminOnly: see M2 (controller not in tree when written). |
| 9 | Per-module DB role created and used | No (R1). |
| 10 | The vue.md decision recorded and implemented as decided | D3 NOT RECORDED. |
| 11 | No new listening port, daemon or outbound host besides the amended allow-list | C5; the one host; item 10. |
| 12 | Secrets never logged or in URLs | The lease is attached by the Sdk seam and never seen by the module; grant id and address are `SensitiveString`; the transport logs an attempt number only; the runner logs an exception's type, never its text. `grep -rn "Reveal()" backend/src/Maran.Modules/Marketplace --include=*.cs \| grep -v "///"` prints one line (the downloader) and its positive control is the same search over `backend/src/Maran.SharedKernel`, which prints the type's own uses. |
| 13 | Gates have inverse controls and were mutation-scored | Inverse controls: named in the tables. Mutation: NOT RUN (see T2). |
| 14 | The "could not verify" list is honest | See below. |

New items for the marketplace:

- M1. The switch is off on a fresh database: read `MarketplaceSettings.Off()` and `MarketplaceSettingsStore.ReadAsync`
  (no row reads as off) and `MarketplaceOutboundGateTests`. No environment variable reads or writes it.
- M2. Every marketplace controller action carries `[Authorize(AdminOnly)]` and `[EnableRateLimiting(Api)]`, and
  `ControllerAuthorizationDeclarationTests` lists the controller. The controller was not in the tree when this was
  written.
- M3. The transport's three refusals are each beside an accepted case: off-host redirect [same-host redirect],
  over-cap body [at the cap], grant POST not retried [GET retried] (`MarketplaceHttpTransportTests`).
- M4. The pinned key's place is one option default and no other literal: `grep -rn "marketplace-metadata" backend/src/Maran.Modules/Marketplace --include=*.cs | grep -v /obj/ | grep -v "///"`
  prints exactly `Options/MarketplaceOptions.cs` (the `MetadataKeyPath` default); the positive control is
  `grep -rn "MetadataKeyPath" backend/src/Maran.Modules/Marketplace --include=*.cs | grep -v /obj/`, which also prints
  the checker and the validator, so the search can see a use. No key bytes are embedded in code.
- M5. The package directory is created 0700 and files in it 0600 (`PackageDownloaderTests.The_downloaded_file_is_owner_only`),
  and the installer's directory step is the symbolic-link-safe one above.
- M6. The re-entry window: read `PasswordReauthentication` against the facts listed in its section.
- M7. A restart is requested through `IPanelRestart`, which audits first; nothing in the marketplace stops the process.
- M8. The search text is the only administrator-typed value sent; confirm item 10's list in rules/security.md names it.

### What the author could not verify (marketplace slice)

- **The browser handshake** of the frame (not mine; see the frame section's list).
- **The closed loader**: that it verifies before it loads, that it enforces the `ExpectedModulePackage` cross-check,
  the downgrade rule and the reserved names, and what its unload does.
- **The lease issuer**: how long a lease the service issues lives in practice, whether it is bound to a server, that the
  service verifies its own signature and `kind` before honouring the bearer, and that it binds a grant to the licence
  and server. The panel side (what is attached, when, and that nothing else is added) is covered by
  `LeaseRequestAuthorizerTests` and `LeaseBearerTokenTests`, which have not been run.
- **The service side**: that grants are single use and bound to a server identity, that entitlement words are honest,
  that signing happens only over what the service means to say, and what the service logs about a request.
- **Key custody**: the owner has not yet supplied `installer/keys/marketplace-metadata.pub` or
  `installer/keys/module-signing.pub`. Until both exist, nothing in this repository verifies against a production key;
  the tests sign with a per-run key.
- **nginx 1.25.1 or later** parsing of the vhost (the vhost's own disclaimer).
- **Any run at all**: no build, no test, no mutation, no polygon, no installer run has happened for this slice. The
  installer's key step and directory helper were checked only by reading and by `bash -n`.
- **Real TLS behaviour** against the real host; the transport was exercised against scripted handlers.
- **The controller, commands and screens** were being written in parallel and are not described by this note beyond the
  contract (`docs/marketplace/api-v1.md` and the plan); T2 re-reads them.

### T2 - implemented-diff review and mutation table

Status: **NOT RUN.** The lead fills this in after the gate (rules/testing.md: `maran mutate`, whole suite, the named
test that died, or SURVIVED with its witness; restore with a fresh mtime and `cmp`). Rows, from the plan's mutation
pass:

| # | Mutation | Status | Killed by |
|---|---|---|---|
| 1 | Delete the catalogue signature check | NOT RUN | |
| 2 | Accept an off-host redirect | NOT RUN | |
| 3 | Drop the response size cap | NOT RUN | |
| 4 | Drop the digest comparison | NOT RUN | |
| 5 | Drop the `Expected` cross-check call | NOT RUN | |
| 6 | Swap the pinned key for the module-signing key | NOT RUN | |
| 7 | Remove the `X-Maran-Frame` comparison in the middleware | NOT RUN | |
| 8 | Add `allow-same-origin` to the sandbox string | NOT RUN | |
| 9 | Make the transport register when the switch is off (G2), or make the grant retry | NOT RUN | |

Rows 5 and 8 have no .NET mutant that this repository's tests can see in the closed loader and in the nginx header
respectively; the sandbox string and the nginx header are covered by the Playwright probe and the polygon assertion, and
a protection with no possible mutant is a claim to prove, not to assert.


## Service implementation language (owner decision)

The cloud service at `https://api-marketplace-maran.innovayse.com` (catalogue, downloads, licence leases) is built in C# (.NET) under
the conventions of `rules/csharp.md`. It is outside this repository and is not reviewed by this note; the panel-side
contract it must meet is `docs/marketplace/api-v1.md`. The panel verifies every signed answer it receives, so the
service's language changes none of the controls above.

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

Marketplace service client, download path, reauthentication window and installer key and directory step (#186-#188): Second review: OUTSTANDING (#331). Privileged surfaces for the hand-off: the new outbound destination, the pinned metadata key and its custody, the path from download to the closed loader, `PasswordReauthentication`, and `installer/lib/40-user.sh` (`create_service_owned_directory`, `install_marketplace_key`).

Frame, bridge and scope middleware (#185): Second review: OUTSTANDING (#331). Privileged surfaces for the hand-off: the nginx `/module-frames/` location (installer), the anonymous frame route, the shell relaying API calls for third-party code, and `ModuleFrameScopeMiddleware`.

Lease credential, open operation record and kept module name (follow-ups to #186 and #187): Second review: OUTSTANDING (#331). Privileged surfaces for the hand-off: `LeaseRequestAuthorizer` and `FileLeaseSource.ReadSignedAsync` (the licence lease leaves the licence module as a bearer credential), and the marketplace's migration `KeepOperationTaskAndModuleName`.
