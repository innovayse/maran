# Threat note: file manager agent mutations: create, move, copy, chmod, trash, delete, framed upload (issue #167)

`rules/security.md` requires a threat note, and a second reviewer, for a change to the agent's privs
module. This change grows `agent-core::privs` (a reporting fork, a parent-to-child framed input pipe, a
caller-chosen patience, a no-replace rename between two directory descriptors) and puts nine new rpcs
and one changed rpc of `FilesService` on top of it. It is the mutation half of the file manager (#141);
the browse half and the common walk primitives are #166
(`2026-11-07-file-manager-browse-threat-note.md`), which this note builds on and does not restate.
Archives (#168) are covered by their own note.

**Second review: OUTSTANDING (#331) — must not merge to main.** The work is done by agent sessions,
which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human has read the
sections named in "What a reviewer must check" (rules/security.md "Sensitive change escalation"). The
owner is told at hand-off that this surface carries an outstanding review. Nothing here discharges it.

## What is added

1. **`fork_reporting_as_account`** in `agent-core::privs`: a second entry point beside
   `fork_as_account`, sharing its drop sequence (`drop_to_account`: `setgroups`, `setgid`, `setuid`,
   then a re-read and verification, in the child). It adds (a) a caller-chosen patience, (b) a fixed
   17-byte report from child to parent (`encode_report` / `decode_report`: one status byte, two
   little-endian `u64`), (c) an optional parent-to-child framed byte pipe (`ChildInputWriter`,
   `InputFrame`). `fork_as_account` is unchanged for its existing callers.
2. **`rename_no_replace_between_directories`**: `renameat2` with `RENAME_NOREPLACE` between two held
   directory descriptors, one wrapper file with its own SAFETY argument and the flag forced, never a
   parameter.
3. **Rpcs of `FilesService`:** `CreateDirectory`, `MoveEntry`, `ChangePermissions`, `CopyEntry`,
   `ForceDeleteEntry`, `TrashEntry`, `ListTrash`, `RestoreTrashEntry`, `EmptyTrash`,
   `PurgeExpiredTrash`; and `WriteFile` gains two additive header fields (`fail_if_exists`, a byte cap)
   and writes through the framed channel. Every one except `ListTrash` is in `MUTATING_RPCS`.
4. **A per-account trash, `.maran-trash/`, directly in the home**, owned and written by the account,
   with `entries/<id>/` (the entry's `payload` and its `original` path) and `staging/<id>/` for copies.
5. **Bulk admission slots**: host-wide and per-account counting semaphores (`BoundedSlots`, shared with
   browse, archive and run-by-hand) that refuse with `ERROR_CODE_AGENT_SATURATED` at 4 per host and 2
   per account.
6. **Additive error codes** for the closed refusal list a child can report (quota exceeded, crosses
   device, too deep, unsupported entry, not owned, into itself, invalid trash entry, and the entry and
   byte limits). `PROTO_VERSION` and the panel window move once, in the lead's combined bump.

Not added: no daemon, port, outbound call, file on disk outside a customer's own home, database, new
crate, shell, spawned program, or rpc that executes caller input. The agent stays stateless: the trash is
files in the customer's own home, not agent state.

## What the customer controls

As in the browse note: every directory and name in the tree being walked, and the right to replace any of
them between two syscalls (symlink, hardlink, FIFO, device node, bind-mount point, directory swapped for
another they own), to build very deep or very large trees, to race the agent from a shell or from PHP,
to edit their own trash, and to send any request the panel forwards. The panel validates; the agent
re-validates everything and trusts no value it did not parse into a type.

## What an attacker could do with each surface, and why it is safe

**Containment.** Every rpc reaches its target through `open_parent_directory` / `open_mutable_parent`
(descriptor descent from the home, `O_NOFOLLOW` and an ownership check at every level) and runs inside
the forked child that has dropped to the account. `resolve_in_home` is not the control for any mutation.
A component swapped for a symlink between check and use is refused, not followed. The recursive walkers
in `ops/src/files/tree/` descend with `openat(O_NOFOLLOW | O_DIRECTORY)` from a descriptor they hold,
never build a path string to reopen, never follow a link at any depth, and refuse to cross onto another
device (`st_dev` compared with the root of the operation), so a bind mount inside a tree cannot widen it
(`CrossesDevice`).

**Move and restore.** `renameat2(RENAME_NOREPLACE)` between two held directory descriptors. The
destination is never overwritten, a symlink at the destination name is neither followed nor replaced,
and a directory moved into its own subtree is refused (`IntoItself`). `EXDEV` is the typed refusal
`CrossesDevice`; there is no copy-and-delete fallback, so a move can never become a partial copy. A
filesystem that does not support the flag answers `EINVAL`, which is a typed failure, not a fallback to
a replacing rename.

**Copy.** A pre-scan (`scan_tree`) measures entries and bytes against the caps and refuses the whole copy
on any entry that is not a regular file or a directory (`UnsupportedEntry`), deeper than 128 levels
(`TooDeep`), or past the entry and byte ceilings (`LimitReached`), before anything is written. The copy
is staged under `.maran-trash/staging/<id>/` and renamed into place with `RENAME_NOREPLACE`, so a copy
is all or nothing at the destination and a stopped copy leaves nothing visible. A source file is opened
`O_NOFOLLOW | O_NONBLOCK` and judged by `fstat` on the descriptor before it is read, so a FIFO planted
after the scan cannot block the child and a swap to a link is refused. The byte counter, not the scan's
estimate, enforces the cap. Permission bits are kept; owner and times are not. A free-space floor
(issue #148) is asked as root before the fork, as for uploads.

**Cleaning a failed copy, and emptying the trash.** The copy keeps the source's bits, so a directory
copied as `0555` and then a later failure (or a refused final rename) leaves a staged tree the account
cannot empty; a directory trashed as `0555` would likewise hold its bytes in the trash forever. The trash
and its staging area are panel-managed space, so everything that clears them removes with
`DirectoryAccess::Reclaimed`: the copy's own cleanup, the retention purge (expired entries and the
staging sweep) and EmptyTrash, all through `remove_trash_entry`, which has no other mode. Each directory
entered is `fchmod`ed to `0700` on the descriptor the walk already proved is a non-link directory owned by
the account, then emptied; nothing is widened by name, and a directory of another owner or on another
device is still refused. Only the customer's own force delete stays `DirectoryAccess::AsFound`, so a mode
the customer chose on live data is still reported to them and never overridden. An entry the purge keeps
(unexpired) is not touched at all, mode included.

**Chmod.** The mode is a `FileMode` (the nine permission bits; setuid, setgid and sticky are refused,
never masked). The recursive form calls `fchmod` on descriptors opened with `O_NOFOLLOW`; a symlink in
the tree is skipped and counted, never followed, and a tree root that is a link is refused. An entry the
account does not own is skipped and counted (`PermissionReport`).

**Force delete.** `unlinkat` by name inside a pinned directory descriptor, directories emptied
depth-first through descriptors in passes of bounded size, `AT_REMOVEDIR` only on an empty directory. A
link is unlinked, not followed. A tree deeper than the ceiling is refused, not half deleted by guessing.
The operation is idempotent: a second run finishes what a killed first run left.

**Trash.** `.maran-trash/entries/<id>/payload` and `.maran-trash/entries/<id>/original` (the original
relative path, written by the account). The trash is the customer's own data and the customer can edit
it, so nothing in it is trusted: the id is parsed into `TrashEntryId` (`<unix seconds>-<16 hex>`), the
original path is re-parsed as a `MutablePath` on restore, restore is no-replace, an entry that does not
match the expected shape is `InvalidTrashEntry` and is not listed, and a forged `original` can only aim
the restore at a place the account could already write. The reserved first component `.maran-trash` is
refused by `MutablePath` for every other rpc, so the file manager cannot move the trash into itself or
delete it by accident. The trash lives in the home on the same filesystem: a trash move is an O(1)
rename that cannot escape the home and counts toward the account's quota (so it is not free storage).
`ListTrash` answers through the byte channel under a 48 MiB ceiling (`TooLarge`).

**Streaming write and the framed channel.** The parent feeds the child over a pipe in frames
`[length][bytes]`, at most 64 KiB each; a frame of length zero is the terminator. End of pipe, a broken
pipe or an oversized frame WITHOUT the terminator reads as `InputFrame::Aborted`, and the child
publishes nothing and unlinks its temporary file. A cap exceeded, a stream dropped or a deadline passed
is an abort, so a partial file is never renamed into place. The temporary file is created with
`O_EXCL | O_NOFOLLOW` beside the target (an existing name is never reopened), `fchmod`ed on its
descriptor, and renamed over the target, or with `fail_if_exists` renamed with `RENAME_NOREPLACE`.
Missing parent directories are created inside the same child, so there is no window between creating
them and writing. The byte cap is counted in the parent (a refusal there is reported as the cap, not as
the abort it causes) and again in the child. Bytes the parent chose to send are data the child writes
into a file the account owns; nothing the child sends back is read as anything but integers.

**The report channel.** The child writes exactly 17 bytes to a pipe the parent reads with a fixed-size
read: a status byte (0 success, 1..=63 a refusal number, anything else malformed) and two `u64`
figures. The parent decodes integers only; no length, no string and no path crosses. The refusal number
is mapped through one closed list (`FilesRefusal`, read by one function for every job that forks:
`files_error_of_privilege`); a number outside the list is a plain failure, so a confused or compromised
child cannot choose what the panel is told beyond that list. Until now the only way back from the child
was the exit status, on the argument that a channel from an unprivileged child into the root parent is a
surface. That argument is kept as far as it goes: this surface is a fixed-size integer read bounded by
the fork's deadline, and a short or malformed report is a failure, never trusted. The browse note
covers the larger byte channel and descriptor passing; they are separate entry points.

**Denial of service.** A bulk rpc holds a blocking-pool thread for up to its patience (120 s for a
single-step job, 600 s for a bulk one; a child that outlasts it is killed and reaped with a 5 s grace).
Each takes a slot first and refuses with `AGENT_SATURATED` at the limit (host 4, account 2), never
waits. Entry (1 000 000), byte (20 GiB), depth (128) and per-frame bounds apply to copy, chmod and
delete; the upload is bounded by the request's cap, itself bounded by 256 MiB. A customer who fills
their quota gets `QUOTA_EXCEEDED` from the errno, not a retry loop.

**Accountability.** All mutating rpcs are in `MUTATING_RPCS` and produce one `maran_audit` line each
(method, outcome, account). The line carries no path and no trash id, by the audit layer's own design
(`2026-11-06-agent-audit-lines-threat-note.md`), so "who deleted `public_html/wp-config.php`" is
answered only by the panel's journal (#169). That is a stated limit, not an oversight.

**Cancellation classes** (rules/rust.md "Async and blocking"): every mutation is run to completion
(idempotent, or staged and renamed, so a dropped client leaves nothing to reclaim); `WriteFile` stops
(a vanished sender is an abort and nothing is published); `ListTrash` is a read and stops. Each handler
states its class in its doc comment.

## What a reviewer must check

- `fork_reporting_as_account.rs`: the drop sequence is the same code as `fork_as_account` (shared, not
  copied); the child closes every inherited descriptor except the pipe ends it was given; the parent's
  feed and its wait obey one deadline (a stuck child cannot hold the parent in `write` past patience);
  the report read is fixed-size and a short read is a failure; the status byte `0xFF` and `0` can never
  be a refusal.
- `child_input_writer.rs` and `ChildContext::read_frame`: the terminator is the only success; EOF and
  an oversized frame are an abort on every path; `SIGPIPE`/`EPIPE` when the child dies mid-feed.
- `rename_no_replace_between_directories.rs`: flag forced; SAFETY comment; `EINVAL`/`EXDEV` mapping in
  `rename_failure.rs`.
- Every walker in `ops/src/files/tree/`: `O_NOFOLLOW | O_DIRECTORY` on every descent, `st_dev` check,
  depth and entry ceilings, no path string rebuilt and reopened.
- `MutablePath` and `TrashEntryId` cannot be built from a string with `..`, a separator, a control
  character or the reserved name; the trash id parse and `parse_trashed_original` are strict.
- `write_in_home`: temporary removed on every abort path; publication only after `Complete`.
- `files_error_of_privilege` and `FilesRefusal`: one closed list, both directions (`of` and
  `from_code`), numbers below 64.
- Each new rpc is in the right audit list and states its cancellation class.

## What the author could not verify

- The reporting fork under a real kernel: the closed-descriptor sweep with retained pipe ends and
  `SIGPIPE`/`EPIPE` behaviour when the child dies mid-feed are designed from the code read. The
  real-host (polygon) suites are where this is observed, and they were not run when this was written.
- `renameat2` / `RENAME_NOREPLACE` on filesystems of the supported matrix other than ext4 and xfs.
- Behaviour with disk quotas enabled: `EDQUOT` is mapped from the errno name, and whether the polygon
  images enforce quota is not known.
- That no path exists by which an account name differs from the account whose uid the child drops to;
  it is the existing `AccountName` / `AccountIds::resolve` chain, reused unchanged.
- That every unit-test mutation of the tree walkers is killed by a named test: `maran mutate` has not
  been run over the new `tree/` and `privs/` files.

## rules/security.md checklist (items 1-13) as applied

1. **Input:** `AccountName`, `RelativePath`/`MutablePath`, `FileMode`, `TrashEntryId`, bounded numerics,
   revalidated in the agent; the C# FluentValidators are #169.
2. **Paths:** descriptor walk, `O_NOFOLLOW` at every level, ownership at every level, inside the forked
   child; `resolve_in_home` locates nothing that is written.
3. **Shell:** none. No program is spawned.
4. **Config-file injection:** no config file is written; names reach the filesystem as single path
   components that refuse separators and control characters.
5. **SQL:** none.
6. **AuthZ:** at the panel (#169); the agent adds no cross-account reach.
7. **Configuration:** no new variable; bounds are agent constants.
8. **Secrets:** none handled; no content, name or id is logged.
9. **Crypto:** none.
10. **Surface:** no new port, daemon or outbound call; rpcs on the existing socket.
11. **Dependencies:** `tokio` with the `sync` feature only is added to `agent-core` for the shared
    `BoundedSlots` (it was already a dependency of `ops`); nothing else.
12. **Agent:** closed typed commands, idempotent, revalidated, audited; no "run this for me" rpc.
13. **Module reach:** the panel module that drives these rpcs declares the files capability (#169).
