# Threat note: the agent browses a customer's home — list, stat, read, name search (issue #166, part 1 of 6 of #141)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
agent's privileged surface. This change gives the root agent (`maran-agent`, the only root process)
the first operations that READ a hosting customer's tree and hand its contents back to the panel, and
it adds new primitives to `agent-core::privs`, the only module allowed to drop privileges. This note is
written BEFORE the code: every statement is a requirement the code must meet. Where the code lands and
differs, the code is wrong or this note is amended in the same change.

**Second review: OUTSTANDING (#331) — must not merge to main.** The work is done by agent sessions,
which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human has read the
sections named in "What a reviewer must check" (rules/security.md "Sensitive change escalation"). The
owner is told at hand-off that this surface carries an outstanding review. Nothing here discharges it.

## What is added

1. `proto/agent/v1/files.proto`: `ListDirectory` and `ReadFile` become real (additive fields only),
   and one new rpc `SearchEntries`. One new `ErrorCode`, `ERROR_CODE_TOO_LARGE`. `PROTO_VERSION` and
   the panel's window move together, in one combined bump.
2. `agent-core::validation::fs`: `BrowsePath` (the home allowed, deeper ceiling) and `SearchQuery`.
3. `agent-core::privs`: TWO new entry points beside `fork_as_account`, which stays the only place that
   drops privilege: one returns a bounded byte string from the dropped child, the other returns one
   file descriptor the dropped child opened. Plus `fdopendir`-based name listing, `sendmsg`/`recvmsg`
   descriptor passing, and `EntryStat` gaining size, mode and mtime.
4. `ops::files`: `list_directory`, `read_file`, `search_entries`, a shared descriptor walk
   (`open_home`, `open_level`, `open_directory_in_home`) extracted from `open_parent_directory`, a
   `BrowseHost` seam and a per-account and host-wide concurrency bound.
5. `agent` crate: the three rpcs in `FilesService`, wire builders, and an audit classification for
   `ReadFile`.

Not added: no daemon, port, outbound call, file on disk, database, new crate (hand-written codec, libc
already a dependency), shell, spawned program, or rpc that executes caller input. No mutation of a
customer's tree: these operations create, change and delete nothing.

## 1. What the customer controls

The customer owns EVERY directory in the tree being walked and can, between any two syscalls the agent
makes: replace a directory with a symlink, swap a directory for another they own, put a FIFO, device
node, socket or hardlink (to a file they can read) where the agent expects a file, create a directory
with millions of entries, nest directories deeper than any path limit, name entries with newlines,
invalid UTF-8 or 255-byte names, and fill a file as fast as the agent reads it. They can also fire as
many browse requests as the panel will forward. Everything below is shaped by that, and by the fact
that the agent is root.

## 2. How data leaves the dropped child (the new privileged surface)

The existing contract is "the child's outcome crosses back as an exit status; a channel from an
unprivileged child into the root parent is a surface this module exists to avoid". Browse cannot be
done without one. Options considered:

- **A. Do the walk and read as root** with `O_NOFOLLOW` and an ownership check at every level.
  REJECTED: rules/architecture.md ("File operations on customer data run under the account's UID,
  never as root"); with a dropped uid a missing check is harmless, as root a missing ownership check
  discloses any file on the host, and a single mutation survivor would be a vulnerability, not a
  coverage gap.
- **B. `setfsuid` on a runtime thread.** REJECTED: it is per-thread, changes capabilities in ways
  rules/rust.md "Privileges" does not allow ("`fork_as_account` is the only entry point; nothing else
  drops privileges") and it runs customer-steered syscalls inside the multi-threaded daemon.
- **C (chosen for list and search): a bounded byte channel.** `fork_as_account_for_bytes`: the parent
  creates a pipe BEFORE the fork; the child, after dropping and verifying, keeps only the write end
  (every other descriptor above 2 is still closed by the same sweep), writes one length-prefixed
  record set, and exits. The parent reads concurrently with waiting (a result over one pipe buffer
  must not deadlock the child against a parent that only waits), stops at a hard byte cap
  (`ResultTooLarge`, child killed), and accepts the bytes only if the child's exit status is OK. The
  parent decodes with a strict bounded codec and treats every length as hostile.
- **D (chosen for read): descriptor hand-off.** `fork_as_account_for_descriptor`: the child walks,
  opens the file `O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC`, and passes the open descriptor to the
  parent over a pre-made `AF_UNIX` socketpair (`SCM_RIGHTS`), then exits. The parent then reads from a
  descriptor, not a path. Why not C for reads: the child's lifetime is bounded by `CHILD_PATIENCE`
  (120 s, total), a 256 MiB download at a customer's link speed can outlast that, and a child that
  lives as long as the consumer is a customer-uid process held open by the customer's own network.

What the parent trusts from the child, stated exhaustively: nothing it can check. For D the parent
re-`fstat`s the received descriptor and requires a regular file, owned by the account's uid, with
exactly one link, BEFORE reading a byte (the child's claim is not evidence); it receives exactly one
descriptor with `MSG_CMSG_CLOEXEC` and refuses a truncated control message. For C the parent bounds
and parses; a hostile child can at worst make the answer an error. A child that is compromised (a
kernel filesystem bug reachable by the customer's tree) can already do anything the customer's uid can;
the new channels give it no capability it lacked except "send bytes to root", which is why the
parent's side is the code a reviewer reads first.

New `unsafe` is confined to `agent-core::privs` (`sendmsg`/`recvmsg`, `fdopendir`/`readdir`/
`closedir`, `pipe2`/`socketpair`), one file per syscall wrapper with its own `// SAFETY:` argument, as
the layout already requires.

## 3. Containment and the three attacks the issue names

All three browse operations reach their target through the descriptor walk inside the forked child:
the home is opened once (`/home` is root-owned, the account cannot rewrite `/home/<account>`), then
every component with `openat(O_RDONLY|O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC)` from the descriptor above,
requiring a directory owned by the account at each level. `BrowsePath::parse` rejects `..`, `.`, an
absolute path, an empty component, a control character and an over-long component at the boundary,
but that is advice: the walk is the containment (rules/security.md item 2; `resolve_in_home` is not
used by any browse operation).

- **Traversal.** `..` is refused at parse; a component that is a symlink to a directory elsewhere
  (`escape -> /etc`) is refused by `O_NOFOLLOW` at that level; a directory swapped for a link between
  two levels is refused because each level is reached from a descriptor, and a descriptor names an
  inode.
- **Symlink escape.** A symlink is never followed anywhere: not as a path component, not as the final
  read target (`O_NOFOLLOW`), not during a search (a symlink entry is reported with `is_symlink` and
  kind symlink, size = the link's own length, and is never descended into), not in a listing
  (`fstatat(AT_SYMLINK_NOFOLLOW)`: a link to `/etc/shadow` lists as a link, never with the target's
  size or mode).
- **Hardlink.** `ln /etc/passwd ~/x` is genuinely inside the home, so every path check passes it.
  `ReadFile` refuses an entry whose link count is not exactly one or which the account does not own,
  in the child AND again in the parent on the received descriptor. Cost, stated: a customer's own
  two-names-one-file is also unreadable here; that is the same stance as `DeleteEntry`, and the caller
  can copy it (#167). (`fs.protected_hardlinks` is not relied on: it is a sysctl the agent does not
  control and its state on the supported distributions has not been read from their documentation for
  this note — see "What the author could not verify".)
- **FIFO, device, socket.** Listing and search NEVER open an entry that is not a directory (a FIFO
  with no writer blocks an open forever; a device open has side effects): entries are `lstat`ed only.
  `ReadFile` opens with `O_NONBLOCK` so a FIFO returns instead of blocking the child, then `fstat`s and
  refuses anything but a regular file. A child blocked in the kernel is killable only by the parent's
  `CHILD_PATIENCE`, i.e. two minutes of a blocking-pool thread per request the customer may repeat, so
  non-blocking opens are a denial-of-service control and are mutation-scored as one.
- **Mount crossing.** Search does not descend into a directory whose `st_dev` differs from the
  directory it started in (`find -xdev`): a bind mount or another filesystem inside a home is out of
  scope of what a customer may browse through the search.

## 4. Bounds (denial of service against the root daemon)

Every one is a constant in the agent, a typed refusal when exceeded, and a mutation-scored line:

| Surface | Bound | Over the bound |
|---|---|---|
| path | `BrowsePath`: 64 components, 255 bytes each, 4096 total | parse refusal |
| directory read | 100 000 names read per listing | `TooLarge`, nothing returned |
| listing page | request `limit` default 200, hard max 1 000, `offset` | clamped / INVALID_INPUT |
| listing result | 16 MiB through the byte channel | `ResultTooLarge` |
| read | request `max_bytes` required (>0) and <= 512 MiB; size checked before streaming and bytes counted while streaming (`take(max_bytes + 1)`) | `TooLarge` (a file that grows past it mid-read ends the stream with the error; the panel discards the partial body) |
| read chunk / channel | 64 KiB chunks, 8-slot channel, a client that stops reading is dropped after 30 s | stream ends |
| search | depth <= 16, results <= 500 (request values clamped to these), **200 000 directory entries scanned** (a deterministic budget, not a clock) | `truncated = true`, never an error |
| search result | 4 MiB through the byte channel | `ResultTooLarge` |
| concurrency | 4 browse operations in flight per account, 16 per host, taken without waiting | refused with `ERROR_CODE_AGENT_SATURATED` |

The concurrency bound is a pair of counting semaphores in the shape of `ops::cron::run_now_slots`: it
refuses and never waits, so it adds no wait-for edge to the table in rules/rust.md "What this agent
serialises". Nothing in browse takes `config_tree_lock`, `account_lock` or any other lock: these
operations are read-only observations and wait for nothing.

A listing with names that cannot be sent (invalid UTF-8, or a control character that `BrowsePath`
would refuse on the way back) skips those entries and reports how many were skipped
(`skipped_entries`), because an entry the panel cannot name cannot be addressed by any later request
and must not be silently invisible.

## 5. Disclosure, secrets and the audit trail

- **Errors carry no path.** `FilesOpError` has no variant that holds one and stays that way; new
  variants (`TooLarge`, `NotADirectory`, `Saturated`) are payload-free. `AgentError.tool_output` is
  empty (no program is spawned). File content, names and sizes never reach a log: the agent logs
  nothing about a browse beyond what the generic layers log.
- **No secret type is involved.** The operations take an `AccountName`, a `BrowsePath`/`SearchQuery`
  and numeric bounds.
- **Who may ask.** Unchanged: the unix socket mode and `PeerGuard`. The panel authorizes the customer
  (tenant filter, 404 for another account's path, suspended customer refused, administrator allowed)
  in #169; the agent trusts the panel to name the right account and re-validates every value it is
  handed.
- **The trail.** `ReadFile` is journaled: one line per read on the existing `maran_audit` target with
  the validated account and the outcome and NO path, which is the independent record that "the agent
  was asked to read an account's file at time T" if `maran-api` is compromised. `ListDirectory` and
  `SearchEntries` stay unaudited (names only, and a UI polls them). The line carrying no path is a
  deliberate limit, stated: the trail says that a read happened, not which file. This needs a third
  audit class beside the mutating and read-only ones and an amendment of the audit threat note and
  rules/rust.md "Logging", both made in the change that adds it.

## 6. Cancellation class (rules/rust.md "Async and blocking")

- `ReadFile`: **stop.** Pure observation; the blocking pump asks whether the channel is still open
  between chunks and returns. The forked child is already gone (D hands off the descriptor), so a
  dropped client costs one closed descriptor.
- `ListDirectory`, `SearchEntries`: unary; a dropped client lets the short already-forked child finish
  (as every `run_blocking` rpc). Nothing is left behind: there is no state to reclaim.

## rules/security.md checklist (items 1-13) as applied

1. **Input:** `AccountName`, `BrowsePath`, `SearchQuery`, bounded numerics; revalidated in the agent.
   The C# FluentValidator half lives in #169.
2. **Paths:** descriptor walk, `O_NOFOLLOW` at every level, ownership at every level, inside the
   forked child. No path string is built, parsed or `resolve_in_home`d for browse.
3. **Shell:** none.
4. **Config-file injection:** nothing is written anywhere.
5. **SQL:** none (the agent has no database).
6. **AuthZ:** at the panel (#169). The agent adds no cross-account reach: the account name selects the
   home, and the walk requires every level to belong to that account's uid.
7. **Configuration:** no new variable (bounds are agent constants; the panel's 100 MiB / 1 MiB /
   256 MiB limits are #169 configuration and are strictly below the agent ceilings).
8. **Secrets:** none handled; no content or name logged.
9. **Crypto:** none.
10. **Surface:** no new port, daemon or outbound call; three rpcs on the existing socket.
11. **Dependencies:** none added.
12. **Agent:** `SearchEntries` is a closed typed command (a validated substring, not a pattern, glob or
    program); idempotent (pure reads); revalidated; `ReadFile` audited. No "run this for me" rpc.
13. **Module reach:** the panel module that drives these rpcs declares the files capability (#169);
    nothing in the C# declaration changes here.

## What an attacker could do with this surface

- **Read a file outside the home.** Needs a walk step that follows a link, skips the ownership check
  or reopens by path. The covering tests are real-host (symlinked component, symlinked leaf, link into
  `/etc`, link into another account's home) with positive controls, each mutation-scored.
- **Read another account's file by hardlink.** Needs the link-count and owner check to be skipped in
  BOTH the child and the parent. Scored independently, then together (rules/testing.md "two checks
  that mask each other").
- **Hang the agent with a FIFO.** Needs the open to lose `O_NONBLOCK`; scored by a test with a hard
  timeout.
- **Exhaust the agent's memory or blocking pool.** Needs a bound to be missing; each row of section 4
  has a mutant. Residual: 16 browse children at once, each up to a 16 MiB result, is 256 MiB of
  transient memory in the worst case.
- **Make the parent misparse the child's bytes.** Only with a compromised child; the codec is bounded
  and strict, and a decode failure is an error, never a partial listing.
- **Use the listing to attack the UI** (names with markup, bidi characters, 255-byte names): out of
  the agent's reach; the SPA renders text (#170). Control characters never leave the agent.

## What a reviewer must check

1. `privs/fork_as_account_for_bytes.rs`, `fork_as_account_for_descriptor.rs`, `send_descriptor.rs`,
   `receive_descriptor.rs`: the sweep spares exactly one descriptor; the drop-and-verify code is the
   SAME code as `fork_as_account` (split out, not copied); the parent re-validates; every length is
   bounded before allocation; a full pipe cannot deadlock the parent's wait; no `unwrap`/`expect`/
   `panic`; each `unsafe` has a `// SAFETY:`.
2. The split of `fork_as_account.rs`: a diff that moves code must move it verbatim
   (`git diff --color-moved`); the existing `privileges_on_a_real_host` suite must be unchanged and
   green.
3. `ops/files/open_level.rs` / `open_home.rs`: extracted from `open_parent_directory` without a
   behaviour change (that function is also the write path).
4. `read_in_home.rs`: `O_NOFOLLOW|O_NONBLOCK`, regular file, owner == account, `nlink == 1`, and the
   parent's second check.
5. `list_in_home.rs` / `search_in_home.rs`: no `open` of a non-directory; `AT_SYMLINK_NOFOLLOW`
   everywhere; the device check; the scan budget counts entries read, not entries matched.
6. Every constant in section 4 against its mutant in the scored table.
7. The `files_service.rs` handlers: three steps only, audit slot taken before the request is consumed,
   no logging.
8. The `rules/rust.md` amendments (files row, privs row, the `ReadFile` reserved sentence, the
   serialisation paragraph about the browse bound).

## What the author could not verify

1. Nothing here has run: no build, no test. Every statement is a requirement until a gate pass and a
   reviewer say otherwise.
2. `fs.protected_hardlinks` and `fs.protected_symlinks` defaults on Ubuntu 22.04/24.04, Debian 12/13,
   AlmaLinux/Rocky 9/10 — from each distribution's documentation, not memory. The design does not
   depend on them.
3. That `open(O_DIRECTORY|O_NOFOLLOW|O_NONBLOCK)` on a FIFO returns `ENOTDIR` immediately on every
   supported kernel (including the EL9 kernel); the real-host FIFO test with a timeout is the
   observation.
4. `d_type` reliability across filesystems (xfs, ext4, overlay in the polygon): the design `lstat`s
   every entry and ignores `d_type`, so this should not matter; not observed on XFS project quotas.
5. `st_dev` stability for the `-xdev` check on overlay and network filesystems.
6. Behaviour of an autofs mount inside a home (a stat can trigger a mount): out of scope, an operator
   configuration.
7. Whether 16 concurrent browse children at the result caps are acceptable on the smallest supported
   host (memory not measured).
8. Whether a customer can legitimately be left unable to read two-name files (hardlink refusal)
   without support cost.

## Amendment (issue #455): a closed exit status for "no such entry"

**Second review: OUTSTANDING (#331) — must not merge to main.** This amendment changes
`agent-core::privs`, so it carries the same debt as the rest of this note and discharges nothing.

**What was wrong.** `ReadFile` on a missing file answered `VALIDATION_FAILED` while `files.proto`
promises `NOT_FOUND`. The child (`read_in_home`) knew the entry was absent (`ENOENT` on the final
`openat`) but every failure of a closure crossed the exit status as the one `EXIT_WORK_FAILED`, which
the parent read as `PrivError::WorkFailed`, and `ProcessBrowseHost::map_fork_error` turned that into
`NotARegularFile`.

**What is added.** One more value in the closed set of exit statuses a child may end with:
`EXIT_NO_SUCH_ENTRY` (79, `child_exit_status.rs`), and one more `PrivError` variant,
`NoSuchEntry`, which carries nothing. A work closure asks for it by returning exactly
`PrivError::NoSuchEntry`; `fork_and_run` turns that one value into the status, every other closure
error and every panic stays `EXIT_WORK_FAILED`, and `wait_for_child::outcome_of` turns the status
back into the variant. The browse host's child maps only `FilesOpError::NotFound` (a missing final
name) onto it; a directory, a symlink stopped by `O_NOFOLLOW`, a hard link, another owner's file, a
FIFO and a failed walk of the parent directories are unchanged: still `WorkFailed`, still
`NotARegularFile` or `DirectoryUnusable`. The parent maps `NoSuchEntry` to `FilesOpError::NotFound`,
which `file_status.rs` already sends as `NOT_FOUND`.

**Why it leaks nothing an account could not already learn.** The status is one byte chosen from a
fixed list; no path, errno or text crosses, so the channel stays closed in the sense of section 2.
The only fact it adds is "the name the requester themselves supplied does not exist in a directory
that the walk has already shown to be inside the account's own home, owned by the account". The
account owns that home and can `stat` any name in it; the answer is the same one a `ls` in an SFTP
session gives. Nothing outside the home is distinguishable: a name that would resolve outside it is
refused by the walk or by `O_NOFOLLOW` before the open, and keeps the old status, so existence of
`/etc/shadow` or of another account's file is still not observable (a symlink is refused whether or
not its target exists). A compromised child could already pick any of the existing statuses, so
being able to choose a seventh grants it no capability: the worst a lying child achieves is that its
own customer's request is answered "not found" instead of "refused".

**What a reviewer must check.** That `fork_and_run` is the only producer of the status and matches
the variant exactly (not a wildcard); that `read_in_home` returns `NotFound` only from the final
`openat`'s `ENOENT` and not from the parent walk (`open_directory_in_home` never returns it); that
the two inverse controls hold (`a_child_whose_entry_is_absent_reports_no_such_entry_and_every_other_failure_stays_work_failed`
in `fork_as_account_for_descriptor_tests.rs`, `a_child_that_found_its_entry_absent_reports_no_such_entry`
in `wait_for_child_tests.rs`).

**What the author could not verify.** Nothing beyond what the tests above and the real-host test
`files_browse_on_a_real_host::a_read_refuses_a_directory_and_reports_a_missing_file_as_not_found`
observe; no mutation scoring has been run for the new branch.

## Mutation-scored table

Filled after the scoring run (task C6); empty until then, which means no mutant has been scored.
