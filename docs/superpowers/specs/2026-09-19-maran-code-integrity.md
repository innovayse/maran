# Maran — code-integrity addendum

- **Дата:** 2026-09-19
- **Статус:** owner-decided scope and response; this document records the decision and its
  boundaries. Not yet reviewed against the base spec's numbering — filed as a standalone addendum
  rather than an edit to `docs/superpowers/specs/2026-08-29-maran-design.md`, so the base spec's own
  review history stays intact.
- Extends: `docs/superpowers/specs/2026-08-29-maran-design.md` §9 (agent contract), §10 (panel
  security/audit), §13 (licensing, `PluginLoader`), §14 (installer/updates), §15 (observability, "no
  hidden telemetry"), §18 (cloud side), §19 (risks).

## 1. What this is, and why it is a gap being filled, not a gap being decided

A search of the base spec for целостн / подмен / модифиц / tamper / integrity returns nothing: no
version of the design has ever mentioned runtime verification that the panel's own installed files
match what was released. This addendum fills that gap for v1, at the owner's direction, in the
aaPanel model: detect that files on a customer's server differ from the exact release version that
shipped them.

Three decisions below are the owner's and are recorded here, not re-argued:

1. **Scope is ALL panel files** — every file the release ships under `api/`, `agent/`, `frontend/`,
   not only the licence-verification path.
2. **Response is report-only.** To the operator (panel alert) and to the cloud (phone-home report).
   No degradation, no blocking, no disabling of paid modules — the base spec's own standing rule for
   licence failures (§13: "деградируют только платные модули; ядро не умирает никогда") is not even
   applied here; the owner has chosen a strictly weaker response than the one licence-lease loss
   already gets.
3. **The comparison runs inside the closed `PluginLoader`** (§13, closed Rust, private repository —
   not this monorepo). This document specifies what THIS repository must expose so that comparison
   is possible and its findings reach an operator and the cloud; it specifies nothing about the
   comparison algorithm itself.

## 2. Why the response is what it is, and what it implies about the hash list

BSL explicitly permits a customer to modify the code they were licensed. A report that reads
"tampering detected" to a customer exercising that right is a false accusation the license itself
makes false. So every operator-facing and cloud-facing artifact this mechanism produces is a
statement of fact — *these N files differ from what release X shipped* — and never a verdict.
That constraint is why the hash list must be tied to an EXACT release version, stated plainly: with
every file in scope, every ordinary update or security patch changes some hashes legitimately. A
hash list not pinned to the version actually installed produces noise on every update — the
mechanism would fire on its own patches — and noise a operator cannot act on teaches them to ignore
the report, which is worse than not having it. §14's blue/green update model already gives this a
natural anchor: the version an operator is running is always known (the active symlink target), so
the hash list this mechanism compares against is always "the list for the version currently live,"
never a floating or latest-only list.

## 3. The hash list is a release artifact, distinct from the installer's transport manifest

§14 already commits the installer to a signed manifest (`installer/lib/50-artifacts.sh`) that
authenticates three downloaded component archives (`api`, `agent`, `frontend`) before extraction —
one sha256 per archive, checked twice, gated by an Ed25519 signature over a baked-in public key
(`installer/lib/50-artifacts.sh:28-97`). That manifest is a TRANSPORT-integrity check, read once, by
root, before anything is unpacked.

The code-integrity hash list is a different artifact, produced at the same release-build step
(`scripts/lib/release-bundle.sh`) with the same signing key and the same version, but:

- **it names one entry per installed FILE**, not per component archive — the granularity the
  transport manifest's own reader was never built for (`manifest_field()` in
  `installer/lib/50-artifacts.sh:91-104` is a bare awk scanner over a three-entry structure, by
  design, because jq "is not guaranteed present at this point in the install");
- **it is read continuously, after install, by a different process** (the closed `PluginLoader`,
  running inside the panel process once composed — §13), not once by the installer script;
- **it therefore ships as a second file inside the same signed bundle** —
  `integrity-manifest.json` + `integrity-manifest.json.sig` — rather than a new field grafted onto
  `manifest.json`. Deciding otherwise would mean the installer's transport check and the panel's
  standing runtime check depend on one file serving two audiences with two different shapes, which
  is exactly the "two sources of truth about what a release contains" risk this addendum's
  assignment named directly. One build, one key, one version, two files, because they answer two
  different questions to two different readers.

## 4. What is, and is not, hashed — and why

`integrity-manifest.json` is built by walking the SAME three staged output directories the release
build already tars (`scripts/lib/release-bundle.sh`'s `build_api`/`build_agent`/`build_frontend`
output, the exact contents of `api.tar.gz`/`agent.tar.gz`/`frontend.tar.gz`) — never by scanning a
live installed filesystem. This is a positive list, not a negative one: it enumerates what a
release CONTAINS, and nothing outside that enumeration is ever compared. Two consequences follow,
stated as limits rather than hidden:

- **Files the installer or the running panel write AFTER extraction are never in scope, and cannot
  be, by construction** — `/etc/maran/panel.env` (rules/security.md item 7-8), DataProtection keys,
  TLS certificates (self-signed at install, later LE-issued per §14), the per-site nginx vhost
  fragments the agent renders (§9: "Агент пишет только свои файлы"), and PostgreSQL state. These are
  legitimately host-specific and different on every install; including them would false-positive on
  every host that has ever configured anything. They are excluded by never being enumerated in the
  first place, not by a named exclude-list a customer could quietly grow to hide something inside.
- **A file a customer or an attacker ADDS anywhere on the host is invisible to this mechanism.** It
  answers "does the shipped code match", never "is anything extra present." That is a real,
  deliberate limitation of the v1 design, not an oversight, and it is recorded here so nobody later
  treats a clean report as proof nothing extra was planted.

## 5. What this mechanism does NOT achieve — read this before relying on it

A hash list plus a verifier that both live on a server the customer, or an attacker who has taken
over that server, fully controls is **not a barrier** to that attacker. They can patch the verifier
binary, patch the hash list the verifier reads, or intercept whatever either produces before it
leaves the host. Running the comparison inside the closed `PluginLoader` (§13) does not change this
fact — it only means the comparison LOGIC is not readable in this public repository, which raises
the bar against an opportunistic or unsophisticated actor and against ordinary supply-chain
failures (a corrupted download, a partial extraction interrupted mid-update), but it is **a
deterrent, not a proof**, against a determined attacker who already has root on that box. This
mechanism gives an operator and the cloud a factual signal worth investigating; it does not give
Innovayse or a customer a guarantee that an unreported host is running unmodified code. Any product
copy, sales material, or support answer that states otherwise overstates what this document
specifies, and this document is the correction on record.

## 6. Reporting path

**To the operator:** a panel alert, following the existing Monitoring `AlertKind`/`AlertState`
machine exactly (spec §11, implemented in `backend/src/Maran.Modules/Monitoring/`) — raise once per
episode, resolve once when it clears, debounced the same way disk-usage and service-down alerts are,
never a mail per observation. The wording shown to an operator states the fact (which files, which
release) and explicitly disclaims that a difference is not by itself evidence of compromise — a
customer holding a BSL right to modify their own installation must never read an accusation.

**To the cloud (§18, §229):** the finding rides on the EXISTING documented phone-home exchange
(the daily licence-lease renewal), never a new outbound call — §15's standing rule ("без скрытой
телеметрии: единственный исходящий вызов — документированный phone-home") and rules/security.md
item 10 ("no new outbound calls... anything of the kind requires a spec change first") both forbid
opening a second one. This repository's obligation ends at making the finding available to whatever
closed component already performs that exchange; the exchange's payload shape and the cloud
service's handling of it are §18's territory, outside this monorepo, and this addendum does not
specify them.

## 7. What this addendum explicitly does not decide

- The comparison algorithm, its performance characteristics, and its own defenses against a
  compromised host — `PluginLoader`'s internals, closed, §13.
- The cloud service's storage, alerting, or customer-facing presentation of a received finding —
  §18, a separate service outside this monorepo.
- The exact operator-facing and cloud-facing wording in en/ru/hy. Section 6 states the MEANING the
  wording must carry (fact, never accusation, names the release and the files, states plainly that
  BSL permits modification); the owner reviews and approves the actual sentences.
