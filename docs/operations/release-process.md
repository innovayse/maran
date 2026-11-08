# Release process

How a Maran release is cut, what it contains, and how anyone can check it. Maintainers read the first
half; operators who want to verify a download without trusting the page it came from read the second.
How a server verifies and installs a release is in `docs/operations/panel-update.md`; this page is
about producing and auditing one.

## What a release consists of

One GitHub release per tag, with these assets:

| Asset | What it is |
|---|---|
| `manifest.json` | the release manifest: version, channel, release date, notes URL, and for every `<component>-<arch>` its address and sha256 |
| `manifest.json.sig` | the signature of `manifest.json` (ECDSA P-384 over SHA-384, DER) |
| `integrity-manifest.json` and `.sig` | the signed list of installed-file hashes, signed with the same key |
| `api-<arch>.tar.gz`, `agent-<arch>.tar.gz`, `frontend-<arch>.tar.gz` | the three components, for `x86_64` and `aarch64` |
| `maran-offline-<version>-<arch>.tar.gz` | one offline bundle per architecture, for `maran update --offline-tarball` |
| `api.cdx.json`, `agent.cdx.json`, `frontend.cdx.json` | one CycloneDX SBOM per component |
| `openapi.json` | the OpenAPI description of the API, generated from the tagged tree; **not signed** |
| `SHA256SUMS` | sha256 of every other asset; NOT signed, a convenience only (see below) |

The release notes are the body of the GitHub release, generated from the merged pull requests since
the previous tag, not an attached file.

A published release always carries both architectures; `maran release build --publish` and `sign
--publish` refuse anything else.

## Cutting a release

Tags are `vMAJOR.MINOR.PATCH` for the `stable` channel and `vMAJOR.MINOR.PATCH-beta.N` for `beta`
(for example `v1.0.0` and `v1.0.0-beta.4`). The workflow refuses a tag that is not of that form, or
whose commit is not reachable from `origin/main`.

**Those two checks are hygiene, not controls against someone who can push a tag.** The workflow file
that runs is the one in the tagged commit, so a tag pusher can change both checks away. The real
gates are settings the owner configures, once, in the repository:

- an environment named `release`, restricted to `v*` tags, with **required reviewers who are not the
  person who pushed the tag**;
- the environment secret `MARAN_RELEASE_SIGNING_KEY`: the private half of the key whose public half
  is `installer/keys/release-signing.pub`. The key never lives in the repository and is not a
  repository-level secret, so a job outside the environment cannot read it;
- a **tag ruleset** limiting who may create `v*` tags.

Pushing a tag matching `v*` starts `.github/workflows/release.yml` (it never runs on a pull request).
Jobs, in order:

1. **check**: validates the tag name and that its commit is an ancestor of `main`. It validates tag
   and ancestry only; it does not look at CI status, so a red commit on `main` can still be tagged.
2. **build**: the three components for both architectures with `SOURCE_DATE_EPOCH` set to the tag
   commit's time, and the release notes. It holds no secret and no write permission. Before it runs
   the aarch64 build it registers emulation with `docker run --privileged --rm tonistiigi/binfmt
   --install arm64`.
3. **sbom**: the three SBOMs, in a job of their own, so the third-party generators never run beside
   the build. The **openapi** job runs beside it for the same reason: it generates `openapi.json`
   (below) and holds no secret.
4. **sign**: bound to the `release` environment, so it waits for a reviewer. It writes the key to a
   0600 file, runs `maran release sign --key <file> --publish`, deletes the file in a step that
   always runs, then verifies and packages the offline bundles. It has read-only repository access.
5. **publish**: the only job with `contents: write`, with no environment and no access to the key. It
   runs `maran release verify --publish` (both architectures, signature, checksums) and only then
   creates the GitHub release, with the notes as its body. A `-beta.N` tag becomes a pre-release.

A beta that goes out while second reviews of privileged changes are OUTSTANDING needs the owner's
written decision for that release, and its notes must say so (rules/security.md, "The one recorded
exception"). A stable release is never covered by it.

## Verifying a downloaded release by hand

Needs only `openssl` and `sha256sum`. Take the public key from a checkout of the tag
(`installer/keys/release-signing.pub`), not from the release page.

```
openssl dgst -sha384 -verify installer/keys/release-signing.pub \
  -signature manifest.json.sig manifest.json
```

`Verified OK` means the manifest is the one the release key signed. That is the exact check the
installer and `maran update` run (`installer/cli/lib/release-verify.sh`). Then compare each file you
downloaded with the manifest, which is now trusted:

```
sha256sum agent-x86_64.tar.gz
grep -A2 '"agent-x86_64"' manifest.json
```

The two sha256 values must be identical. `SHA256SUMS` is not signed and proves nothing about who
made the files: whoever can replace an artifact can replace it too, and only the signature binds the
manifest to the key. The same two steps apply to `integrity-manifest.json` and its `.sig`.

## SBOMs

Each SBOM is a CycloneDX JSON document listing the third-party packages one component is built from:
the Rust crates of the agent (read from `Cargo.lock`), the npm packages of the SPA (from
`package-lock.json`) and the NuGet packages of the API (from the restored project assets). Read them
with any CycloneDX tool, or with `jq '.components[] | {name, version}'`. The generators are pinned to
exact versions in `scripts/lib/release-sbom.sh` and are not reproducible: every document carries a
timestamp and a random serial. The npm and NuGet generators read lockfiles and restored project
assets, but the transitive dependencies those lockfiles name are not pinned by this project beyond
what the lockfiles record. `cargo cyclonedx` runs `cargo metadata` without `--offline`, so it may
fetch crates from the registry; the script refuses a result if `Cargo.lock` changed meanwhile.

What an SBOM does not prove: it is evidence of what the lockfiles declared, not a signature over the
binaries, and it is not covered by the manifest signature. A compromised generator could emit a wrong
one, which is why it says nothing you cannot re-derive from the lockfiles at the tag. It lists
dependencies, not vulnerabilities; it does not cover the operating system packages the panel's
installer pulls from a distribution.

## The OpenAPI document

Every release carries `openapi.json`, the description of the panel's HTTP API (what it is, and the
gate that guards it: `docs/api/README.md`). The `openapi` job of `release.yml` generates it from the
tagged tree with `maran release openapi --out <dir>`, which uses the same generator as the
`maran openapi` check, writes it in the same canonical form, prints its sha256, and refuses to write
anything when the document was not generated or describes no operation. The job holds no secret and
no write permission; `publish` downloads its artifact and attaches it.

**It is not signed.** That is an owner decision: the manifest signature and the integrity manifest
do not cover it, and the only integrity statement is its line in `SHA256SUMS`, which is itself
unsigned and detects a corrupted download, not a replaced one. A reader who needs the description to
be trustworthy takes the baseline from the signed tag's own tree (`docs/api/openapi-baseline.json`)
or regenerates it from that tree. The signing job's inputs are unchanged by this: it never sees the
artifact.

## Release notes

`maran release build` writes two signed manifest fields next to `channel`: `released`, the UTC date
(from `SOURCE_DATE_EPOCH` when set, so a reproducible build repeats it), and `notes_url`, by default
`https://github.com/innovayse/maran/releases/tag/v<version>`, the GitHub release whose body is the
notes below. `--notes-url <https url>` replaces it; the build refuses a URL `maran update --check`
would not print (see panel-update.md, "Release date and notes").

`maran release changelog` builds the notes offline from the merged pull requests between the previous
`v*` tag of ANY channel and this one (so the notes of a stable release cover the betas before it too):
the same inputs give the same file, with no network call at run time and no
timestamps. It reads GitHub merge commits (`Merge pull request #N`), so a pull request that was
squash-merged would be missing from the notes. Pull request titles are treated as data: control characters and newlines are removed and
the output is a Markdown file that is never evaluated. The notes list changes; they are not a
security statement, and the `CHANGELOG.md` section for the version remains the place that says what is
not verified.

## Reproducing the agent from a tag

Anyone can rebuild the agent from the tag and compare it with the signed release:

```
maran release rebuild --tag v1.0.0 --bundle ./downloaded-release
```

Prerequisites: `git`, `openssl`, and `docker`; for the `aarch64` agent on an `x86_64` host, register
emulation first with `docker run --privileged --rm tonistiigi/binfmt --install arm64` (it does not
survive a reboot). The command first verifies `manifest.json.sig` against the pinned public key,
then exports the tag with `git archive` into a clean directory (the working tree cannot influence
it), builds the agent with `--locked` in the builder image (`docker/release/agent-builder-alma8.Dockerfile`)
with `SOURCE_DATE_EPOCH` taken from the tag's commit, packs it with the deterministic archiver, and
compares the sha256 of the result with the entry in the signed manifest. It also prints the versions of
the host tools that matter and compares the extracted binary, as secondary information.

The builder pins only the base image's digest. The compiler packages installed on top (`gcc`,
binutils, via `dnf`) float, and the host's `tar` and `gzip` versions matter to the archive bytes, so
a rebuild months later can mismatch a release that was honest.

- **Match**: the signed agent tarball is what this source tag produced under that toolchain.
- **Mismatch**: the release was not built from that tag, or the toolchain differs (compare the printed
  tool versions and the extracted binary first). It is never proof of tampering on its own, and a
  match is never proof of safety: it says nothing about the source being good. Report a mismatch to
  the contact in `SECURITY.md` with both hashes and the output.

Not claimed reproducible: the API publish and the SPA bundle. The `aarch64` agent is reproducible
only as far as it has been measured, and it has not been measured on a runner yet (see below).
`maran release reproducible-selftest`, `rebuild-selftest`, `sbom-selftest` and `changelog-selftest`
prove the checks themselves, each with an inverse control; they do not prove a real release.

## The release host mirror

The workflow publishes a GitHub release and nothing else. Copying the artifacts to
`releases.maran.innovayse.com`, where installed servers fetch from, is not automated and is the
owner's decision (recorded on issue #154). Until that is done, `maran update` finds nothing new on
the release host and an operator uses `--offline-tarball` with the verified offline bundle.

A developer who wants a work-in-progress build on a test host does not use a release at all:
`maran deploy-local` signs with a TEST key and makes that host trust it, so it is for development
hosts only (`docs/operations/deploy-local.md`).

## What is not verified

- No tagged run of the workflow has happened; it is linted, and its logic is exercised through the
  scripts' selftests. The `release` environment's reviewer and tag settings are not checked by any
  file in the repository.
- The SBOM generators have not been installed from their registries in CI.
- The `openapi` job and `maran release openapi` against the real panel: the selftest plants the
  generator's output, and no tagged run has happened.
- Reproducibility of the `aarch64` agent.
- The second security review of this pipeline is OUTSTANDING: see
  `docs/superpowers/notes/2026-11-04-release-pipeline-threat-note.md` for what a reviewer must check.
  A maintainer who may push tags and approve the `release` environment can still release anything:
  that is the owner's trust boundary.
