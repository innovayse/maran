# Threat note: the agent packs and unpacks zip and tar.gz archives in a customer's home (issue #168, part 3 of #141)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
agent's privileged surface. This change makes the root agent (`maran-agent`, the only root process)
drive parsers for two formats a hosting customer supplies, and write what those archives describe into
the customer's home. The primitives it stands on (the fork that drops to the account, the descriptor
walk, the byte channel back from the child) are argued in
`2026-11-07-file-manager-browse-threat-note.md` and `2026-11-07-file-manager-mutations-threat-note.md`
and are not restated. Written before the code; where the code differs, the code is wrong or this note
is amended in the same change.

**Second review: OUTSTANDING (#331) — must not merge to main.** The work is done by agent sessions,
which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human has read the
sections named in "What a reviewer must check" (rules/security.md "Sensitive change escalation"). The
owner is told at hand-off that this surface carries an outstanding review. Nothing here discharges it.

## What is added

1. Three dependencies in `maran-ops` only, none of which may touch the filesystem: `zip` (feature
   `deflate-flate2` only), `tar` (default features off, so no `xattr`) and `flate2` (pure-Rust backend).
   All three parse bytes a customer chose.
2. `CreateArchive` and `ExtractArchive` implemented. The contract already carried their caps and the
   error code `ARCHIVE_UNSAFE`; this change adds no field, no rpc and no code, only the behaviour (and
   rewrites the comments that said "UNIMPLEMENTED").
3. `agent-core::validation::fs`: `ArchiveMemberName`, `ExtractBounds`, `CreateBounds`.
4. `ops::files`: `ArchiveHost` (a third seam beside `FilesHost` and `BrowseHost`), `extract_archive`,
   `create_archive`, and a crate-private `archive/` folder holding the steps.

No new rpc name, no process spawn (no `tar`, `unzip` or `gzip` binary), no new port, no outbound call,
no new `unsafe`, no change to `agent-core::privs`: the archive operations ride the existing
`fork_as_account_for_bytes`, which already returns a bounded byte string from the dropped child.

## What an attacker could do, and why it is safe

The attacker supplies an archive to extract, or chooses what to archive. The classic outcomes: write
outside the destination (zip slip), plant a link a later member writes through, plant a device or FIFO,
exhaust disk, memory, entries or time (a bomb), collide with existing files, overwrite a file the
account did not mean to lose, or get root to parse a hostile structure.

**Who parses.** The archive is opened, judged and unpacked inside the forked child that has dropped to
the account. A bug in a parser is then code execution as the account, in a process holding the archive,
the home and one pipe: the same asymmetry the restore path argued, extended from "run `tar` as the
account" to "run the parser as the account". The root parent receives a record of at most 17 bytes (a
tag and two figures, or a tag and a refusal code), decoded strictly; any other length, tag or code is
"the child did not answer" and is never repaired.

**The archive crates' own path handling is never used.** `enclosed_name`, `mangled_name`, `path()`,
`unpack`, `unpack_in` and `extract` are called nowhere. A member's name is taken as RAW bytes and
parsed by `ArchiveMemberName::parse`, which refuses: empty, over 4096 bytes, not UTF-8, any backslash,
a leading `/`, a doubled separator, a `.` or `..` component, a NUL or other control character, a
component over 255 bytes, more than 64 components. The writer consumes nothing else. The one tolerance
is a single leading `./` on a tar name, which `tar` writes when told to archive `.`; `./../x` is still
a traversal, and a bare `./` is the destination itself.

**Judged twice, from the same code.** Pass 1 reads every member (a zip's central directory without
decompressing; a tar.gz's headers through a bounded stream) and refuses the archive as a whole, with
nothing written, for: a symbolic or hard link, a device, FIFO, socket or sparse member, an unsafe name,
an encrypted zip member, a compression method other than stored or deflate, two members of one name, a
member written below a name another member made a file, declared sizes or entry count over the
request's caps, expansion past the ratio, and non-zero bytes after a tar's end marker. Pass 2 repeats
every check while writing, because the archive is the customer's file and may differ from what pass 1
saw. The scan is an early refusal; it is not what makes the writer safe.

**Caps.** The request states `max_entries`, `max_expanded_bytes` and `max_ratio`; the agent holds
ceilings (1,000,000 entries, 20 GiB, 1000:1) and refuses a request above them rather than clamping.
Declared sizes are charged in pass 1; bytes actually produced are charged in pass 2, so a header that
lies about its size gains nothing: more than declared is `SizeMismatch`, fewer is a damaged archive.
The ratio is judged only once expansion passes 1 MiB, so a small, honestly compressible archive is not
refused, and the byte cap applies regardless. A zip's declared entry count is read from the end of the
file and refused before the zip crate (which reads the whole central directory into memory) sees it.
The decompressed stream of a `.tar.gz` is bounded independently of any header (byte cap plus 4 KiB per
entry plus 1 MiB), so padding, endless zero blocks or a gzip member that never ends cannot run on.
Exceeding the entry or byte cap is `LIMIT_REACHED`; an archive past the ratio is `ARCHIVE_UNSAFE`
(`LimitKind` has no ratio, and a bomb is by definition not an archive to write).

**Writing.** Through descriptors only. Each member's parent is reached from the home with `openat`
(`O_NOFOLLOW | O_DIRECTORY`), every level required to be a directory the account owns; missing levels
are made with `mkdirat` and widened to 0755 on their own descriptor. Files are created
`O_CREAT | O_EXCL | O_NOFOLLOW`, which refuses a regular file, a directory and a symlink (resolving or
not) at the name alike. A member can therefore never be written through a link: the archive cannot
contain one, and a link the customer plants in the destination mid-run is refused. Modes are not taken
from the archive: files become 0644 (0755 when any execute bit is set), directories 0755, no owner and
no time, so a setuid, setgid, sticky or world-writable mode in an archive has no effect. Nothing is
overwritten (`ALREADY_EXISTS`); an existing directory is merged into. A member that would land in the
account's trash (`.maran-trash` directly in the home) is refused, as are the archive, destination and
sources of a request that name it. The service layer refuses the paths before the fork, and the
operations refuse them again inside the child (`refuse_reserved_location` for the archive, destination
and sources; `refuse_reserved_member` for members), so a caller that skipped the service layer still
cannot read from or write into the trash.

**Failure.** A failure during unpacking (quota, I/O, a collision) removes what this run created, tracked
in an in-memory ledger bounded by the entry cap and recorded only when a creation SUCCEEDED, so a name
that already existed is never touched. Directories are removed only while empty. A child killed by the
agent's patience (two minutes, `fork_as_account_for_bytes`) cannot clean up and may leave the files it
had written; they belong to the account. That patience is the existing primitive's, not this change's.

**Create.** Sources are measured first, by `fstatat` with `AT_SYMLINK_NOFOLLOW`; a link, FIFO, socket or
device anywhere in a source, or a name an archive cannot hold, refuses the WHOLE request, because an
archive that silently omits what was selected is a backup that fails on the day it is needed. Sources
are then re-opened by descriptor without following links, `fstat`ed on the descriptor (regular file
only; `O_NONBLOCK` keeps a FIFO planted since the measure from blocking the open) and read for exactly
the size `fstat` reported. The archive is written to a temporary file beside the destination, created
`O_EXCL`, and published with a rename that cannot replace; it is complete at its destination or absent.
A destination inside a source, the home as source or destination, and overlapping sources are refused
before the fork. Recorded metadata is the same three modes an extraction produces.

**Denial of service.** CPU and memory: a zip's directory is bounded by the entry cap before it is held;
a tar.gz streams through fixed buffers and is never materialised; the per-name registry of a 1,000,000-
entry archive is tens to a couple of hundred megabytes in the child (see below). Time: the existing
two-minute patience; at most 4 archive operations in flight on the host and 2 per account, refused with
`AGENT_SATURATED`, never queued. Disk: the free-space floor is checked before the child starts, bytes
are counted as written, and `EDQUOT` is reported as the account's quota (`ENOSPC` stays a system
failure).

## What a reviewer must check

- `ArchiveMemberName::parse`: every refusal in the list above has a test, and an accepting control.
- That extraction consumes only `ArchiveMemberName`, never the crates' own name or path accessors
  (`grep -nE 'enclosed_name|mangled_name|\.path\(\)|unpack|\.extract\(' agent/crates/ops/src/files/archive`
  should find nothing outside tests and comments; run it with `/usr/bin/grep -E`).
- That the archive crates are built with the features named in `agent/Cargo.toml` and no others
  (no `bzip2`, `zstd`, `aes-crypto`, `xattr`, no C-backed backend): `cargo tree -e features -p maran-ops`.
- `walk_zip_members` and `walk_tar_gz_members`: every `ArchiveRefusal` they can return, and that pass 2
  re-judges rather than trusting pass 1.
- `DirectoryCursor::enter`: `O_NOFOLLOW | O_DIRECTORY` on every level, ownership checked, and the
  creating and read-only modes kept apart.
- `ExtractionLedger::roll_back`: it removes only what was recorded, newest first, directories only
  while empty, and reaches each parent by descriptor.
- `create_in_home`: the temporary is `O_EXCL`, the publish cannot replace, and the temporary is removed
  on every failure.
- The dependency versions and the advisory state below.

## Dependencies (rules/security.md item 11)

| crate | version | features | why it is here |
|---|---|---|---|
| `zip` | `=9.1.3` | `deflate-flate2`, defaults off | reads the central directory and writes zip64-capable archives; hand-writing a zip reader in a root daemon is worse |
| `tar` | `=0.4.46` | defaults off | reads and writes tar headers (GNU long names, pax size); no `xattr` |
| `flate2` | `=1.1.10` | defaults (pure Rust) | the gzip layer, and the deflate engine behind `zip` |

Advisories read by hand from the RustSec database on 2026-11-07 (`cargo audit` is not a CI gate here,
rules/testing.md): `zip` RUSTSEC-2025-0168, -2026-0367, -0368, -0369 and -0429 are all patched in 9.0.0
and concern the crate's `extract` helpers, which this agent does not call; `tar` RUSTSEC-2026-0067 and
-0068 are patched in 0.4.45, and concern `unpack_in` and a pax-size discrepancy; no advisory was listed
for `flate2` at the time of reading. `maran licenses` must be run to record the licences.

## What the author could not verify

- Nothing in this change has been compiled or run: the lead's gate runs every build and test once all
  coders have finished. The hostile-archive fixtures are hand-assembled and were never fed to the real
  crates; a few outcomes (a sparse tar member, a zip whose header understates its size) are asserted as
  "refused as one of two refusals" in the unit tests because which of the two the crate produces was not
  observed.
- Parser behaviour on malformed input beyond those fixtures. No fuzz run is part of this change; the
  note does not claim the crates are free of bugs, only that a bug runs as the account with no
  descriptor but the archive, the home and one pipe.
- Memory of the registry and the zip crate's central directory at the entry ceiling on a small host. The
  default cap (100,000) is well inside; 1,000,000 was not measured.
- Whether a gzip header with a pathological extra field or name can cost more than the byte cap suggests
  (the stream bound counts decompressed bytes, not header bytes).
- An archive whose end-of-central-directory record is crafted so that the early count read and the zip
  crate disagree about which record is the real one: the per-entry budget still holds, but only after
  the crate has allocated.
- That a sustained extraction fits in `fork_as_account_for_bytes`'s fixed two-minute patience. A 2 GiB
  extraction on a slow disk may not; the fix (a patience argument) belongs to that primitive, not here.
