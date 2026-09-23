# Threat note — reading one shadow entry to decide whether an account can be unlocked

**Surface:** the agent's account operations (`ops::accounts::AccountOperations`), a `privs`-adjacent
part of the only root process on the server, driven from a billing-facing panel endpoint.

**Status: written BEFORE the change.** Nothing in `agent/` had been edited when this file was
created; the measurements it argues from — `usermod --unlock` and `usermod --lock` on a
passwordless login, exit codes and the raw shadow field, taken on both polygon families — are
the ones "The defect being fixed" below states. A note written afterwards would be a
justification for a choice already made, which
rules/architecture.md names as the mechanism that stops the next reader looking.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from this session. Per
rules/security.md this change may live on a branch and MUST NOT merge to `main` until a second
reviewer has read it.

## The defect being fixed

`usermod --unlock <account>` on a login that has **no password hash** exits **0 on the Debian
family and 1 on the RHEL family** (measured; unchanged under `LC_ALL=C`, so it is not a locale
artefact). Every hosting account this agent creates is passwordless — `useradd` writes `!` on
Debian and `!!` on RHEL and the agent never sets a password on the account's own entry. So
`AccountOperations::unsuspend`, which treats a non-zero status as a failure, **could not succeed on
any RHEL host for any account**. Reactivating a suspended customer has been impossible on half the
supported matrix since `unsuspend` was written.

## What the change does

`unsuspend` will first ask what is actually in the account's shadow password field, via
`getent shadow <account>` (one key, one line), classify it, and run `usermod --unlock` **only when
there is a password hash behind the lock**. Everything else about the operation is unchanged, and
`suspend` is not touched.

## What an attacker could do with this surface, and why it is safe

1. **The agent now reads a password hash into a root process.** This is the real cost, and the
   adapter's own doc previously argued against reading the shadow database for exactly this reason.
   The objection was about reading the whole database; `getent shadow <name>` returns **one entry
   for one name**, and the name is an `AccountName` the agent has already re-validated
   (`AccountName::parse`) and which `require_existing` has resolved on this host. Bounded further by
   construction: the field is classified into a four-state enum **at the point of reading** and the
   bytes are dropped. No variant carries the field, nothing returns it, nothing puts it in an error,
   a log line, an audit entry or a proto message. **A reviewer must check exactly that** — that no
   `Debug`, `Display`, error variant or trace on the new path can print the field.
2. **Could an attacker make the agent skip the unlock and leave an account locked?** Skipping is
   the *safe* direction: a suspension that stays on is visible to the operator, and the panel's
   attestation refuses to report a reactivation it cannot see. The dangerous direction is unlocking
   something that should stay locked, and the change never widens what gets unlocked — the unlock
   runs on strictly fewer inputs than before, and only when a hash is present.
3. **Could an attacker make the account passwordless-loginable?** No. The change writes nothing.
   The rejected alternative `passwd -u -f` *did* have this property — measured, it succeeds on RHEL
   by **emptying the shadow field**, turning a locked account into one whose password is the empty
   string — and it is rejected for that reason as much as for not existing on Debian.
4. **Message parsing was refused.** Matching `usermod`'s English sentence is locale-dependent, and
   this repository already has a finding where a translated message changed a parse's meaning.
   Treating exit 1 as success was refused too: it would swallow "cannot update the password file"
   and every other real refusal of the same call.
5. **No new surface.** No listening port, no outbound call, no new daemon, no shell string. One
   additional read-only program on the allow-list (`getent`, from `glibc-common`/`libc-bin`), spawned
   with an argv array.
6. **The account name is the only caller-supplied value** and it reaches `getent` as a single argv
   element after validation. `getent` interprets no metacharacters and the agent uses no shell.

## What the author could NOT verify

- That no site-local NSS module (LDAP, SSSD, `nss-pam-ldapd`) makes `getent shadow` **block or time
  out** on a host whose directory server is unreachable. The polygon has only `files`. A hosting
  server with a remote passwd backend is outside anything this session could test, and a reviewer
  should decide whether the agent must pin the lookup to the local files database.
- Behaviour on the six matrix members with no polygon image (Ubuntu 22.04, Debian 12/13,
  AlmaLinux 10, Rocky 9/10). Only ubuntu24 and alma9 were measured; the argument for the rest is
  that they run the same two shadow lineages, which is reasoning, not measurement.
- That `unsuspend` is the only caller that needed this. The author read the callers in `agent/`;
  the backend half was not in this task's writable scope and was not re-read end to end.

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
