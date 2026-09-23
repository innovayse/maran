# Threat note — ending a suspended account's open transfer sessions

**Surface:** `ops::logins::end_account_sessions`, a new crate-private operation called by
`ops::logins::set_account_logins_locked` when it is asked to LOCK. It gives the agent — the only
root process on the server — the ability to **send a fatal signal to every process running as a
hosting account's uid**. That is a new privileged, destructive capability in a root daemon, so
rules/security.md "Sensitive change escalation" applies: a second reviewer and this note.

**Status: written BEFORE the change.** Nothing in `agent/crates/ops`, `agent/crates/distro` or
`agent/crates/agent/tests` had been edited when this file was created. The question it is written
forward from is *what can an attacker, or a mistake, do with a kill-by-uid surface inside a root
daemon* — not *why is the code I already wrote alright*
(rules/architecture.md: a justification written after the choice validates the choice).

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from an agent session and no agent
can be one. Per rules/security.md this change may live on the branch `fix/live-findings` and **MUST
NOT merge to `main`** until a second reviewer has read it. The owner is to be told at hand-off that
this surface carries an outstanding review.

## Why the change is being made at all

`docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`, "Correction,
2026-09-11 (second)", measured and wrote down the gap: **suspension refuses a new login and does
nothing to a session already open**, on SFTP and on FTPS alike. The marker a suspension writes is
consulted at authentication only, and neither daemon re-authorises a session it has admitted. FTPS
idles out after ten minutes (`idle_session_timeout=600`, a value this repository renders); SFTP has
no bound at all, and that zero is OpenSSH's own compiled-in default rather than anything this
product states. A session that is *transferring* is bounded by nothing on either protocol.

That correction costed four options and recommended option 1 (say so) now, option 3 (cull) when
suspension gained an abuse story. **The owner has since asked for option 3.** This note is the
escalation that option 3's own text demanded of whoever implemented it, and its reviewer checks
below are written against the two conditions the earlier note's reviewer list named: that the cull
cannot reach a uid other than the account's, and that a half-written file left by a cut upload is
accounted for.

## What the change does

One new crate-private operation and one call site.

1. `end_account_sessions(host, distro, account)`:
   - reads the host's local password database through the existing `LoginsHost::read_passwd` seam
     and takes the **account's own row** — the row whose name is exactly the validated
     `AccountName`. Its uid is the only uid this operation will ever name.
   - **refuses uid 0**, with its own typed error, before anything is spawned.
   - **refuses a row whose home is not `<ACCOUNT_HOME_ROOT>/<account>`**, with its own typed error.
     This is the check that says "this passwd row is a hosting account this agent created", and it
     is the same kind of check `account_logins` makes when it decides a row is one of the account's
     logins by its *home* and never by its *name*.
   - spawns `pkill --signal KILL --count --uid <uid>` — an argv array, an absolute path from the
     `DistroAdapter`, `<uid>` a `u32` rendered from that passwd row.
   - reads `pkill`'s own answer: status 0 means it matched and signalled (`--count` prints how
     many), status 1 means nothing was running as that uid, and anything else is a failure that
     becomes a typed error.
2. `set_account_logins_locked_under_lock` calls it **after** the logins are locked and **only** when
   `locked == true`, under the hosting account's existing lock, and propagates its error.

No new lock, no new listening port, no new daemon, no outbound call, no proto change, no shell.

## What an attacker could do with this surface, and why it is safe now

### 1. Can a request make the agent kill a process outside the account?

No, and the confinement is the kernel's rather than this code's. `pkill --uid <n>` matches on the
**real** uid; a process not running as `<n>` is not a candidate, whatever it is called and whoever
started it. So the whole question reduces to *where does `<n>` come from*, and `<n>` comes from one
place: the passwd row whose name equals a validated `AccountName` that the service layer re-parsed
from the request. Nothing in the request reaches the argv, and no name is parsed to derive the uid —
the failure mode that produced a cross-tenant destructive delete on this branch, and the one that
made a jailed row on a foreign uid look like the account's own, were both name-parsing.

Two guards stand in front of it and **they are independent, not a pair of half-checks**:

- **uid 0 is refused.** Without it, a hosting account somehow named `root` — the account-name regex
  `^[a-z][a-z0-9_]{2,29}$` accepts `root`, `mail`, `news` and `daemon` — would resolve uid 0 and
  `pkill --signal KILL --uid 0` would kill `systemd`, `sshd`, the database and the panel. This is
  the guard whose absence is catastrophic rather than merely wrong.
- **the home must be `/home/<account>`.** Without it, an account colliding with a pre-existing
  *system* user (`mail` at uid 8) would cull that system user's daemons. The home is what this agent
  itself creates for a hosting account, so a row that does not have it is not an account this agent
  provisioned.

They overlap for `root` on an ordinary host (root's home is `/root`, so the home guard refuses it
too), and they do **not** mask each other: a host whose root home had been moved to `/home/root` is
refused by the uid guard alone, and `mail` at uid 8 is refused by the home guard alone. Each is
mutated separately in this change's mutation run and each dies to a test by name; rules/testing.md
requires exactly that of two checks that look like one.

### 2. Can this become "run this for me" (rules/security.md item 12)?

No, and the argument is structural rather than a promise.

- The program is `distro.pkill_binary()`, a `&'static str` from the adapter. `ops` names no platform
  literal — `scripts/lib/check-structure.sh` (`maran structure`) rejects one, and an unattributed
  hunk naming `/usr/bin/pkill` directly has already appeared in this tree once.
- The spawn is `LoginsHost::run`, which is `agent-core::utils::spawn_argv`: `Command::new(program)`
  plus `args`, reaching `execve` as separate `char *`. There is **no command line** for anything to
  re-parse, no `sh -c`, no `format!` into a command.
- Every argument but the uid is a `const` in the source. The uid is a `u32` printed with `Display`,
  so it cannot contain a space, a quote, a newline or a metacharacter — not because it is escaped
  but because the type has no room for one.
- No request field reaches the argv. `SetAccountLoginsLocked` carries an account name and a boolean;
  the name selects a row, and the row's numeric uid is what is passed.

### 3. Can the cull be aimed by a second actor while it runs?

The hosting account's existing lock (`ops::accounts::account_lock`) is held across the enumeration,
the locking and the cull, which is what makes them one critical section: without it a login created
between the enumeration and the cull is a credential this suspension never saw, and — the reason
that matters *here* specifically — a `fork_as_account` child of some other operation of this agent
runs **as the account's uid** and would be killed mid-write by our own `pkill`. The lock is the
thing that says no such child of ours exists.

It is the same lock, taken at the same entry, and **no sixth lock is added**
(rules/rust.md "What this agent serialises"). This operation takes that one and nothing else; it
reaches neither `safe_write` nor `cron` nor the firewall, so it adds no edge to the wait-for graph,
and the lock's load-bearing property — it never waits, it refuses — is untouched.

What the lock cannot do is exclude anything outside this process: an operator's own `useradd
--non-unique`, a second agent binary, a `crontab -e`. Every lock in this agent is process-local and
this is no exception.

### 4. What happens if the cull fails?

**The suspension fails.** `end_account_sessions` returns `LoginsError::SessionCullFailed { code }`
(or `SessionCullRefused`), `set_account_logins_locked` propagates it, `logins_status.rs` maps it to
`ERROR_CODE_SYSTEM_FAILURE`, and the panel's `AccountSuspendingHandler` throws — which is how that
handler already aborts a suspension the agent refused. So the panel does not record a suspension
whose cull it cannot account for.

This is deliberate and it is the whole point of the change: a suspension that reported success while
the cull silently failed would be **worse than today's honest gap**, because today's gap is written
down and an operator can act on it, whereas a green suspension with a live session behind it teaches
the operator to stop looking (rules/architecture.md).

The state left behind by such a failure is safe to retry and is not half-done in any direction that
matters: the logins are **already locked** when the cull runs, so a failed cull leaves an account
that refuses new logins and may still hold an open session — exactly today's shipped behaviour, and
never a state in which access was *restored*. Re-issuing the suspension is idempotent and is the fix.

### 5. What does a suspended customer's interrupted transfer leave behind?

This is the cost, and it is a real one. In the operator's words, and the sentence is repeated in
`docker/README.md` beside the tests that measure it:

> Suspending an account now ends its open SFTP and FTPS sessions as well as refusing new ones. An
> upload that was in flight is cut at whatever byte it had reached, so the customer's home can be
> left holding a **partial file** — a truncated archive, half a database dump, a half-written
> `.zip` that will not open. Nothing deletes it and nothing marks it: it is a file of the customer's
> that is shorter than it should be. If you are suspending for non-payment rather than for abuse,
> that is the price of the access stopping immediately, and it is paid in the customer's data.

Two things follow that a reviewer should weigh rather than accept:

- **Nothing cleans it up, and deliberately.** The agent cannot tell a truncated upload from a file
  the customer meant to write; deleting a customer's file on a suspension would be a destructive act
  on data the panel does not own, which is a larger hazard than the partial file.
- **SIGKILL rather than SIGTERM, and that makes the partial file certain rather than likely.** The
  reasoning is in the ruling below; a reviewer who disagrees is disagreeing with a stated choice.

### 6. Idle sessions as well as transferring ones — the ruling

`pkill --uid` does not distinguish them, and this change does not try to. Argued rather than
defaulted:

- The idle FTPS session is already bounded at ten minutes, so culling it buys only those ten
  minutes. **Those ten minutes are exactly what an abuse suspension is for.** An operator
  suspending a compromised account is not content with "the idle session will be gone within ten
  minutes", and the SFTP idle session is bounded by nothing at all, so on that protocol the idle
  case is not covered by anything else.
- Telling idle from transferring is not observable to us without classifying processes by **name or
  state**, and a classifier over process names is the shape that has already produced two defects in
  this area (an FTPS login invisible to a jail-name scan; a foreign uid taken for the account's
  own). A uid predicate is enforced by the kernel and cannot be fooled; "is this session busy" is a
  guess this code would be believed about.
- So both are culled. The FTPS `idle_session_timeout=600` is **not removed** and is not redundant:
  it still bounds the idle sessions of accounts that are *not* suspended, which is every account
  this cull never touches.

### 7. New surface, new dependency

No listening port, no daemon, no outbound call (rules/security.md item 10). One new tool on the
allow-list, `pkill` — `procps` on the Debian family, `procps-ng` on the RHEL family. Both polygon
images already carry it at `/usr/bin/pkill` (measured: `procps-ng 4.0.4` on Ubuntu 24.04, `3.3.17`
on AlmaLinux 9, `--count` supported by both), and
`agent/crates/agent/tests/binary_paths_on_a_real_host.rs` now asserts the declared path names an
executable on each family, so an image or a host without the package fails by name rather than at
exec time on a customer's server.

**A reviewer must check the installer names that package.** `installer/` is outside this change's
writable scope and was not edited; the requirement is reported as prose to the owner. On both
families the package is ordinarily present (`procps` is priority *important* on Debian, `procps-ng`
is in RHEL's `@core`), which is precisely why a missing declaration would not be noticed until a
minimal host.

## What this change does NOT close

- **A process the account's uid does not own.** A suspended customer's work running as somebody
  else's uid — a system service they persuaded an operator to install, anything under `www-data`
  from a PHP request — is not culled. `php-fpm`'s pool children DO run as the account and are
  culled; the master is root and is not, so it respawns them, which is correct: the suspended site
  is served by the suspended vhost and the pool has nothing to serve.
- **The window between the lock and the cull.** It is one `pkill` spawn wide, and a login that
  authenticates inside it is impossible for the ordinary path (the passwords are already locked) but
  not for a credential this agent does not manage — the `unmanaged` count exists for exactly those,
  and this agent locks none of them. A session opened through one of them **is** culled if it runs
  as the account's uid, which is the one place the unmanaged entries are covered rather than merely
  counted.
- **Sessions on a host where `pkill` cannot see the process.** A process in another PID namespace is
  invisible to `pkill`; the product runs no container story for customer sessions, so this is stated
  rather than handled.
- **The panel's own wording.** Nothing here makes the suspension screen tell the operator that a
  transfer will be cut and a partial file may be left. That sentence is written in this note and in
  `docker/README.md`; putting it in front of the operator is a product change and is the owner's.
- **`AccountBusy`, still.** A suspension issued while a backup of the same account runs is refused
  for the length of that backup, and the panel must retry. Unchanged by this note, and unresolved.

## What the author could not verify

- **Any host outside the two polygon families.** Both guards, `pkill`'s statuses, its `--count`
  output and the end-to-end session cull are measured on the Ubuntu 24.04 and AlmaLinux 9 polygon
  images, which run the installer's own step files but are not an installed host.
- **That `pkill` is present on a minimal supported host.** Measured in the images only; the
  installer declaration is owed and is reported as prose.
- **What the panel shows afterwards.** ~~The count of processes signalled is logged by the agent and
  is NOT on the wire.~~ **Superseded 2026-09-12 — see the correction below.**
- **Whether SIGKILL is the signal the owner wants.** A TERM-then-KILL pair would give an FTPS
  session a chance to close its data connection cleanly, at the cost of a wait in a root daemon and
  of an unreliable second observation (a reaped-but-not-yet-collected process still matches
  `pkill`). The choice made is one KILL and no wait, and it is the choice that makes the partial
  file in §5 certain.

## What a second reviewer must check here, specifically

1. **The uid.** Read `end_account_sessions` and confirm the uid is taken from the passwd row whose
   name **equals** the account, that no name is parsed, and that nothing from the request reaches
   the argv. Then confirm both guards — uid 0 and the home — and satisfy yourself that they are two
   checks rather than one, using the two cases named in §1.
2. **The binary.** Confirm `pkill_binary()` is asked of the adapter at the call site and that no
   platform literal appears in `ops` (`maran structure` proves the second half mechanically).
3. **The failure.** Agree, or do not, that a failed cull must fail the whole suspension, and that
   the retry is safe because the logins are locked before the cull runs.
4. **The partial file.** Rule on §5: is a truncated upload in the customer's home an acceptable
   price for immediate access loss, is the sentence the right one, and does the suspension screen
   owe it to the operator?
5. **Idle versus transferring.** Rule on §6, and on whether `idle_session_timeout=600` should stay
   (it does, and this note says why).
6. **The signal.** Rule on SIGKILL with no grace period (§"could not verify").
7. **The installer.** Confirm `procps` / `procps-ng` is named by `installer/` before this ships to a
   minimal host, since this change could not edit that tree.
8. **Run the suite on a real installed host, not a polygon.** The cull is measured in containers,
   and `pkill` in a container sees only that container's processes. On a real host it sees the whole
   machine, which is a *stronger* test of the two guards than anything the author could run.

## Correction, 2026-09-12

This note said the signalled count was logged by the agent and **not** on the wire, and that an
operator could learn it from the agent's log and nowhere else. That was true when the note was
written and is now false: a follow-up lane added `optional uint32 sessions_ended = 1` to
`SetAccountLoginsLockedOk`, carried it out of the ops layer on its own outcome type, and the
suspension attestation now renders four distinguishable readings — a count, "the host found none of
this account's to end", "carried no count of them", and "no module reported having done so". The
"neither" outcome renders no attestation at all, because the subscriber throws, the account stays
active and the task fails: there is nothing to attest to.

The field is **optional** deliberately. A bare `uint32` would make an agent predating the field send
the same zero as an agent that measured zero, which is the defect this branch already paid for once
with `unmanaged_logins`, where "measured" had to be inferred from an unrelated field. Absence is now
a fact of the wire rather than an inference. Note also that `pkill` exits zero only when it matched
at least one process, so a zero from the ops layer means "nothing matched" and never "no answer" —
nothing downstream may merge those two.

**A seventh item for the reviewer, which the follow-up lane could not settle:** whether a task's
report lines are ever read by the account's owner rather than only by the operator. Every wording
above was written for an operator. If a customer can read them, the clause about what an interrupted
transfer leaves behind is customer-facing copy and must be judged as such.

**The second reviewer remains OUTSTANDING.** The agent's behaviour is bit-for-bit unchanged by the
follow-up — only the response literal differs — so no new note was required, but this one still
carries an unread review and the branch must not merge to `main` until it is read.

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
