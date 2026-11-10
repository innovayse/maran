# The `.ipm` module package format

A module that is not part of the panel arrives as one file, `<id>-<version>.ipm`. This page specifies the file,
the rules the open verification library (`backend/src/Maran.Packaging`) holds it to, and the tooling that writes it
(`maran ipm`). The module contract itself — what a module declares about itself — is in
[`docs/modules/module-contract.md`](../modules/module-contract.md); this page is about the container.

**A verified package is trusted code.** Verification answers "are these the bytes the holder of the module-signing key
signed, and do they fit this panel". It does not sandbox anything: a signed module runs inside the panel process
with the panel's privileges. What the format and the library reduce is WHO can sign and WHAT the loader accepts.
Nothing here loads code: the library reads bytes and answers; loading is the loader's work, after verification.

## The archive

A `.ipm` is a ZIP file with this layout and no other:

```
ipm.json            the manifest (always)
ipm.sig             the signature of ipm.json (always, once signed)
lib/<Entry>.dll     the module assembly named by entryAssembly
lib/<culture>/<Entry>.resources.dll     its satellite resource assemblies (ru, hy, ...)
lib.enc             instead of lib/*: the encrypted library (reserved, see below)
spa/entry.js        the interface bundle's entry, a classic script, when the module has a screen (see below)
spa/locales/<locale>.json     one file per translated locale; en is always present
```

Only the module assembly and its satellite resource assemblies are packaged. Every other assembly a publish copies
(the Sdk, the SharedKernel, the framework, EF Core, Npgsql, Wolverine) is the panel's own: a package that shipped one is
refused, and a private dependency is a build failure of `maran ipm package`, not a surprise at install.

Entry names are contained by rule, not by checking the text of a path later: a name is 1 to 255 characters, at most 16
segments of `A-Za-z0-9._-`, never absolute, never `..` or `.`, no `\` and no `:`. Duplicate names and names that
differ only by case are refused. An entry that is not a plain file (a symbolic link, a directory) is refused.
The archive is deterministic when `maran ipm package` writes it: entries sorted bytewise (`ipm.json` first), every
time set to `SOURCE_DATE_EPOCH` (or the last commit's time), every file mode `0644`, no platform extra fields, no
directory entries.

## `ipm.json`

The signature covers the bytes of `ipm.json` exactly as stored, so there is no canonical form for a signer and a
verifier to disagree about. Reading is strict: a duplicate member, an unknown member, a missing member (a nullable
member is present as `null`), a null where none is allowed and a number where a name belongs are all refused.
Enumerations are written as their names. `maran ipm package` writes the file with its keys sorted and a trailing
newline.

| Member | Type | Meaning |
|---|---|---|
| `formatVersion` | integer | The revision of this format; the library reads `1` and refuses any other. |
| `id` | string | The module id: lower-case letters and digits, beginning with a letter. Equals the schema name when `schemaName` is set. |
| `displayNameKey` | string | The resource key of the module's name, resolved from its own `DisplayNames` resources. |
| `version` | string | This build's version, Semantic Versioning 2.0.0. |
| `tier` | `Included` \| `AddOn` \| `PlanGated` | The licence tier the module ships under. |
| `audience` | `Everyone` \| `AdministratorOnly` \| `AdminOrReseller` | Who the module's screens are for (presentation only; every endpoint still authorizes). |
| `minPanelVersion` | string | The oldest panel that can host it. |
| `maxPanelVersion` | string or null | The newest panel that can host it; not below `minPanelVersion`. |
| `notBefore` | timestamp or null | The package is refused before this instant. |
| `schemaName` | string or null | The PostgreSQL schema the module owns, equal to `id`; null for a module with no tables. |
| `agentCapabilities` | array | The `AgentCapability` values the module may drive. The loader refuses a module whose types reach past this list. |
| `permissions` | array | The authorization policy names its controllers use; each must be a name in the Sdk's vocabulary. |
| `menu`, `routes` | arrays | What the module contributes to the navigation, as data. |
| `dependencies` | array of `{id, range}` | The modules it needs and the versions of each it works with. |
| `migrations` | `{from, to}` or null | The migrations the package carries; present exactly when `schemaName` is. `from` is null when the package also creates the schema from nothing (a package always brings the whole chain). |
| `entryAssembly` | string | The assembly file name; pinned as `lib/<entryAssembly>`. |
| `spa` | `{entry, integrity, locales}` or null | The interface bundle; see below. |
| `encryption` | `{alg, keyId}` or null | The encrypted-library reservation; see below. |
| `files` | array of `{path, sha384, size}` | Every other entry of the archive, its SHA-384 (lower-case hex) and its size in bytes. |

A module that declares `kind: Core` is never a package: the manifest has no `kind` member, and a package is always
`Packageable`.

`files` is a census, not a hint. A listed entry that is missing, a stored entry whose hash or size differs, and an
entry present in the archive that is not listed are each refused. `ipm.json` and `ipm.sig` are the only entries not
listed: the manifest cannot hash itself and the signature cannot cover itself.

## The signature

`ipm.sig` is the ECDSA P-384 / SHA-384 signature of the bytes of `ipm.json`, in DER form — what
`openssl dgst -sha384 -sign` writes. That is the release scheme (`sign_release_file` in `scripts/lib/signing.sh`,
rules/security.md item 9), used with a THIRD key: the module-signing key. The licence key (Ed25519), the release key and
the module-signing key are three keys with three purposes and are never shared, so a key compromised in one role does
not sign in another.

The public key is INJECTED into the verifier (`IpmSignatureVerifier.FromPem`) and is never embedded in the library, so the
open library cannot be taught to trust a key by editing it. The repository will carry
`installer/keys/module-signing.pub` once the owner generates the real pair; until then `maran ipm verify` takes
`--pubkey`. The private key is never in this repository and `maran ipm sign` never prints, copies or stores it.

The order of checks is fixed: read the archive safely, check the signature over `ipm.json`, only then parse the manifest,
validate it, check that it fits this panel, and last compare every entry with its pin. Nothing before the signature is
believed.

## Size caps

Applied by `IpmReader` before anything in the archive is believed, because a package is hostile bytes until its
signature has been checked and the signature can only be checked on what the reader hands over.

| Cap | Value | Why this number |
|---|---|---|
| Entries | 512 | A module is a handful of assemblies plus an interface bundle; the cap keeps a listing from being a denial of service while leaving room for a bundle of many small files. |
| One entry, unpacked | 64 MiB | Larger than any module assembly we have seen by two orders of magnitude, small enough that one entry cannot exhaust memory. |
| All entries, unpacked | 256 MiB | Memory is bounded by this whatever the archive claims; the reader counts the bytes it reads and does not trust a declared size. |
| Compression ratio | 100 to 1, judged from 1 MiB | A decompression bomb is a small download that unpacks huge. A tiny entry of zeros legitimately compresses by a large factor, so entries under 1 MiB are not judged. |
| Manifest | 1 MiB | Read first; the manifest is metadata and never legitimately large. |

These are `IpmLimits.Default`. A test may construct a smaller `IpmLimits`; a panel does not.

## Versions and compatibility

- `version`, `minPanelVersion` and `maxPanelVersion` are strict Semantic Versioning 2.0.0 (`SemanticVersion` in the
  SharedKernel); `dependencies[].range` is a space-separated set of comparators, `>=1.2.0 <2.0.0` (`VersionRange`).
- A package is refused when the panel is older than `minPanelVersion` (`IpmPanelTooOld`), newer than `maxPanelVersion`
  (`IpmPanelTooNew`), or before `notBefore` (`IpmNotYetValid`).
- An unknown `formatVersion` is refused (`IpmFormatVersionUnknown`) before any other member is read, so a future format is
  reported as such and not as a malformed manifest.
- Installing over an installed module refuses an older version (`IpmDowngradeRefused`) unless the operator states a
  reason; a version that cannot be compared is treated as older (fail closed). Every dependency must be installed in an
  accepted version (`IpmDependencyUnsatisfied`).
- Every refusal is a typed code with text in English, Russian and Armenian (`Maran.Packaging/Resources/ErrorMessages`),
  never an exception.

## The `spa` folder

A module with a screen of its own ships its interface here. The panel never loads that script into its own
page: the screen runs in a sandboxed frame that the panel builds, and the script talks to the panel only
through a message bridge.

**What the folder holds.** An entry script, an integrity string pinning it, static assets (`.js`, `.css`, `.json`,
`.png`, `.jpg`, `.jpeg`, `.webp`, `.woff2`, `.svg`) and one JSON file of strings per locale.

- **The entry is a classic script**: a `.js` file placed directly in `spa/` (`spa/entry.js`), loaded with a plain
  `<script>` tag, never `type="module"`. A module author bundles to one classic script (for example an IIFE).
  An entry that is not a `.js` file directly under `spa/` (`.mjs`, a nested file, anything else) is refused
  (`IpmSpaEntryNotClassicScript`).
- **No document is allowed under `spa/`.** A file ending in `.html` or `.htm`, anywhere in the folder, is refused
  (`IpmSpaDocumentForbidden`), whether or not the manifest declares a bundle. The panel generates the one document the
  script runs in; a package can never make the panel serve a page of its own.
- `spa.integrity` is `sha384-` followed by the base64 SHA-384 of the entry file (the Subresource Integrity form), and must
  equal the entry's pin in `files`. The generated document carries it on the script tag, so the browser checks the
  very bytes the signature covered. `spa.locales` always includes `en` and lists exactly the locales that have a
  `spa/locales/<locale>.json` entry. A package with no `spa` member has no `spa/` entry at all, and the reverse.

**How the panel serves it.** An installed module that is active is framed from `/module-frames/{id}/{version}/`: the
folder URL answers a document the panel generates (a page whose only content is the entry script with its integrity
value, nothing inline), and every other path answers the matching file of the package, with a content type the panel
decides from a fixed extension list and never one the package supplies. The panel shows a module's screen only for
the version that is running; a module that is degraded or failed is not framed, and an unknown path is a plain 404.
The address is built by the panel, never taken from the package.

**What the frame can and cannot do.** It runs in an iframe with `sandbox="allow-scripts"` and an opaque origin: it has no
access to the panel's page, cookies, storage or session, and the policy it is served under (`connect-src 'none'`)
forbids every network request, so the script cannot call the API, a server or any other site itself. Its one way out
is the bridge, a message channel the panel opens after a handshake. Over it the script can ask the panel to
perform an API call (the panel makes it with the signed-in user's own session, and the server refuses any endpoint
that does not belong to this module), to resize the frame, to move to another route the module declares, and to show
a short plain-text notification; the panel tells it the interface language, the colour scheme, the user's role name
and later changes to the first two. Nothing else crosses: no token, cookie or user record is ever sent into the frame.
Because the frame cannot fetch, a script that wants its translations carries them inside the bundle, and the
`spa/locales` files are the same strings the panel reads. The bridge's client for authors is the package
`frontend/packages/module-bridge` (`connectToShell`); the sample's `spa/entry.js` implements the same protocol by hand.

## Encryption (reserved)

`encryption` and the `lib.enc` entry are reserved for distribution access control: a library encrypted with a key held
in the licence lease. This is access control and not integrity (the signature is) and not secrecy against the licensee
(the panel is source-available). The library only checks that the shape is consistent — `lib.enc` present, no plain
`lib/` entry, a well-formed algorithm and key id; decrypting is the closed loader's step. `maran ipm package` does not
write encrypted packages.

## Key rotation and revocation

- **Rotation.** The module-signing key is replaced by distributing a new public key to panels by a release signed with the
  release key, and signing new packages with the new private key. A package signed with a retired key fails verification
  against the new key; the old public key is kept as long as a panel may hold a package signed with it.
- **Revocation.** The marketplace serves a revocation list, signed by a key the panel already pins. Its entry format is
  `RevocationList` in `Maran.Packaging`:

  ```json
  {
    "formatVersion": 1,
    "sequence": 42,
    "issuedAt": "2026-11-07T00:00:00+00:00",
    "revoked": [
      { "moduleId": "hello", "range": "<1.2.0", "reason": "vulnerability" }
    ]
  }
  ```

  `sequence` strictly increases per issued list and a panel never accepts a lower one than it holds. `range` is a
  `VersionRange`; a minimum-version floor is the same thing written `<2.1.0`. `reason` is a short machine token such as
  `key-compromise` or `vulnerability`. The library defines the shape; serving the list, checking its signature and the
  floor on `sequence`, and applying it at install and at every start are the loader's work.

## Writing and checking a package: `maran ipm`

```
maran ipm package --project <csproj> --out <dir> [--spa <dir>] [--source-date-epoch <n>]
maran ipm sign    --package <file.ipm> [--key <pem>]       # or $MARAN_MODULE_SIGNING_KEY
maran ipm verify  --package <file.ipm> --pubkey <pem> [--panel-version <semver>]
maran ipm selftest
```

(`maran module <Name>` is unrelated: it scaffolds a module inside this repository.)

- `package` publishes the module deterministically (`Deterministic`, `ContinuousIntegrationBuild`, a path map), takes the
  manifest from the module itself through `Maran.Host --cli module-manifest <assembly>` — the manifest is never typed —
  adds the pin of every entry and the interface bundle, and writes an UNSIGNED `<id>-<version>.ipm`. Two builds of one
  source are the same bytes.
- `sign` adds `ipm.sig`. The key must be an ECDSA P-384 key; the release key is the same scheme and cannot be told apart
  here, so keeping the two apart is a matter of custody.
- `verify` re-implements nothing: it runs `Maran.Host --cli module-verify`, which calls the library. It answers
  `VERIFIED: <id> <version>` or `REFUSED: <code>`.
- `selftest` packages `backend/samples/hello-module` twice and requires equal bytes, signs it with a throwaway key, requires the
  library AND openssl to accept the signature, then requires each of these to be refused for its own reason: one flipped byte
  of the assembly (`IpmHashMismatch`), a stripped signature, another key, an edited manifest, and an entry the manifest
  does not list (`IpmUnlistedEntry`). It runs in the `backend` workflow and prints what it cannot see: hostile archives are
  the library's tests', and two machines building different bytes from one commit is unobserved by a run on one.

## The sample

`backend/samples/hello-module` is the reference module and the one packageable fixture the suites share. It sits in
`backend/samples/`, beside `src/` and `tests/`, and outside `ModuleRegistry`: it is packaged, not composed, and is not a
project of `Maran.sln` (the architecture tests reference it, so the solution build compiles it under the backend's own
build settings, and not under `src/`'s production-only banned-API list). It declares `Kind = Packageable`, the `hello`
schema with one table and one migration, one administrator-only endpoint (`GET api/v1/hello`), a `DisplayNames`
resource triple, and the interface bundle (a classic script that renders the module's answer). It references the Sdk and the SharedKernel only; `SampleModuleContractTests`
holds it to the same module contract as the modules the panel composes.
