# Release pipeline threat note (issue #154)

Status of the second review: **OUTSTANDING.** Written before the change, as rules/security.md
"Sensitive change escalation" requires. The change may land on `dev`; it MUST NOT merge to `main`
until a reviewer other than the author has read this note against the diff.

## What changes

A tagged release workflow (`.github/workflows/release.yml`) builds the three components for both
architectures, signs the release manifest with the release key, generates one CycloneDX SBOM per
component, generates release notes from the merged pull requests since the previous tag, and
attaches all of it to a GitHub release. `maran release rebuild` rebuilds the agent from a tag in a
clean tree and compares its hash with the signed manifest. The release build itself
(`scripts/lib/release-bundle.sh`, ECDSA P-384 over SHA-384, key `installer/keys/release-signing.pub`)
already exists from issue #128; this change automates its invocation and adds evidence around it. It
adds no daemon, no listening port and no outbound call from any installed panel (rules/security.md
item 10): every new network call happens in CI or on a maintainer's machine.

## What an attacker could do with this surface

1. **Get a release signed that is not ours.** The signing key is the root of trust for every
   `maran update` in the field. Anyone who can make the workflow sign attacker-chosen bytes owns every
   server on the channel.
   - The key is a secret of a protected GitHub environment named `release`, configured by the owner
     with required reviewers. The workflow triggers on `push` of a tag matching `v*` only, never on
     `pull_request` or `pull_request_target`, so a fork cannot reach the secret.
   - The `check` job refuses a tag that is not a release version or whose commit is not reachable
     from `origin/main`. It checks the tag and ancestry only, not CI status. **These two checks are
     not controls against a tag pusher:** the workflow file that runs is the one in the tagged
     commit, so whoever pushes the tag can edit them away. They catch mistakes, not attackers.
   - The real gates are settings the owner configures and no file here enforces: the `release`
     environment restricted to `v*` tags with a required reviewer other than the person who pushed
     the tag, and a tag ruleset limiting who may create `v*` tags.
   - **Not closed:** a maintainer with permission to push tags and to approve the environment can
     still release anything. That is the owner's trust boundary, not a defect of this change.
2. **Leak the key.** The key is written to a file under `$RUNNER_TEMP` with mode 0600, passed to
   `maran release sign --key` by path, never by value, never echoed, and removed in a step that runs
   `if: always()`. Shell tracing is off in the step. The key is never put in an environment variable of
   a step that runs other tools, never in an artifact and never in the SBOM. A reviewer must check
   that no step prints `env`, enables `set -x`, or uploads `$RUNNER_TEMP`.
3. **Inject through a tag or PR title.** A tag name and the title of a merged pull request are
   attacker-influenced text. Neither is interpolated into a shell command line: the workflow passes
   them through `env:` and the scripts read them as data. The release notes are written to a file and
   passed with `--notes-file`; PR titles are stripped of control characters and newlines before they
   are written, and the generated Markdown is never evaluated.
4. **Make the SBOM lie, or tamper with the build tools.** The three SBOM generators are third-party
   programs that run in the `sbom` job, a job separate from the build and from the signing job (the
   signing key exists only in the `sign` job, which also runs no generator). Each is pinned to an exact version
   (one table in `scripts/lib/release-sbom.sh`), installed with `--locked` or an exact version, and
   reads the lockfiles (`Cargo.lock`, `package-lock.json`, and the backend's restored project assets), not the
   network, except that `cargo cyclonedx` runs `cargo metadata` without `--offline`, so it may fetch
   crates from the registry (the script fails if `Cargo.lock` changed meanwhile). The npm and NuGet
   transitive dependencies are not pinned beyond what their lockfiles record. A compromised generator
   cannot read the key: it never runs in a job that has it. **Not closed:** a compromised generator can still emit a wrong SBOM; the SBOM is evidence
   about dependencies, not a signature over them, and says nothing a reviewer cannot re-derive from
   the lockfiles.
5. **Make a rebuild appear to match.** `maran release rebuild` compares the rebuilt agent with the
   entry in `manifest.json`, and only after it has verified `manifest.json.sig` against the pinned
   public key. A hash file shipped beside the artifacts is not trusted, because whoever can replace
   an artifact can replace its hash file. The rebuild runs in a clean `git archive` export of the tag,
   not in the working tree, so uncommitted files cannot influence it.
6. **Reproducibility is claimed only where it is measured.** The agent tarball is deterministic
   (sorted names, fixed mtime from the tag's commit time, numeric root ownership, `gzip -n`) and the
   agent is built with `--locked`, a path remap and `SOURCE_DATE_EPOCH` inside the builder image, which pins
   only its base image digest: the `dnf` compiler packages float, and the host `tar` and `gzip`
   versions matter, so a later rebuild can mismatch an honest release. The rebuild prints the tool
   versions and compares the extracted binary as secondary information. The API publish and the SPA bundle are NOT claimed reproducible; the manifest and
   the documentation say so.

## Why it is safe now

- Nothing in the installed product changes: the verifier, the key and the manifest format are those of
  #128. The manifest gains no field; the SBOMs and notes are separate files that no installed
  component reads.
- Unsigned output cannot be published: the publish step depends on the verify step, which runs
  `maran release verify --publish` (both architectures, signature, checksums) over the staged bundle.
- The workflow has `permissions: contents: read` at the top and `contents: write` only on the
  `publish` job, which has no environment and never sees the key; the `sign` job, which does, has no
  write permission to the repository.
- Every refusing check has an inverse control in its selftest (rules/testing.md).

## What a reviewer must check

- `release.yml`: trigger, environment, permissions, the key's life (write, use, delete), no
  interpolation of `${{ github.* }}` into a `run:` line, and that third-party actions are pinned the
  way the other workflows pin them.
- `scripts/lib/release-rebuild.sh`: that the signature is verified before any hash is read, and that
  the comparison is against the signed manifest.
- `scripts/lib/release-changelog.sh`: that text from a pull request title can reach only a Markdown
  file.
- That the SBOM step precedes the key write.

## What the author could not verify

- The workflow itself: it needs a pushed tag and the `release` environment's secret, neither of which
  exists in this session. It is linted for syntax and its logic is exercised by the scripts' selftests,
  not by a run.
- Installing the three SBOM generators from their registries in CI.
- Reproducibility of the aarch64 agent, which builds under emulation here and natively on a runner.
- Where the published artifacts are mirrored for the release host
  (`releases.maran.innovayse.com`): an owner decision, recorded on issue #154. This change publishes a
  GitHub release only.

## Amendment: the publish gates re-check the pinned keys (#544, #552)

- `maran release sign --publish` and `verify --publish` (the commands the `sign` and `publish` jobs run) now
  require the manifest's `pinned_keys` object to equal the hashes of the checkout's `installer/keys/` files, and
  every `api-<arch>` archive's `pinned-keys.txt` to agree. The gate no longer lives only in the workflow's
  `build --publish` flag: a bundle built without it cannot be signed or verified for publication.
- A pinned key that is empty, or neither a PEM public key nor 64 hexadecimal characters, is refused by the
  Licensing build target and by the manifest writer. A hash that cannot be computed aborts the build instead of
  entering the signed manifest empty.
- Every input of `sign` (integrity manifest, `openapi.json`) is checked before the first signature is written.
- Not verified here: the first tagged run; the key ceremony must land before it (the build stops with `MARAN001`
  until all five keys are in `installer/keys/`). A red installer leg blocks signing by design.

Second review: OUTSTANDING (#331).
