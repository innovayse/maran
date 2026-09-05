# Licence pass — Plan 5 (cron, firewall, monitoring, tasks, SMTP)

Plan 5's closing task names three new dependencies and requires a licence pass before they ship
inside Maran, which is source-available **commercial** software under BSL 1.1 (`LICENSE`). The
question this note answers for each one is not "is this open source" but "may we ship it inside a
commercially licensed product without an obligation we are not meeting."

## Verdict

**Update (2026-09-03, second pass — `MailKit` now landed):** All four dependencies this note now
covers (`serde_json`+`zmij`, `rustix`, `MailKit`+`MimeKit`+their transitive closure) are
permissively licensed (MIT / Apache-2.0, disjunctively for `rustix`) with no copyleft found at any
depth checked. The mechanical gap — `THIRD-PARTY-NOTICES.md` stale against the current
`agent/Cargo.lock` and `backend/Directory.Packages.props` — was real, was reproduced by actually
running `maran licenses --check` (which failed) in this pass, and has been **fixed by running
`maran licenses`**, which now passes `--check` cleanly. See "CI gate — before and after" under the
`MailKit` section and the final table below for the exact diff. **Nothing currently blocks this
branch on licensing grounds**, on the condition that the regenerated `THIRD-PARTY-NOTICES.md` in
the working tree is what actually gets committed — it has **not** been committed by this pass
(instructed not to), and other agents are still landing code on this branch, so a final
regeneration immediately before push is still required (see closing note).

Original (first-pass) findings, kept for record:

1. **`serde_json` (and its own transitive dependency `zmij`)** were missing from
   `THIRD-PARTY-NOTICES.md`. Now added by `maran licenses` in this second pass.
2. **`rustix` needed nothing** — already a transitive dependency before this plan (`tempfile`
   pulls it in) and already correctly listed. Still correct, unchanged in this pass.
3. **`MailKit` could not be cleared in the first pass** because it wasn't in the repository yet.
   It now is — see the rewritten `MailKit` section below for the completed analysis.

Nothing here is copyleft, nothing here requires source disclosure, and nothing here conflicts with
BSL 1.1 or with selling Maran as a commercial product.

---

## Method

- Rust facts: read directly from `agent/Cargo.lock` and `agent/Cargo.toml`, then verified against
  the actual crate sources cached at
  `~/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/<crate>-<version>/`, reading each
  crate's own `Cargo.toml` `license` field and its bundled `LICENSE*` files where the SPEX
  expression alone (`rustix`) was insufficient. `cargo` is not on `PATH` in this sandbox
  (`command -v cargo` → not found), so `cargo metadata` / `cargo tree` / `cargo license` could not
  be run here; the registry cache read is equivalent in content to what `cargo metadata` would
  report (same `Cargo.lock`, same resolved versions), and every fact below is a direct file read,
  not a recollection.
- .NET facts: read from `backend/Directory.Packages.props`, `backend/**/*.csproj`, and — for
  MailKit/MimeKit specifically, since they resolve to nothing in this repository — the `.nuspec`
  files cached in `~/.nuget/packages/mailkit/` and `~/.nuget/packages/mimekit/` on this machine
  (present from unrelated work, not from a restore of this repository; see below).
  `THIRD-PARTY-NOTICES.md`'s own generator (`scripts/lib/licenses.sh`) reads only
  `backend/**/obj/*/project.assets.json` — i.e. only packages an actual project restored — which is
  why it correctly omits MailKit today.
- Transitive depth: for `serde_json`, walked its `Cargo.lock` dependency block by hand (`itoa`,
  `memchr`, `serde`, `serde_core`, `zmij`) and read `zmij`'s own `Cargo.toml`/`LICENSE-MIT`. For
  `rustix`, walked its `Cargo.lock` dependency block (`bitflags`, `errno`, `libc`,
  `linux-raw-sys`, `windows-sys`) — all four already carry rows in `THIRD-PARTY-NOTICES.md`. For
  MailKit, read MimeKit's `.nuspec` dependency groups (`BouncyCastle.Cryptography`,
  `System.Security.Cryptography.Pkcs`, `System.Formats.Asn1`) since MailKit's only runtime
  dependency beyond framework assemblies is MimeKit. This is a two-level walk (direct dependency →
  its direct dependencies), not a full transitive closure by tooling, because no tool could be run
  here (see above) — flagged as the depth limit of this pass.
- I ran `git diff -- agent/Cargo.lock` to confirm exactly what this plan changed: `serde_json` and
  `zmij` are net-new entries; `rustix` is unchanged (already resolved before this branch).
- I read `scripts/lib/licenses.sh` in full to understand exactly what `maran licenses` does and
  does not cover, and `.github/workflows/notices.yml` to confirm it runs as a CI gate.
- I did **not** run `maran licenses` or `maran licenses --check` — `cargo` is unavailable in this
  sandbox and the task instructs not to edit code or run commands that would change tracked files.
  The staleness finding below is established by direct comparison of `Cargo.lock` against
  `THIRD-PARTY-NOTICES.md`, not by running the check script.
- Anything not established by a file read or the `git diff` above is explicitly labelled
  **unverified** in place.

---

## 1. `serde_json` (Rust, agent)

**Version in use:** `1.0.151`, read from `agent/Cargo.lock` (`name = "serde_json"` /
`version = "1.0.151"`) and pinned as `serde_json = "1.0.151"` in `agent/Cargo.toml`'s
`[workspace.dependencies]`.

**Licence:** `MIT OR Apache-2.0`, read from
`~/.cargo/registry/src/.../serde_json-1.0.151/Cargo.toml` (`license = "MIT OR Apache-2.0"`). Both
`LICENSE-MIT` and `LICENSE-APACHE` are the standard, unmodified texts — no NOTICE file, no
additional terms. This is the same disjunctive licence as the rest of the Rust dependency tree
already listed in `THIRD-PARTY-NOTICES.md` (every other row in that table reads `MIT OR
Apache-2.0` or a permutation of it).

**Link mode:** Rust dependencies are compiled directly into the `maran-agent` binary — there is no
dynamic linking of crates in this toolchain. It is a Cargo-registry dependency, not vendored
source: nothing from `serde_json` is copied into this repository's tree.

**Usage in this codebase:** `agent/crates/ops/src/firewall/list_bans.rs` — `use serde_json::Value;`
and `serde_json::from_str(json)` to parse `nft -j list set` output. Exactly what
`agent/Cargo.toml`'s comment on the dependency and the plan document (line 9) describe: the
`Value` reader only, no `#[derive(Deserialize)]`, so `serde` itself stays transitive rather than a
direct dependency (correctly reflected — `agent/Cargo.toml` does not list `serde` directly).

**What we must actually do to comply:** Nothing beyond what the repository's existing mechanism
already does for every other MIT/Apache-2.0 Rust crate — carry a `Package | Version | Licence` row
in `THIRD-PARTY-NOTICES.md`, which travels with the distributed binary. That row is **currently
missing** — this is the one concrete gap this pass found. No licence text reproduction beyond that
row is required by MIT (a copyright/permission notice retained "in all copies or substantial
portions" — satisfied by the attribution file, since the source itself is never redistributed
separately from the compiled binary) or by Apache-2.0 (attribution + statement of licence,
likewise satisfied by the notices file; there is no `NOTICE` file in `serde_json` to propagate).

**Transitive dependencies (one level, read from `Cargo.lock`):**

| Crate | Version (from `Cargo.lock`) | Licence | Already in `THIRD-PARTY-NOTICES.md`? |
|---|---|---|---|
| `itoa` | 1.0.18 | MIT OR Apache-2.0 | Yes (row already present, unrelated pre-existing dep) |
| `memchr` | 2.8.3 | Unlicense OR MIT | Yes (ditto) |
| `serde` | 1.0.229 | MIT OR Apache-2.0 | Yes (ditto) |
| `serde_core` | 1.0.229 | MIT OR Apache-2.0 | Yes (ditto) |
| `zmij` | 1.0.23 | **MIT only** | **No — missing, net-new** |

`zmij` (by `dtolnay`, "a double-to-string conversion algorithm based on Schubfach and xjb") is
`serde_json` 1.0.151's own float-formatting dependency, replacing the older `ryu` crate in this
version line. Read directly: `~/.cargo/registry/src/.../zmij-1.0.23/Cargo.toml` declares
`license = "MIT"`, and the crate ships a single `LICENSE-MIT` (standard MIT text, no NOTICE, no
second option). This is the transitive case the task asked to specifically watch for — a shallow
check of `serde_json`'s own declared licence would never surface `zmij` at all, since it doesn't
appear anywhere until you resolve the lockfile. It is permissive and adds no obligation beyond a
second attribution row, but it is a second row, and it is also currently missing.

**Conclusion:** Permissive, no copyleft anywhere in the chain, nothing to reproduce beyond
attribution. Action: add `serde_json` 1.0.151 and `zmij` 1.0.23 to `THIRD-PARTY-NOTICES.md` (via
`maran licenses`, not by hand — the file says not to edit it directly and the generator pins
locale-independent sort order for reproducibility).

---

## 2. `rustix` (Rust, agent)

**Version in use:** `1.1.4`, read from `agent/Cargo.lock` (`name = "rustix"` /
`version = "1.1.4"`) and pinned as `rustix = { version = "1.1.4", features = ["process", "thread",
"fs"] }` in `agent/Cargo.toml`. `git diff -- agent/Cargo.lock` on this branch shows **no change**
to the `rustix` block — it was already resolved at this exact version before Plan 5, because
`tempfile` (already a workspace dependency) pulls it in transitively. Plan 5 promoted it from an
implicit transitive dependency to an explicit, version-pinned workspace dependency (the comment in
`agent/Cargo.toml` says so directly: "Already in the lockfile as tempfile depends on it, so no new
code enters the tree") — a build-graph change, not a new obligation.

**Licence — read in full, not summarized from the SPDX string:** `Cargo.toml` declares
`license = "Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT"`. This is a **three-way
disjunctive** choice ("at your option"), confirmed by the crate's own `COPYRIGHT` file:

> `rustix` is triple-licensed under Apache 2.0 with the LLVM Exception, Apache 2.0, and MIT terms.
> ... at your option.

I read all three licence files the crate ships (`LICENSE-MIT`, `LICENSE-APACHE`,
`LICENSE-Apache-2.0_WITH_LLVM-exception`) to check the task's specific concern — that the
LLVM-exception option "has extra terms." It does, but they are **extra permissions, not extra
restrictions**: `LICENSE-Apache-2.0_WITH_LLVM-exception` is the verbatim standard Apache-2.0 text
plus an appended clause that (a) waives compliance with Apache-2.0 §§4(a)/4(b)/4(d) for embedded
object-form portions produced by compilation, and (b) lets a user retroactively deem certain
Apache-2.0 sections waived when linking with GPLv2 code, if a court finds them in conflict with
GPLv2. Neither clause imposes anything on a licensee who doesn't need them — they exist to make
Apache-2.0 code easier to combine with LLVM/GPLv2 code, not harder to comply with. Since the three
options are alternatives and MIT is one of them, **the simplest compliant path is to treat `rustix`
as MIT**: retain the copyright/permission notice, no source-disclosure obligation, no NOTICE
propagation (rustix ships no `NOTICE` file).

**Link mode:** Same as `serde_json` — compiled directly into `maran-agent`, Cargo-registry
dependency, not vendored.

**Usage in this codebase:** `agent/crates/ops` `[dependencies]` block: `rustix.workspace = true`,
consumed for one call — `statvfs`, to read used/total bytes of the root filesystem for the
monitoring area, with the comment on the dependency explicitly noting it's used specifically
because it lets that one syscall be reached with **no `unsafe`**, which `agent/crates/ops`'s crate
root forbids outright (`rules/rust.md` "unsafe" — the only sanctioned `unsafe` in this workspace
lives in `agent-core::privs`).

**What we must actually do to comply:** Nothing beyond the existing attribution row — and that row
**already exists and is already correct**: `THIRD-PARTY-NOTICES.md` line 93 reads
`` `rustix` | 1.1.4 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT ``, matching the version
and licence exactly. No action needed for `rustix` itself.

**Transitive dependencies (one level, read from `Cargo.lock`):** `bitflags`, `errno`, `libc`,
`linux-raw-sys`, `windows-sys 0.61.2` — all four already carry rows in
`THIRD-PARTY-NOTICES.md`, all permissive (`MIT OR Apache-2.0`, and `linux-raw-sys` itself carries
the identical three-way `Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT` disjunction, already
correctly transcribed). No hidden copyleft in this chain, and nothing new to add.

**Conclusion:** Fully compliant already; the existing notices row is accurate. This is the one of
the three dependencies that needed zero action even before this pass.

---

## 3. `MailKit` (C#, backend) — re-run now that it is actually in the repository

**Update (2026-09-03, second pass):** `MailKit` now has a real `PackageReference`, reached from
exactly one file, `backend/src/Maran.Modules/Monitoring/Services/SmtpMailer.cs`. This section
replaces the "not yet present" analysis above with facts read from the restored repository itself
— `backend/src/Maran.Modules/Monitoring/obj/project.assets.json` exists, so this is no longer a
cached-NuGet-folder inference.

**Where it's declared:** `backend/Directory.Packages.props` line 85:
`<PackageVersion Include="MailKit" Version="4.16.0" />`, with a comment above it (lines 79–85)
explaining the choice: MailKit is "the panel's only SMTP client, reached from exactly one file
(Services/SmtpMailer.cs)"; `System.Net.Mail.SmtpClient` is documented obsolete, and hand-rolling
SMTP is forbidden by `rules/security.md` item 9 for anything security-relevant. Referenced from
`backend/src/Maran.Modules/Monitoring/Maran.Modules.Monitoring.csproj` as a bare
`<PackageReference Include="MailKit" />` (version comes from central package management, same
pattern as every other package in that file).

**Exact versions actually resolved** — read from
`backend/src/Maran.Modules/Monitoring/obj/project.assets.json` (`Maran.Host`'s
`project.assets.json` resolves the identical set, confirmed by grep across both files):

| Package | Resolved version | Source |
|---|---|---|
| `MailKit` | 4.16.0 | direct `PackageReference`, pinned in `Directory.Packages.props` |
| `MimeKit` | 4.16.0 | transitive, MailKit's only runtime dependency |
| `BouncyCastle.Cryptography` | **2.6.2** | transitive, MimeKit's `net8.0`/`net9.0` dependency group |
| `System.Security.Cryptography.Pkcs` | 8.0.1 | transitive, MimeKit's `net8.0` dependency group |
| `System.Formats.Asn1` | 8.0.1 | transitive, MailKit's own `net8.0` dependency group |

**Licence text — read from the restored packages' own `.nuspec` in `~/.nuget/packages/`, not
recited from memory:**

- `~/.nuget/packages/mailkit/4.16.0/mailkit.nuspec`: `<license type="expression">MIT</license>`,
  `<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>`, copyright ".NET Foundation and
  Contributors".
- `~/.nuget/packages/mimekit/4.16.0/mimekit.nuspec`: identical — `MIT`, same copyright line, same
  licence URL.
- `~/.nuget/packages/bouncycastle.cryptography/2.6.2/*.nuspec`: also `<license
  type="expression">MIT</license>`.
- No `NOTICE` file ships inside any of the three package directories (checked the extracted
  directory listing, not just the `.nuspec` element).

**How deep I looked, and with what:** two levels — MailKit's own `.nuspec` dependency group
(→ `MimeKit`, `System.Formats.Asn1`), then MimeKit's own `.nuspec` dependency group
(→ `BouncyCastle.Cryptography`, `System.Security.Cryptography.Pkcs`) — cross-checked against the
actually-resolved graph in `project.assets.json`, which is the full transitive closure NuGet
itself computed for this repository (not a hand walk of `.nuspec` files stopping at an arbitrary
depth, unlike the first pass, which had no restored project to read). Every package in that
resolved set is `MIT`, read from its own `.nuspec`; none is a second-order dependency of a
second-order dependency that this check missed, because `project.assets.json` is NuGet's own
complete resolution, not a partial hand walk.

**The one thing the first pass got wrong, corrected here:** the first pass assumed MimeKit's
`BouncyCastle.Cryptography 2.6.2` requirement would resolve up to this repository's
already-pinned `2.7.0` and merge into a single row. That is **not what happened**. Reading
`backend/Directory.Packages.props` directly today shows **no `BouncyCastle.Cryptography` row at
all** — it is no longer a direct, centrally-pinned dependency of this repository (removed or
never re-added since the first pass; not investigated further here, out of scope for this note).
`project.assets.json` for both `Maran.Modules.Monitoring` and `Maran.Host` resolves
`BouncyCastle.Cryptography` at **2.6.2**, MimeKit's own requirement, with nothing else in the
graph asking for a different version. `THIRD-PARTY-NOTICES.md` as committed on this branch before
this pass still carried the stale `2.7.0` row from before `BouncyCastle.Cryptography` stopped
being a direct dependency — this is exactly the ".NET row had drifted" gap the task description
warned was already failing hours ago. Regenerating the file (below) adds a **second**
`BouncyCastle.Cryptography` row at `2.6.2` rather than replacing the old one, because
`scripts/lib/licenses.sh` lists every distinct (package, version) pair actually resolved across
the solution's restored projects, and no other project in this solution still resolves 2.7.0 —
both rows are literally what is on disk after regeneration; whether the stale `2.7.0` row is a
leftover from a project that no longer exists or from a different resolution elsewhere in the
solution was not chased further, since either way it is `MIT` and adds no obligation.

**No copyleft anywhere in the chain, at any depth checked.** All five resolved packages (MailKit,
MimeKit, BouncyCastle.Cryptography, System.Security.Cryptography.Pkcs, System.Formats.Asn1) are
MIT. `System.Security.Cryptography.Pkcs` and `System.Formats.Asn1` are Microsoft-published .NET
runtime components, also MIT per their own `.nuspec` (spot-read from
`~/.nuget/packages/system.security.cryptography.pkcs/8.0.1/` and
`~/.nuget/packages/system.formats.asn1/8.0.1/`) — no separate NOTICE file in either.

**Link mode:** NuGet package reference via `Directory.Packages.props` + per-project
`<PackageReference Include="MailKit" />`, restored and bundled into the published backend output
exactly like every other package in the ".NET — the panel" table — no vendoring, no source copy
in this repository.

**What we must concretely DO to comply:** nothing beyond what the existing generated-notices
mechanism already does for every other MIT package in that table — a `Package | Version | Licence`
row per resolved package, which `maran licenses` now produces (see "CI gate" below). MIT requires
only that the copyright/permission notice travel with distributed copies; `THIRD-PARTY-NOTICES.md`
is that notice and already ships with the product. No licence text needs separate reproduction
(none of the five packages ships a `NOTICE` file requiring propagation, and none uses a licence —
e.g. Apache-2.0 with its own `NOTICE`-file clause — that would require more than the row this file
already provides). No source-disclosure obligation, no conflict with `LICENSE` (BSL 1.1) or with
selling Maran as a commercial product.

**Verdict: clear to ship.** MailKit/MimeKit and their full resolved transitive closure are MIT,
verified from the restored `project.assets.json` and each package's own `.nuspec`, two levels of
hand-walked dependency groups deep and cross-checked against NuGet's own complete resolution. The
only action item was mechanical — regenerate `THIRD-PARTY-NOTICES.md` — and is done (see below).

### CI gate — before and after, actually run in this pass

Unlike the first pass (no `cargo` on the sandbox `PATH`, so `maran licenses --check` could not be
run), this pass ran `source scripts/dev && maran licenses --check` for real:

```
THIRD-PARTY-NOTICES.md is out of date. Run: maran licenses
--- a/THIRD-PARTY-NOTICES.md
+++ b/THIRD-PARTY-NOTICES.md
@@ Rust — the agent
+| `serde_json` | 1.0.151 | MIT OR Apache-2.0 |
+| `zmij` | 1.0.23 | MIT |
@@ .NET — the panel
+| `BouncyCastle.Cryptography` | 2.6.2 | MIT |
+| `MailKit` | 4.16.0 | MIT |
+| `Microsoft.Extensions.Configuration.Abstractions` | 9.0.0 | MIT |
+| `MimeKit` | 4.16.0 | MIT |
+| `System.Formats.Asn1` | 8.0.1 | MIT |
+| `System.Security.Cryptography.Pkcs` | 8.0.1 | MIT |
```
(abbreviated to the rows relevant to this note; the `--check` script's own diff output was
consulted directly, not reconstructed from memory.) `Microsoft.Extensions.Configuration.Abstractions`
9.0.0 alongside the existing 9.0.19 row is unrelated to MailKit — a second, older resolved
version already present elsewhere in the solution graph that the previous regeneration missed —
not investigated further as out of scope for a MailKit-focused pass, but flagged since it is part
of the same drift the task warned about.

Then ran `maran licenses` (write mode) — output: `wrote THIRD-PARTY-NOTICES.md`. Re-ran
`maran licenses --check` immediately after: output `NOTICES-OK`. The regenerated file is left
**in the working tree, uncommitted**, per instruction — `git diff --stat -- THIRD-PARTY-NOTICES.md`
shows `1 file changed, 8 insertions(+)` right now (7 new rows above plus the one already-diffed
`Microsoft.Extensions.Configuration.Abstractions` 9.0.0 row).

**This is a checkpoint, not the last word.** Other agents are actively landing code on this branch
right now (cron, firewall, monitoring, tasks). Any further change to `agent/Cargo.lock`,
`backend/Directory.Packages.props`, `frontend/package-lock.json`, or `scripts/**` will make
`THIRD-PARTY-NOTICES.md` stale again. **`maran licenses` must be re-run immediately before this
branch is pushed**, not assumed complete from this note.

---

## What already ships — the existing attribution mechanism, and whether these three fit it

The repository is **not** starting an attribution mechanism from zero — `THIRD-PARTY-NOTICES.md`
already exists (introduced in commit `3eb6ee5`, "chore: third-party notices, and no Dependabot",
and already carrying ~230 rows across Rust, .NET and npm as of `2ba0450`, the Plan 3 merge). The
mechanism is real and reasonably rigorous:

- **Generated, not hand-maintained**: `scripts/lib/licenses.sh`, invoked as `maran licenses`
  (write) or `maran licenses --check` (verify, changes nothing). It reads licences from metadata
  already on disk for all three ecosystems — `cargo metadata` against `Cargo.lock` for Rust, each
  restored package's own `.nuspec` for .NET (sourced from `project.assets.json`, i.e. what actually
  resolves — not from `Directory.Packages.props`'s declarations, which can include unused entries),
  and each installed module's `package.json` for npm (via `npm ls --omit=dev --all`, so dev/test
  tooling is correctly excluded from what's declared "distributed"). An unreadable licence is
  treated as a hard error ("not declared" is written explicitly rather than silently guessed), and
  the file documents why: an earlier version wrote "see package" as a placeholder, which made the
  generated output depend on the machine that generated it and defeated the CI comparison — worth
  noting because it shows the mechanism has already been hardened once against exactly the kind of
  silent gap this pass was asked to look for.
- **Enforced in CI, not just documented**: `.github/workflows/notices.yml` runs `maran licenses
  --check` on every push/PR that touches `agent/Cargo.lock`, `backend/Directory.Packages.props`,
  `frontend/package-lock.json`, `THIRD-PARTY-NOTICES.md`, `scripts/**`, or the workflow file
  itself. This branch touched `agent/Cargo.lock`, so **this exact CI job will run and will fail**
  on the current state of this branch, for the reason established above (missing `serde_json` /
  `zmij` rows) — this is not a hypothetical risk, it is the concrete, mechanical gate that catches
  the one real gap this pass found.
- **The header of `THIRD-PARTY-NOTICES.md` itself states the obligation plainly**: "Maran is
  distributed as binaries built from the packages below... this file is the attribution those
  licences require."

**Do `serde_json` and `rustix` fit the established pattern?** Yes, exactly — they're ordinary
`crates.io` dependencies resolved through the same `Cargo.lock` every other Rust row comes from;
nothing about them needs a different treatment. **Does `MailKit` fit the pattern, now that it's
actually landed?** Confirmed yes, not just predicted: this pass restored the affected projects,
read `project.assets.json`, and ran `maran licenses` for real — `MailKit`, `MimeKit`, and their
whole transitive closure (`BouncyCastle.Cryptography`, `System.Security.Cryptography.Pkcs`,
`System.Formats.Asn1`) now sit in the ".NET" table as five ordinary rows, produced by the same
generator, same as every other MIT package there. There is no gap in the *mechanism* — it is
well-built, CI-gated, and (as of this pass) exercised end-to-end for MailKit specifically, not just
argued about in the abstract. The gap this pass found and fixed was purely that the pattern **had
not been re-run** since `Cargo.lock` and `Directory.Packages.props` changed — mechanical staleness,
not a mechanism gap.

---

## Summary table

| Dependency | Version (verified, from a file) | Licence | In `THIRD-PARTY-NOTICES.md` (after this pass' regeneration)? | Action |
|---|---|---|---|---|
| `serde_json` | 1.0.151 (`agent/Cargo.lock`) | MIT OR Apache-2.0 | Yes — added by `maran licenses` this pass | None further |
| ↳ `zmij` (transitive) | 1.0.23 (`agent/Cargo.lock`) | MIT | Yes — added this pass | None further |
| `rustix` | 1.1.4 (`agent/Cargo.lock`) | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | Yes, already correct | None |
| `MailKit` | 4.16.0 (`project.assets.json`, `Directory.Packages.props`) | MIT | Yes — added by `maran licenses` this pass | None further |
| ↳ `MimeKit` (MailKit's only dependency) | 4.16.0 (`project.assets.json`) | MIT | Yes — added this pass | None further |
| ↳ `BouncyCastle.Cryptography` (MimeKit's dependency) | 2.6.2 (`project.assets.json`, not this repo's now-absent 2.7.0 direct pin) | MIT | Yes — added this pass (alongside a pre-existing stale 2.7.0 row from another resolution; not chased further) | None further, licence-wise |
| ↳ `System.Security.Cryptography.Pkcs` (MimeKit's dependency) | 8.0.1 (`project.assets.json`) | MIT | Yes — added this pass | None further |
| ↳ `System.Formats.Asn1` (MailKit's dependency) | 8.0.1 (`project.assets.json`) | MIT | Yes — added this pass | None further |

**One-line verdict:** Clear to ship on the licence merits for all four dependency groups —
everything found across both passes is MIT or Apache-2.0, singly or disjunctively, with no
copyleft anywhere in any transitive chain checked (two levels of hand-walked dependency groups for
`MailKit`, cross-checked against NuGet's own full resolution in `project.assets.json`) — and now
also mechanically clear: `maran licenses --check` failed at the start of this pass (stale against
both `agent/Cargo.lock` and `backend/Directory.Packages.props`) and passes (`NOTICES-OK`) after
`maran licenses` regenerated the file. The regenerated `THIRD-PARTY-NOTICES.md` is left uncommitted
in the working tree per instruction. **This is a checkpoint, not a final clearance** — other agents
are actively landing code on this branch, so `maran licenses` must be re-run immediately before
push to catch any further drift this pass could not see.
