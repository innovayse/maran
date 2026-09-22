# The backup object store seam — an open contradiction, and the two ways out

**Status: for the owner to decide. Nothing here has been actioned, and the spec has
deliberately NOT been amended — that is the owner's call.**

Date: 2026-09-07. Subject: `agent/crates/ops/src/backup/object_store_host.rs` and the
promise in `docs/superpowers/specs/2026-08-29-maran-design.md` §209.

## The contradiction, stated plainly

The spec promises, twice:

- §32 (v1 scope): «бэкапы (файлы + БД, локально и S3, восстановление из UI)»
- §209: «хранение локально и S3-совместимо»

The code does not do the second half. An S3 destination cannot be created, restored,
listed or deleted:

- `ObjectStoreHost` — the trait the remote half would go through — has no caller. Its own
  doc comment now opens **"NOTHING CALLS THIS TRAIT YET"**
  (`agent/crates/ops/src/backup/object_store_host.rs:12`), after three earlier doc comments
  in this area were found asserting the seam was wired and were corrected.
- `grep -rn "ObjectStoreHost" --include=*.rs agent/crates/` answers the trait, its two
  implementations, three `mod.rs` re-export lines and the two test modules. Nothing else.
- The four operations (`create_backup`, `restore_backup`, `list_backups`, `delete_backup`)
  take a `LocalBackupRoot` and touch the filesystem directly. An S3 destination cannot even
  be *named* at that boundary.
- The agent's `BackupService` refuses a remote destination with `NotImplemented`
  (`agent/crates/agent/src/services/backup/remote_destination_refused.rs`), and the panel
  refuses one too (`backend/src/Maran.Modules/Backups/Mappers/BackupDestinationMapper.cs`).

So the product's behaviour is coherent and honest — a customer is refused, not silently
given a broken backup — and it is coherent with everything except the spec. That last
mismatch is what this note is about, and by `rules/architecture.md` ("a doc comment that
describes what the code does not do is a defect of the same severity as the behaviour it
misdescribes") a spec line in that state is the same class of defect, one level up.

## Why it was left unwired — three measured obstacles

The wiring attempt's full working was kept only as a scratch report, not committed; summarised
here, because the decision below rests on them:

1. **A listing cannot be expressed in `{key, bytes}`.** `list_backups` refuses a name that is
   not valid UTF-8 (`BackupError::UnmintedArtifactName`) and a directory or symlink wearing an
   artifact's name (`UnreadableReason::NotARegularFile`). Both are facts about an *inode*;
   `ObjectStoreHost::list` answers `ObjectSummary`, which is a key and a byte count, and a
   bucket has neither directories nor links. Routing the listing through the seam would not
   relocate those two refusals — it would delete them, and four named tests with them. A
   listing also needs the sidecar's *contents*, and the trait can only `get` an object into a
   file: N backups would become N downloads.
2. **Every LOCAL backup and restore would gain a full extra copy of the artifact.** Today
   `create_backup` has `tar` write straight into the destination and publishes with one
   `rename`; `restore_backup` makes five `tar` passes over the artifact in place. Through the
   seam the creation must build elsewhere and `put`, and the restore must `get` before the
   first pass — for a local destination a byte-for-byte copy of an artifact bounded only by
   `MAXIMUM_ARCHIVE_BYTES` (64 GiB). That is a disk-headroom change for every existing local
   install, not a refactor.
3. **The publish path's inode re-check has no home in `put`.** `create_backup`'s publication
   re-checks `is_file`, uid, exact mode and `nlink == 1` on the artifact it is about to name.
   `put` has nowhere to carry that, so folding publication into the seam loses it.

A fourth, adjacent: a remote restore must stage the whole artifact locally (`tar` takes a
path), and no operation in `ops` consults free space before staging. The primitive exists —
`maran_agent_core::utils::available_bytes` — and has no caller (measured this session).

## Option A — amend the spec: v1 storage is local only

Change §209 and §32 to promise local destinations in v1 and name S3 as post-v1.

**What it costs**

- A product promise is withdrawn from v1. If S3 was sold, offered, or is load-bearing for the
  hosting-panel integration, this is a commercial decision and not an engineering one.
- Task 6's ~1240 lines (`s3_object_store_host.rs`, `local_object_store_host.rs`,
  `object_key.rs`, `PublicReadVerdict`, their tests) and the S3 client dependency in
  `Cargo.toml` / `THIRD-PARTY-NOTICES.md` become unexercised code with no consumer, which
  `rules/architecture.md` (DRY/YAGNI, "no speculative abstractions") does not permit
  indefinitely. Honest execution of Option A therefore includes deciding whether to *delete*
  that code and the dependency, or to keep it behind an explicit "post-v1, kept deliberately"
  note. Keeping it silently is the state we are already in.
- The public-bucket probe, the credential redactor and their threat-note paragraphs
  (`docs/superpowers/notes/2026-09-05-backups-threat-note.md`) become descriptions of
  post-v1 code.

**What it buys**

- The spec, the agent's `NotImplemented`, the panel's refusal and the code all say the same
  thing on the same day. No further work is required for correctness.
- Plan tasks 6, 11 and 16's S3 halves become explicitly deferred rather than silently unmet.

## Option B — give the seam the two methods it lacks, then wire it

The seam as written cannot be wired at any price, because obstacles 1 and 2 are both
consequences of a trait that speaks only in whole-object `put`/`get` against a path. Wiring
therefore starts with extending the trait:

1. **A publish that a local destination can satisfy by `rename`** — an operation that takes
   an already-written artifact *in the destination's own staging area* and makes it visible
   under its key, so the local implementation keeps its single `rename` and its inode re-check
   (obstacles 2 and 3), while the S3 implementation performs the multipart upload it needs.
2. **A small-object read into memory** — so a listing can read a sidecar without downloading
   it to disk, and `list` can answer something richer than `{key, bytes}` that carries the
   local implementation's two inode verdicts as first-class values a bucket simply never
   returns (obstacle 1). The refusals must be *represented*, not deleted.

**What it costs**

- Trait redesign plus re-signaturing all four operations, each with its own test suite; the
  listing's four inode tests must survive the move, by construction, not by hope.
- A staging free-space ceiling before any remote restore may ship: `available_bytes` gains its
  first caller, and `AgentPaths::BULK_SCRATCH_ROOT` gains a documented ceiling. Without it a
  remote `get` is bounded only by 64 GiB.
- Panel and proto work: destinations, credentials and the public-bucket probe are Task 11 and
  are not built. The panel's refusal path and the agent's `NotImplemented` both come out.
- A live pass against a real S3-compatible provider — the existing tests run against a local
  HTTP stub, which by its own test-module doc proves our request shape and our redaction and
  proves nothing about a provider's policy semantics.

**What it buys**

- The spec's promise is kept, the 1240 lines gain a consumer, and the dependency earns its
  place in the notices.

## The third option, named so it is not chosen by default

Leaving it exactly as it stands is honest today — every doc comment in the area now says what
is true — but it is 1240 lines of unexercised code, one dependency, and one spec line that is
wrong. Drift starts the moment the next reader believes §209.

## Recommendation, offered not taken

Option A is the smaller, more truthful change *if* S3 is not a v1 commitment to a customer;
Option B is the only one that keeps §209. Either way the decision should also settle what
happens to Task 6's code, because "keep it and say nothing" is the state that produced this
note.

## Related

- The wiring attempt's own report (its refusal and the mutation evidence) is summarised above,
  under "Why it was left unwired"; it was kept only as a scratch file, not committed.
- `docs/superpowers/notes/2026-09-05-backups-threat-note.md` — the S3 credential path.
- `docs/superpowers/plans/2026-09-05-maran-backups.md` — Tasks 6, 11, 16.
