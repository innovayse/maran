# Threat note — the account error seam, `CommandOutcome`'s `Debug`, and the path-control claim

Written **before** the change, as `rules/security.md` ("Sensitive change escalation") requires.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from this session. The change may
land on `fix/live-findings`; it MUST NOT merge to `main` until a second human reviewer has read this
note and the diff.

Covers the MEDIUM findings F-3, F-4, F-6 and F-7 of `.superpowers/sdd/privs-audit-report.md`.

## Which part of this is a privileged surface, and which is not

- **F-6 touches `agent/crates/agent-core/src/privs/mod.rs`** — a module doc comment, no code. That
  file is inside the agent's `privs` module, which the escalation rule names by name, so this note
  is owed for it regardless of the change being prose. The change adds no syscall, no `unsafe`, no
  flag and no branch; it corrects a sentence that claims a pairing the code does not have.
- **F-3, F-4 and F-7 are not privileged surfaces.** They are an error enum's shape
  (`ops::accounts`), a `Debug` implementation (`agent-core::command_outcome`), and the deletion of a
  duplicated `/home` literal. None of them changes who runs as whom, what is opened, or what is
  spawned. They are in this note because they ship in the same change and a reviewer should read
  them together, not because the rule demands it for them.

## What an attacker could do with this surface, and why it is safe after the change

### F-3 — `AccountError::CommandFailed` loses its `stderr` field

Today the variant carries the failing tool's standard error, and
`services/accounts/account_status.rs` copies it into the wire error's `tool_output`, which the panel
uid reads. The tools this area runs are `useradd`, `usermod`, `userdel`, `setquota`,
`getent shadow`, `passwd -S` and `crontab`. **None of them writes a credential to standard error
today** — `getent shadow` writes the entry to standard *output*, which never enters this variant —
so there is no leak now and this note does not claim one.

What the change buys: this is the one `ops` area besides `db` whose tools are *handed or hand back
credential material at all*. `db` was deliberately given an error enum with no field a message can
occupy, because the MySQL client quotes the statement it refused and that statement can be
`CREATE USER … IDENTIFIED BY '…'`. The account area sits on the same side of that line and had the
opposite shape. After the change the wire carries the program and its exit status; the tool's own
words are written once to the agent's `tracing` output.

Residual risk a reviewer must check: the diagnostic now lives in the agent's log. The agent runs as
root and journald's default access is not the panel uid's, so the split puts the tool's text on the
side that already has root and takes it off the side that does not. **I could not verify journald's
group configuration on a real installed host from this session** — if `panel` is in `systemd-journal`
on the installed system, the split buys less than it claims, and the installer, not this change, is
where that would be fixed.

What an operator loses: the tool's sentence. `useradd` and `userdel` carry documented, distinct
exit statuses; `setquota` and `crontab` do not, so for those the operator must read the agent's log
rather than the panel's error. That cost is accepted, and it is the same cost `DbError` accepted.

### F-4 — `CommandOutcome` gets a hand-written `Debug`

`CommandOutcome` derives `Debug` and its `stdout` holds, at one call site, an account's full shadow
entry including the password hash; at another, a customer's entire crontab. Nothing formats it
today. The change makes the type print `status` and the **lengths** of `stdout`/`stderr` instead of
their contents, so no `{:?}` anywhere — present or future, in this workspace or in a backtrace-style
diagnostic — can print either.

`SecretString` was considered and rejected for this: its whole guarantee is that `expose` is the
single greppable reader, and `CommandOutcome.stdout` is read by roughly a dozen parsers, so wrapping
it would replace one guarantee with a dozen `expose` calls and destroy the property the type exists
for. The redacting `Debug` costs nothing at the read sites and covers every one of them.

Residual risk: a test's `assert_eq!` failure on a `CommandOutcome` now prints lengths, not contents,
so a failing comparison is less informative. Accepted; the values are still available to a test that
compares the fields directly.

### F-6 — the path-control claim is corrected to name the mechanism that actually contains

No behaviour changes. Three doc comments in `agent-core` claim `resolve_in_home` is the containment
for customer paths. Measured against the tree, it is the containment for **reads that must locate an
existing entry** (`files::delete_entry`, `sites::resolved_site_paths`, `sites::tail_site_log`'s
one-time resolution) and for **nothing that is written**: every write descends by descriptor through
`ops::files::open_parent_directory` with `O_NOFOLLOW` at every level, which is strictly stronger and
already says so in `ops/src/files/`. The claim is moved onto the mechanism that does the work.

The security relevance of a prose change is that the checklist in `rules/security.md` item 2 names
`resolve_in_home` as *the* path control, so a reviewer running it verbatim gets a misleading yes on
an operation that correctly does not call it — and, worse, a future operation could adopt
`resolve_in_home`, reopen by path afterwards (the race that function's own doc warns about) and pass
the checklist. **A reviewer must decide whether `rules/security.md` item 2 is reworded**; this
change does not touch `rules/`, because another session owns that directory in this branch's work
split. Until it is reworded the checklist remains misleading, and that is the outstanding half of
this finding.

### F-7 — one spelling of `/home`

`ops::accounts` kept a private `const HOME_ROOT: &str = "/home"` while
`AgentPaths::ACCOUNT_HOME_ROOT` states the same fact, and `AgentPaths`' own doc says keeping it one
constant is what makes "the path this operation built" and "the path the check approved" the same
path. They agree today; the risk is drift, and drift here means the containment check approves a
root the operation did not build under. The local constant is deleted.

Residual, stated rather than hidden: `AgentPaths::RESTORE_STAGING_ROOT` spells `/home` again as part
of `"/home/.maran-restore"`. It cannot be composed from `ACCOUNT_HOME_ROOT` in a `const &'static
str` without unstable const string concatenation, it sits sixteen lines below its source of truth in
the same inventory, and this change does not contort the type to remove it. A reviewer should say
whether that is acceptable or whether the constant should become a function.

## What the author could not verify

- Whether the `panel` uid can read the agent's journald output on an installed host (see F-3).
- Any of this against a real root host: the changes were verified against the workspace's own test
  suites and the polygon suites were not re-run, because none of these four changes alters a
  syscall, a spawn, a path or a privilege.
- That `rules/security.md` item 2's wording will be corrected — that file is out of this session's
  write scope and the correction is recorded here as the outstanding half of F-6.

---

## Correction, 2026-09-09 — F-6's outstanding half has since been closed

Added by a verification pass (`.superpowers/sdd/threat-note-verification.md`). This note says a
reviewer "must decide whether `rules/security.md` item 2 is reworded" and that "until it is
reworded the checklist remains misleading". **It has been reworded.** Item 2 now reads, in part:
`resolve_in_home` "is a *locating* aid used by two read paths, and it contains nothing that is
written: an earlier version of this rule named it as the containment, which is wrong and was worth
correcting". The outstanding half of F-6 is therefore discharged; what remains outstanding for
this note is the second reviewer and the journald question under F-3.
