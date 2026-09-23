# Threat note — repairing a hosting account's home group

Required by `rules/security.md` ("Sensitive change escalation"). Covers
`AccountOperations::repair_home_groups` in `agent/crates/ops/src/accounts/account_operations.rs`,
the six-reason refusal predicate in `agent/crates/ops/src/accounts/model/home_group_repair_refusal.rs`,
and the RPC/backend surface added on top of it in this same session
(`RepairAccountHomeGroups` in `proto/agent/v1/accounts.proto`, the agent's
`services::accounts::accounts_service` wiring, and
`backend/src/Maran.Modules/Accounts/Commands/RepairAccountHomeGroups/`).
**This change needs a second reviewer, and that reviewer is OUTSTANDING.**

## This note is LATE — say what that costs, plainly

`rules/security.md` requires this note **first, before the change**. It was not:
the privileged Rust core (`repair_home_groups` and its predicate) was designed,
written, tested and landed in an earlier session, and this note is being written
against code that already exists and already has five passing tests behind it. I
am the same reasoning that produced the code, reading its own working back to
itself and calling it sound. That is exactly the failure mode
`rules/architecture.md` names: *a note written afterwards is a justification
validating a choice already made, which stops the next reader looking.* I have
tried to write this as an argument a skeptical reviewer would need, not as a
recap of what shipped, but I cannot certify that I would have caught a real
mistake in my own design any better here than I did while writing it. The
concrete costs of the ordering, as best I can name them:

- **I did not choose the predicate under threat first; I inherited it and am now
  rationalising it.** The six refusal reasons (path mismatch, missing,
  symlink, not-a-directory, different mount, owner mismatch) read as a
  complete list because they are the six checks the existing code already
  performs — not because a threat model was drawn up first and the checks
  derived from it. A reviewer arriving cold, asked "what must this repair
  refuse to do", might order or enumerate the risks differently and notice a
  seventh case this note, written backward from the code, cannot invent.
- **The ordering of `lstat` before `stat`-following, and `st_dev` before
  ownership, was fixed by the time I sat down to write this.** I can explain
  why the order is safe (below), but I did not derive the order from a blank
  page while adversarial about it — I confirmed that the order already
  written does not have an obvious hole.
- **What a note-first process would have caught that this process might not:**
  a threat note written before code is reviewable before a single line runs on
  a real host, so a reviewer's objection changes the design instead of
  auditing a fait accompli. Here, the earliest a human reviewer sees this is
  after the code, the tests, the proto RPC and the backend surface all
  already exist — the only lever left is "revert" or "patch", not "reshape".

## What this surface is, and why it is privileged

`repair_home_groups` runs as the agent — root — and calls `chgrp` on a directory
path it derives from a row of `/etc/passwd`. Nothing about the caller (an
administrator, through the panel) supplies the path or the group name: the path
is `<home root>/<account>` for an `AccountName` the agent itself parses from the
password database, and the group is read from the distro adapter (`www-data` on
Debian/Ubuntu, `nginx` on the RHEL family) — never from the wire. The attacker
model has one relevant party:

**The customer**, who can influence exactly one thing this operation reads: the
*contents* of their own home directory (they own it, or did until suspension/
deletion). They cannot influence their own passwd row (uid, home path, name) —
that is written once, by `AccountOperations::create`, and never by a customer
action. So the only lever available to a hosting account against this repair is
what sits *at* the path the passwd row names: they could, in principle, delete
their home directory and put something else in its place — a symlink to
`/etc`, a FIFO, a hardlink-adjacent directory swapped in from a different mount
via a container escape, or (if they compromise another account first) another
account's home relocated under their own name via a bind mount. Every one of
those is exactly what the six refusals exist to catch:

- **Symlink** (`HomeGroupRepairRefusal::Symlink`) is the direct answer to "point
  my home at `/etc` and let root `chgrp` it there." The check uses
  `lstat`/`symlink_metadata` and **never follows** the link — this is the one
  fact in the whole design that must never regress, because `chgrp` without
  `--no-dereference` on a followed symlink is precisely how a customer gets
  root to re-group an arbitrary path. The code passes `--no-dereference` on the
  live `chgrp` call *and* refuses the row before that call ever runs when the
  path is a symlink at all — belt and suspenders, and the note says why both
  matter: `--no-dereference` protects the single `chgrp` invocation, but the
  refusal protects every future maintainer who might delete that flag without
  reading this file.
- **NotADirectory** catches a FIFO, device node, or regular file dropped where a
  directory should be — `chgrp` itself would happily re-group any of those, and
  nothing downstream expects a hosting account's "home" to be one.
  covers the same family with a different shape.
- **DifferentMount** (`st_dev` comparison) catches a home that is itself a
  mount point — a customer (or an operator, or a container boundary) could have
  bind-mounted something else at the expected path. `chgrp` does not traverse
  mounts on its own, but the check exists because "this is a plain directory
  under the home root's filesystem" is part of what "this is a home
  `useradd --create-home` made" means, and a repair that skips the check is
  trusting a shape it never verified.
- **OwnerMismatch** (uid comparison against the passwd row) catches a home
  whose current owner is not the account it is supposed to belong to — the
  strongest single signal that whatever is at that path did not originate from
  this agent's own `create`.
- **HomeNotAtExpectedPath** and **HomeMissing** are the "nothing to reason
  about" cases: an account whose home was moved, or never created, is refused
  rather than the repair inventing a path to act on.

**What root already trusted before this repair existed, unchanged by it:** the
agent already runs as root and already calls `chgrp`/`useradd`/`userdel` on
paths derived from the same passwd rows, throughout `ops::accounts`. This
repair adds no new *class* of privilege — it adds one more root-executed
`chgrp` reachable through a new, narrower, more heavily gated path (report-only
first, six refusals, administrator-only HTTP surface, confirmed-count gate on
the write). If the passwd database itself is attacker-controlled, every
existing operation in this crate is already unsafe and this repair is not the
weak link.

## Why grant-then-revoke ordering does not apply here, and what does

`repair_grants` (the reference this mirrors) worried about crash-ordering
between two SQL statements. This repair issues exactly ONE syscall-level
action per account (`chgrp -h <group> <home>`), so there is no ordering
question of that shape. The equivalent concern here is **read-then-act TOCTOU**:
the predicate's six checks (`lstat` the path, read the passwd row, compare
`st_dev` and uid) all happen, and only then does `chgrp` run — a customer with
a live process could in principle swap the directory for a symlink in the
window between the `lstat` and the `chgrp`. Two things bound this:

1. `chgrp --no-dereference` is passed on the live call, so even a
   symlink swapped in during that window is re-grouped as a LINK, not
   followed into whatever it points at — the same protection the up-front
   refusal gives, applied again at the point of actual privilege use, which is
   the standard TOCTOU mitigation for this exact race (check-then-open-through-fd
   is not available for `chgrp` on a bare path the way it is for the file-write
   path in `rules/security.md` item 2, because `chgrp` has no fd-relative
   form in the coreutils this repair shells out to; `--no-dereference` is the
   closest available guarantee and it is unconditional).
2. The blast radius of losing this race is bounded: at worst, a symlink is
   `chgrp`'d instead of a directory, which changes the group of a link that
   itself grants no new access (a `chgrp`'d symlink does not change what
   reading through it can do — the target's own permissions govern that, and
   `--no-dereference` means the target's ownership is never touched). This is
   materially different from a *file write* through a swapped symlink, which is
   why `rules/security.md` item 2's descriptor-walk requirement for writes does
   not transfer verbatim to a `chgrp`.

## Why mode is deliberately untouched (repeating the ops-layer doc comment, because a reviewer must be able to check it against an independent argument, not just trust the comment)

`repair_home_groups` never widens or narrows a home's permission bits. This
matters for the threat model specifically because a repair that ALSO rewrote
mode would be doing two different privileged things under one report/confirm
gate, and an operator confirming "these are the accounts whose GROUP needs
fixing" would not have separately confirmed "and these are the accounts whose
MODE I am about to change" — silently bundling the two is how a repair grows
scope an operator never agreed to. The defect this fixes (README: a site cannot
be served because the group is wrong) is a group defect, not a mode defect;
`useradd --create-home` already leaves `0750`. If an operator has since
narrowed a mode by hand, that is the operator's own choice and this repair does
not override it.

## What a second reviewer must check (the actual checklist, not a restatement of the above)

1. Re-derive the six refusals from a blank page, adversarially, without reading
   this note's framing first — does anything else belong in the predicate?
   Candidates I considered and rejected, stated so the reviewer can disagree:
   an ACL check (rejected — this panel does not manage ACLs on home
   directories anywhere else, so checking for one here and nowhere else is
   inconsistent surface, not extra safety); a check on the home's *contents*
   below the top level (rejected — the repair only ever `chgrp`s the home
   directory ENTRY itself, non-recursively, so a customer's own files below it
   are untouched by this operation regardless of their ownership).
2. Confirm `--no-dereference` is actually present on every `chgrp` invocation
   this code path can reach, including any future one — this is the single
   fact whose regression turns the whole design into an arbitrary-path
   `chgrp` oracle for any account holder.
3. Confirm the group name and the account's home-root constant are read ONLY
   from the distro adapter and `AccountOperations`'s own constant, never from
   the RPC request — the proto request carries only `report_only` (a bool),
   by design (see `proto/agent/v1/accounts.proto`), and this must stay true
   through any future field addition.
4. Confirm the backend's confirmed-count gate (mirroring
   `RepairDatabaseGrantsCommand.ExpectedRepairCount`) actually re-classifies
   the host at write time rather than trusting the caller's number — i.e. that
   a stale report is answered with a 409, not with "wrote whatever the client
   said."
5. Confirm the audit subject is `key=value;key=value` counts only, and that no
   refused row's account name reaches the journal — several refusal reasons
   (`OwnerMismatch`, `DifferentMount`) exist precisely for a case where the
   passwd row's name may not be the operator's own naming for that account
   any more, and a name in the trail that does not match panel state is a
   different kind of hazard than the numbers.
6. Ask whether administrator-only is the right authorisation boundary, same as
   `DatabaseGrantsController`'s reasoning: this is host-wide (every account's
   home at once), not per-tenant, so there is no query filter and no 404-vs-403
   fallback — the endpoint policy IS the authorisation, same as the database
   grants surface it mirrors.

## What I could not verify from here

- Whether the six refusal reasons are actually EXHAUSTIVE against every way a
  directory entry can be made to look like a home on a real Linux filesystem
  (e.g. an overlayfs whiteout, a filesystem that reports `st_dev` inconsistently
  across bind-mount boundaries under some container runtimes). Tested only
  against the `RecordingHost`/fake `SystemHost` in this crate's own tests, never
  against a real host of either distro family.
- Whether a real `nginx`/Apache worker, after this repair runs, can actually
  traverse into a repaired home and serve a site — nothing in this session
  exercised a real web server against a real repaired home. The README's
  claimed defect (site does not serve) is inferred from the group mismatch,
  not reproduced end-to-end here.
- Whether `chgrp`'s `--no-dereference` flag behaves identically across every
  coreutils version this product's supported distro matrix ships. Assumed
  from documentation, not verified per-distro in this session.

## Status

**OUTSTANDING.** No second reviewer has read this. Per `rules/security.md`,
this change may live on the `dev` branch but **must not merge to `main`** until
a second reviewer has gone through the checklist above and the note is updated
to record their sign-off (or their objections and this design's response to
them).

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
