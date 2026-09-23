# Threat note — the code-integrity hash list (`integrity-manifest.json`)

Required by `rules/security.md` ("Sensitive change escalation"). Covers the extension of
`scripts/lib/release-bundle.sh` (Task 1 of
`docs/superpowers/plans/2026-09-19-maran-code-integrity.md`) that adds a second signed artefact —
`integrity-manifest.json` / `integrity-manifest.json.sig` — to the same release bundle
`docs/superpowers/notes/2026-09-19-release-bundle-signing-threat-note.md` already covers, plus the
new Sdk seam (`ICodeIntegrityReportSink`) and Monitoring alert extension that consume a finding
produced from it by the closed `PluginLoader`. **This change needs a second reviewer. That review is
OUTSTANDING** — written by the same session that wrote the change, which `rules/security.md` names
explicitly as not a substitute for a second reviewer. This note is written before the code, per the
rule.

## What this surface is, and what it is not

`integrity-manifest.json` is a build-time artefact: one sha256 per file the release ships under
`api/`, `agent/`, `frontend/` (walked from the SAME staged output directories
`build_api`/`build_agent`/`build_frontend` already tar, never a live installed filesystem — see
`docs/superpowers/specs/2026-09-19-maran-code-integrity.md` §3-4 for why). It is signed with the
SAME Ed25519 key and the same invocation `cmd_sign` already uses for `manifest.json`
(`scripts/lib/release-bundle.sh`).

**It is not a second transport-integrity check.** `installer/lib/50-artifacts.sh` never reads it —
per Task 5, that file is out of scope for this change entirely; the installer keeps verifying only
`manifest.json` before extraction, exactly as it does today. `integrity-manifest.json` is read
continuously, after install, by the closed `PluginLoader` (§13 of the base spec, not in this
monorepo) — a different reader, on a different schedule, answering a different question ("does what
is on disk still match what the release signed", not "is this download intact before I unpack it").

**This repository's own new code (Task 2/3) never compares a hash.** `CodeIntegrityOutcome`,
`CodeIntegrityReport`, `ICodeIntegrityReportSink`, and the Monitoring `AlertEvaluator` extension only
carry and journal a finding the closed `PluginLoader` already produced. The comparison algorithm
itself — reading `integrity-manifest.json`, verifying its signature, walking the installed
filesystem, and deciding `Clean`/`Drifted`/`Unavailable` — is closed, private, and outside this
repository's threat surface; this note cannot and does not assess it.

## Attacker model and what the signature protects

- **An attacker who can replace `integrity-manifest.json` on a mirror or in an offline bundle, but
  not the signing key**: fully defeated by the same Ed25519 signature check the release-bundle
  threat note already documents for `manifest.json` — a corrupted or substituted hash list fails
  `openssl pkeyutl -verify` and the closed side must report `Unavailable`, never trust it.
- **An attacker with root on the customer's own host** (the attacker model
  `docs/superpowers/specs/2026-09-19-maran-code-integrity.md` §5 names explicitly): **not
  defeated, and not claimed to be.** They can patch the verifier (the closed `PluginLoader` binary
  itself, on their own machine), patch the hash list it reads, or intercept whatever either
  produces before it leaves the host. This mechanism is a deterrent against an opportunistic actor
  and against ordinary supply-chain failures (corrupted download, interrupted extraction), never a
  proof against a host the attacker already fully controls. Any reviewer relying on this note to
  argue otherwise is reading it against its own explicit text.
- **The build host producing `integrity-manifest.json`**: unchanged from the existing
  `manifest.json` build — this tooling assumes the machine running `maran release build`/`sign` is
  not already compromised, out of scope here as it was there.
- **A build that produces an empty or truncated hash list**: this is the specific failure this
  change adds a NEW check against — `write_integrity_manifest`'s vacuity floor
  (`INTEGRITY_MANIFEST_MIN_FILES`), which refuses the build (`release_failed`) below a fixed file
  count. Without it, an empty list would sign successfully, verify successfully, and report `Clean`
  forever on every installed host — the single most dangerous failure mode this whole feature has,
  because it looks identical to health.
- **A signed-but-internally-wrong hash list** — one entry's sha256 corrupted before signing, the
  rest correct: **not defeated by the signature, and this note says so rather than implying
  otherwise.** Signature verification proves the FILE was not altered after signing; it does not
  re-derive hashes from source. A build defect that writes a wrong hash for one file, then signs the
  (defective but internally consistent) result, produces a bundle every check in this repository
  accepts. Task 1's inverse control in `cmd_selftest` demonstrates this gap directly rather than
  hiding it (see the plan, Task 1, "Inverse control (b)").

## What this repository's new Sdk/Monitoring code does and does not protect against

- **`ICodeIntegrityReportSink.ReportAsync`'s implementation must never throw back into the closed
  caller**, and never turn a caller-side failure into a panel-side one — the interface's own doc
  comment states this explicitly. `CodeIntegrityReportHandler` catches every non-cancellation
  exception and logs rather than propagating, so a defect on the receiving side cannot become a
  crash or hang inside the closed `PluginLoader`'s `AssemblyLoadContext`.
- **A malformed report (`Drifted` with zero `DifferingPaths`) is rejected, not laundered into
  `Clean`.** This is the "vacuity trap at this layer" the plan names for Task 3: an inconsistent
  report from the closed side is journalled as `CodeIntegrityReportRejected` and neither raises nor
  resolves either alert subject, so a bug on the closed side cannot silently produce a false-healthy
  reading on the open side either.
- **What this code cannot verify: that the closed `PluginLoader` itself computed the finding
  correctly.** This repository has no visibility into that comparison; it can only refuse to trust
  an internally-inconsistent report, never confirm a consistent one is actually true.

## What a reviewer must check

- That `write_integrity_manifest`'s floor constant is genuinely enforced with `release_failed`
  (non-zero exit), not merely logged — a floor that only warns is not a floor.
- That `cmd_sign`'s new `integrity-manifest.json.sig` call uses the identical `openssl pkeyutl -sign
  -rawin` invocation already used for `manifest.json`, not a different digest mode.
- That `CodeIntegrityOutcome.Unavailable` cannot be produced, stored, or displayed identically to
  `Clean` anywhere in the Monitoring alert path added here (Task 3's `Unavailable` branch feeds
  `HashListSubject`, never silently defaulting).
- That no operator- or cloud-facing string this change adds (the four resx keys) states or implies
  that a `Drifted` finding is proof of tampering, cracking, or license violation — BSL permits a
  customer to modify their own installation, and `docs/superpowers/specs/2026-09-19-maran-code-
  integrity.md` §2/§5 require every such string to read as fact, never accusation.

## What could not be verified in this environment

- **No real `PluginLoader` exists to call `ICodeIntegrityReportSink` for real.** Every proof in this
  change's implementation log exercises the sink through a direct unit-level call, never through an
  actual `AssemblyLoadContext`-loaded closed component, because none is present in this monorepo
  (confirmed by the plan's own Task 0 grep). The calling convention this note and the interface's own
  doc comment describe is therefore a requirement stated for a caller that does not yet exist here,
  not a behavior observed end-to-end.
- **`write_integrity_manifest`'s behavior against a real release build** was not observed with real
  `api`/`agent`/`frontend` staged output in this session — see whichever session's log records
  whether `cargo build --release -p maran-agent` compiles at the time Task 1 is actually executed;
  the existing `cmd_selftest`'s own header notes this failed to compile as of `manifest.json`'s own
  original work, for reasons outside this change's scope.

---

## Second review: RECORDED 2026-09-23

Every sentence above that calls the second reviewer OUTSTANDING described the state until this
date. It is kept rather than edited away, because what a note claimed while the debt stood is part
of what a reader is judging.

**Reviewer:** Edgar Poghosyan (edgar2031), the repository owner — the second human the rule asks
for, and legitimately so: he wrote none of these notes. Every one was written by an agent session,
which is the conflict the requirement exists to break.

**Verdict:** accepted, with no condition attached to this note.

**Typed by the agent at the reviewer's instruction**, because the reviewer does not write English.
Recorded here so a later reader can tell whose judgement this is and whose keyboard it came
through — those are not the same person.

The verdict for all twenty-eight notes is tabulated in
`docs/superpowers/notes/2026-09-22-second-review-packet.md`.
