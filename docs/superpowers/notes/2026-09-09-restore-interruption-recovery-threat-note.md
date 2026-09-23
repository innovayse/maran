# Threat note — recovering a restore that was interrupted rather than returned from

Date: 2026-09-09
Surface: the agent's privileged backup area (`ops::backup`), the agent's startup path, and the
installer's `maran-agent.service` unit.
Finding this answers, from the privileged-surface audit: **F-2 (HIGH)** — a restore killed
mid-flight leaves the account with no home and no recovery, and the next daemon start deletes
the rollback dumps.

## Second reviewer: OUTSTANDING

This change is privileged agent behaviour and touches the installer's unit file, so
rules/security.md "Sensitive change escalation" applies: it requires a second human reviewer and
this note. **No second reviewer has read it. The requirement is OUTSTANDING.** No agent session can
be that reviewer, and this note is written by the author of the change, so it closes nothing — it
exists so the reviewer, when they arrive, reads an argument instead of a diff. The branch
`fix/live-findings` therefore MUST NOT merge to `main` while this line stands.

### What the reviewer must check, named

1. **That the recogniser is a fact and not an inference.** `recover_restores` must decide "a swap
   was interrupted" from a document the agent itself wrote, plus `symlink_metadata` on paths the
   agent recomputed from validated types — never from parsing an account name out of a directory
   name. Confirm no `split`, `strip_prefix`, `rsplit_once` or equivalent runs over a directory
   entry's name in the deciding path.
2. **That the marker cannot be forged.** `/home/.maran-restore` is mode `0711` root-owned and
   `/home` is `0755` root-owned, so no unprivileged uid can create an entry in either. Confirm
   `recover_restores` re-asserts that root owns the staging root and that it is not
   group- or other-writable *before* it reads a single marker, and refuses the whole reconciliation
   otherwise. A forged marker is a root-driven `rename` into `/home/<name the forger chose>`.
3. **That "finish forward" is the right promise.** Section "The decision" below argues it. The
   reviewer is being asked to agree with a product decision, not only a code one.
4. **That removing `ExecStartPre=` does not regress the reason it existed.** The unit's comment
   argued disk hygiene and privacy for a tree holding a full copy of a customer's databases. The
   agent must now do at least as much, on every start, before it serves.
5. **What I could not verify.** I have not observed a real power cut. Everything below about
   durability across a power cut rests on `fsync` of the marker file and of its parent directory
   before the first `rename`, and on ext4/xfs journalling the rename itself — reasoned from the
   filesystems' documented semantics, not measured. What IS measured, on both polygon families, is
   the `SIGKILL` case: a real process killed with signal 9 inside the swap window, and the home
   recovered by a real reconciliation afterwards. Nor have I verified the unit on a booted host —
   the unit file's own "Not yet confirmed on a real boot" block still stands and now has one more
   line in it.

## The surface, and what could go wrong with it

The agent gains **no new rpc, no new port, no new outbound call and no new capability.** It gains
one thing it does at startup, before the socket is bound: it reads its own restore staging root and
its own bulk scratch, and it moves and deletes directories under both.

That is nevertheless the highest-risk shape in this repository, because the *action* it takes is
`rename(<something>, /home/<account>)` as root. Three ways that goes wrong:

- **It picks the wrong account.** A rename into `/home/victim` of a tree an attacker controls is
  the attacker owning the victim's home — their `~/.ssh/authorized_keys`, their site's PHP. This is
  the reason the recogniser may not parse a directory name: the audit's own suggested fix
  ("for every `<account>.previous.<id>` whose `/home/<account>` is absent, rename it back") reads
  the account name out of a string that a future refactor, a locale, or a name containing a dot
  could shift. The name is *discovery*; the decision is a validated document.
- **It acts on something it did not write.** Covered by check 2 above. `/home` is root-owned `0755`
  and the staging root root-owned `0711`, so the only writer is root — but the reconciliation
  asserts that rather than assuming it, because a directory's mode is something an operator or an
  older installer can change, and the assertion is one `stat` on one directory.
- **It runs against a live restore.** Covered under "Concurrency" below.

What it deliberately does NOT do: it never creates an account, never touches `/etc`, never reads a
customer's file contents, never logs a path from inside a home, and never runs a process. It calls
`rename`, `chown`, `chmod`, `remove_dir_all` and `symlink_metadata`, and nothing else.

## The defect being fixed, restated in one paragraph

`swap_home` parks `/home/<account>` at `/home/.maran-restore/<account>.previous.<id>` and then
renames the staging tree into place. Every recovery in `restore_backup.rs` is *in-process*: a
`SIGKILL` after `TimeoutStopSec`, an OOM kill or a power cut in the window between those two
renames executes none of it, and nothing reconciled `RESTORE_STAGING_ROOT` at startup. The customer's
home is on the disk under a name only the agent's source knows, `/home/<account>` is absent, and the
panel's retry of the same restore fails `ENOENT` at the first rename → `StagingUnusable`, forever.
Separately, `ExecStartPre=-/bin/rm -rf /var/lib/maran-scratch` destroyed the per-database rollback
dumps — the only copy of the pre-restore database state — on the very restart an operator performs
to recover.

## The decision: finish forward, not roll back

They are different promises and the difference is the customer's data, so it is argued rather than
asserted.

**The marker is written after step 6 has fully succeeded and immediately before the first rename.**
Its existence is therefore a fact with a strong consequence: *every database this restore was asked
to replace has already been replaced, and the only work left is the two renames and the
ownership.* That is what settles it.

- **Rolling back** (put the parked home back, discard the staging tree) leaves the account with
  its **old home** and its **new databases**. Nobody asked for that pair. An application whose
  schema came from the backup and whose code did not is a site that errors on every request, and
  the customer is told the restore failed while being handed a state that is neither before nor
  after.
- **Finishing forward** (complete the second rename, re-apply owner/group/mode, remove the parked
  tree) produces **exactly the state a successful restore produces**. It is the operation the
  customer asked for and that the panel had already committed to at the first `DROP DATABASE`.

The usual objection to finishing forward — "you cannot know the staging tree is complete" — does
not apply here, and that is precisely why the marker is written where it is. Nothing writes into the
staging tree after `extract_home_as_account` returns, and the marker is written after `prepare`
returned `Ok`, i.e. after that extraction succeeded and was verified. A marker's existence means the
staging tree is a finished home.

Rolling back is kept as the **fallback within** the finish-forward path: if `rename(staging, home)`
fails during reconciliation, the reconciler immediately renames the parked tree back, so the
customer has a working home either way. That is the same preference `swap_home` already encodes
in-process, and it is the only place the reconciler rolls anything back.

The one thing finishing forward cannot do is tell the panel. The rpc that asked for the restore died
with the process; the panel sees a timeout and its own row stays in whatever state it was left in.
That is out of this change's scope (it is the panel's, and eight peers are in it), and it is named
here so the reviewer does not read the recovery as closing it. What the reconciliation does instead
is **log, at `warn`, one line per recovery naming the account and what was completed**, so an
operator reconciling the panel's row against the host has the host's answer.

## Recognising an interrupted swap as a fact

The deciding input is `/home/.maran-restore/<account>.<id>.swap` — a JSON document the agent writes
itself, holding the account name, the backup id, the owner uid, the home gid and the home mode.
Nothing else. In particular it holds **no paths**: the reconciler re-derives `home`, `previous` and
`staging` from `AgentPaths` using the account and id *after* they have been through
`AccountName::parse` and `BackupId::parse`. A path the reconciler acts on is therefore built by the
agent out of two validated tokens, and there is no string in the file that a rename could be pointed
at.

Discovery is by name (`*.swap` under the staging root); the **decision** is the document plus four
`symlink_metadata` calls. `symlink_metadata` and not `metadata`, and not `Path::exists`: a symlink
at any of the three names must be seen as a symlink and refused, not followed.

Durability: the marker is written to a temporary file in the staging root, `fsync`ed, renamed into
place, and the staging root directory itself `fsync`ed — all before the first `rename` of the swap.
Without the directory `fsync` a power cut can lose the marker while keeping the rename, which is the
unrecoverable state this whole change exists to remove. It is removed after `finalise` succeeds.

## Every intermediate state, and what reconciliation does with each

`H` = `/home/<account>`, `P` = the parked previous tree, `S` = the staging tree, `M` = the marker.
"dir" means `symlink_metadata` says a directory.

| # | M | S | P | H | Reached by | Reconciliation |
|---|---|---|---|---|---|---|
| 1 | yes | dir | absent | dir | killed after the marker, before rename #1; or the in-process reversal succeeded and the marker removal did not | Remove `S`, remove `M`. **The live home is not touched.** State 1 and "reversed after a failed rename #2" are indistinguishable and want the same answer. |
| 2 | yes | dir | dir | absent | **the window** — killed between the two renames | **Finish forward:** `rename(S,H)`; then `chown`/`chmod` `H` from the marker; then remove `P`; then remove `M`. If `rename(S,H)` fails: `rename(P,H)`, `chown`/`chmod`, remove `S`, remove `M`. |
| 3 | yes | absent | dir | dir | killed after rename #2, before or during `finalise` | Complete `finalise`: `chown`/`chmod` `H` from the marker, remove `P`, remove `M`. Idempotent — it does the same thing whether or not `finalise` had already got there. |
| 4 | yes | absent | absent | dir | killed between the end of `finalise` and the marker's removal | Remove `M`. Nothing else. **This is the "do not recover healthy state" case** and it is asserted by a test. |
| 5 | yes | dir | dir | dir | unreachable from this code — `rename(H,P)` makes `H` absent atomically. Reached only if something else re-created `H` (an operator, a `useradd`) | **Refuse to touch `H`.** Remove `S` (litter), **leave `P`** (it is a customer's home and only a human can judge which one is wanted), remove `M`, log `error` naming `P`'s full path. |
| 6 | yes | absent | absent | absent | both trees lost outside this code | Remove `M`, log `error` naming the account. Nothing to recover; loud rather than silent. |
| 7 | yes | any of the three is a symlink or a non-directory | | | hostile or corrupt | **Touch nothing at all.** Log `error`. A symlink here is an attack shape and the reconciler is root. |
| 8 | no | dir/dir present in the staging root | | | a kill under a build older than this change, or a kill before the marker was durable | **Leave every such entry in place**, log `warn` naming each path. Without a marker the account is only *inferable* from the name, and a wrong rename costs a home while litter costs disk. |
| 9 | unreadable / unparseable / `AccountName::parse` or `BackupId::parse` refuses | | | | corruption, or a file that is not a marker | **Refuse, act on nothing, leave everything**, log `error`. Never act on a half-understood marker. |

Two renames cannot be made atomic with respect to each other, and this change does not pretend
otherwise. What it does is make the window *recoverable*: every state the window can be observed in
is in the table, and each has an answer that is a decision rather than a guess.

## Concurrency

`restore_backup` already takes a per-account lock (`take_account_lock`, wait-free, answering
`AlreadyRunning`). Two things make the reconciliation safe against it:

1. **It runs before the socket is bound.** `server::serve` calls it ahead of `UnixListener::bind`,
   so no rpc has been accepted and no restore can have started. This is the load-bearing guarantee,
   and it is a matter of *position* in one function — which is why it is stated in that function's
   comment and in the reconciler's own doc.
2. **It takes the same lock anyway, per account, wait-free.** An account whose lock is already held
   is skipped with a `warn` line rather than waited for. That makes the function correct if it is
   ever called from somewhere else, and it means recovery structurally cannot run against a live
   operation for the same account. Note the honest limit: a lock table is per **process**, so it
   says nothing about a *different* agent process — which is the same limit `restore_backup` has
   always had, and which the unit's `Type=simple` single-instance service is what actually prevents.

The scratch reaping has no per-account key to lock on (a scratch directory is named by backup id,
and the reaper does not parse ids out of names either — it reaps whatever is there). It is therefore
protected by ordering alone: it MUST run before the socket is bound, its doc comment says so, and
its only caller is the line in `serve` that is before the bind.

## The scratch directory

`ExecStartPre=-/bin/rm -rf /var/lib/maran-scratch` is **removed from the unit** and replaced by the
agent's own reaping, done in the same pre-bind step. Three candidate fixes were considered:

- *Narrow the `rm` to the reproducible subtree.* Impossible: the paths need a glob
  (`/var/lib/maran-scratch/backup/*/databases`), systemd runs `ExecStartPre=` with no shell, and
  rules/security.md item 3 forbids introducing one. A `sh -c` in the unit is not on the table.
- *Move the rollback dumps somewhere the cleanup cannot reach.* Rejected: it splits one restore's
  material across two roots, and the `require_scratch_room` / `scratch_dump_ceiling` arithmetic
  that bounds a restore's peak is written against **one** filesystem. Two roots means two free-space
  questions and an operation that can pass both and still fill a disk.
- *Make the cleanup selective, in the agent.* Chosen. The agent is the process that knows which
  subtree is reproducible and which is the only copy.

What the reaper does on every start, before the socket is bound:

- Anything directly under `/var/lib/maran-scratch` that is not the `backup` directory: removed.
- For each `backup/<id>/`: everything inside it **except** `rollback/` is removed. That is the
  archive's extracted dumps, which are reproducible from the artifact — the artifact is still on
  disk, its SHA-256 is recorded, and a restore re-extracts them.
- A `backup/<id>/` with no `rollback/` inside it: removed entirely.
- Every surviving `rollback/` set is logged at `warn`, by path and by the dump file names in it, so
  an operator sees which databases were left half-replaced and where their pre-restore state is.
  That is the audit's own condition for deleting anything.

**What bounds the growth** — the question the `rm -rf` existed to answer:

- The large half is bounded to *one start*: the extracted archive dumps, which are the bulk of a
  scratch, are removed unconditionally on every start.
- A successful or a cleanly-failed restore removes its whole scratch itself (`restore_in` does two
  unconditional `remove_dir_all`s), so a `rollback/` set can only survive to a startup at all if
  that run was killed.
- Surviving `rollback/` sets are capped at **`RETAINED_ROLLBACK_SETS = 4`**, newest first by
  directory mtime; older ones are removed. The peak residency is therefore four accounts'
  pre-restore database estates, and it takes four separate killed restores to reach it.

The number is a judgement and it is the weakest part of this change. Four, because a kill takes down
every in-flight restore at once and more than a handful of concurrent restores on one host is not a
thing this product does; and because a recovery is an operator process that may span several
restarts, so "keep it for exactly one restart" would delete the dumps under an operator who
restarted twice. **An age bound was the alternative** ("delete a rollback set older than 14 days")
and was not chosen because it needs the ambient clock, which rules/testing.md keeps out of logic.
A count bound sorted by mtime needs no `now()`. The residual, stated: on a host where four killed
restores happened and nobody ever looked, four rollback sets sit on the disk indefinitely. That is
disk, held deliberately, in a root-only `0700` tree — and it is an **owner decision** whether the
cap or the age bound is wanted.

## What this change does not fix

- **The panel's row.** Named above. The host recovers; the panel is not told.
- **A partly-replaced database estate.** If the kill lands inside step 6 rather than step 7, some
  databases are new and some are old. This change makes the rollback dumps *survive*, and logs
  where they are; it does not load them back. Doing that automatically means a root daemon replaying
  a database load at startup with no operator present, against a MariaDB that may not be up yet,
  and against an application that may have written to the new schema since. That is deliberately
  left to the operator, with the material preserved and the log line naming it. **This is the second
  owner decision in this note.**
- **The drain budget.** `DRAIN_BUDGET` stays 30s and `TimeoutStopSec=45` stays 45s. No budget an
  operator tolerates covers a restore, and the fix for a restore is recovery, not waiting. What
  changes is `drain_deadline.rs`'s claim that the restore window "is not made safe by any shutdown
  path" — the home half of it now is, and the comment says so.

## Verification actually performed

- `agent/crates/agent/tests/restore_recovery_on_a_real_host.rs`, in the polygon images, as root:
  a real account, a real database, a real backup, a real restore, and a real `SIGKILL` (signal 9)
  delivered from inside the swap window by the restore's own progress sink — a seam that already
  existed. The case asserts the window was actually entered before it asserts anything about
  recovery, so a kill that missed fails the case instead of passing it.
- Each case was proved able to fail by reverting the fix and quoting the named failure; the
  quotes were kept in a working report only, not committed.

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
