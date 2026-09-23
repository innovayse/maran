# Threat note — carrying the login's password STATE on the suspension attestation

**Surface:** `proto/agent/v1/accounts.proto` (`GetAccountSuspensionStateOk`), the attestation the
panel's suspension and reactivation both gate on, produced by the only root process on the server;
and the two command handlers that consume it
(`backend/src/Maran.Modules/Accounts/Commands/{SuspendAccount,ReactivateAccount}`).

**Status: written BEFORE the change.** No file in `proto/`, `backend/src/Maran.Modules/Accounts/`,
`backend/src/Maran.Agent.Client/` or `agent/` had been edited for this change when this file was
created. The whole working tree is uncommitted, so `git status` cannot attest that; the mtimes of
every file this change touches were recorded first, in a working report kept only as a scratch
file (not committed), and each precedes this note's own mtime.
The measurements argued from here are the same ones the sibling `account-unlock-threat-note.md`
states: `usermod --unlock`/`--lock` on a passwordless login diverge by exit code between the
Debian and RHEL families, measured on both polygon families.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from this session. Per
rules/security.md this change may live on a branch and MUST NOT merge to `main` until a second
reviewer has read it. It is the sibling of
`2026-09-08-account-unlock-threat-note.md`, which carries the same outstanding debt for the agent
half of the same defect; a reviewer should read both together, because either half alone leaves
reactivation broken.

## The defect being fixed

The attestation carries one boolean, `login_locked`, read from `passwd -S`. `passwd -S` answers
`L` (Debian) / `LK` (RHEL) both for a login LOCKED OVER A PASSWORD and for a login that never had a
password at all. Every hosting account is the second kind — the agent never sets a password on an
account's own entry — so the boolean is `true` for such an account **for ever**: before a
suspension, during it, and after it.

`SuspendAccountCommandHandler` refuses when the boolean is false, and is correct: it is asking "can
a password authenticate this login", and for a passwordless account the answer is genuinely no.
`ReactivateAccountCommandHandler` refuses when the boolean is TRUE, asking "is something of the
customer's still held down" — and gets the suspension question's answer. **So the panel refuses
reactivation for every hosting account, on both families**, even now that the agent's `unsuspend`
succeeds. One boolean cannot carry both directions.

## What the change does

`GetAccountSuspensionStateOk` gains an additive enum field, `login_password_state`
(`LoginPasswordState`: `UNSPECIFIED` = 0, `EMPTY`, `ABSENT`, `LOCKED`, `USABLE`), filled from the
agent's existing `StoredPassword` classification of the raw shadow field — the fact `passwd -S`
cannot express, and identical on both families. `login_locked` is kept, kept filled and kept
meaning exactly what it means today. Suspension then refuses when a password could authenticate
(`EMPTY` or `USABLE`); reactivation refuses only on `LOCKED`.

## What an attacker could do with this surface, and why it is safe

1. **Does this weaken suspension?** No. Suspension's condition moves from "`passwd -S` did not say
   locked" to "the shadow field can authenticate", which is the same question asked of better
   evidence. The states it refuses on — `EMPTY` (authenticates with the empty password) and
   `USABLE` (a real hash) — are exactly the states a suspension must not leave behind, and `EMPTY`
   is the state the rejected `passwd -u -f` produces. A reviewer must check that suspension still
   refuses on both, and that no state was quietly added to the accepted set.
2. **Does this let a suspended account be reported as reactivated while still held down?**
   Reactivation still refuses on `LOCKED` — a real hash behind a lock marker, the only state in
   which anything of the customer's is actually held down and the only state the agent's
   `usermod --unlock` acts on. It stops refusing on `ABSENT`, which is what a NEVER-SUSPENDED
   hosting account looks like, so refusing on it was refusing the normal state of every account.
   Every other condition in the attestation (vhosts still stubbed, cron markers, locked SFTP
   logins, the unreadable-directory guard) is untouched, so a resumption that did not happen is
   still caught by them.
3. **Could an attacker put the login into `ABSENT` to slip past reactivation's gate?** Reaching
   `ABSENT` from `LOCKED` means destroying the account's password hash, which requires root on the
   host — an attacker who has that does not need this path — and the result is an account that
   cannot authenticate at all. The direction that would matter, making a locked account usable,
   is not reachable through anything this change writes: **the panel writes nothing here and the
   agent's read is read-only.**
4. **Secrets.** No hash and no shadow bytes cross the wire. The proto field is an enum with four
   inhabitants; the agent classifies at the point of reading and drops the field
   (`StoredPassword` holds no bytes in any variant). A reviewer must check that the new enum is
   what is sent and that no diagnostic on the new path prints the field.
5. **Version skew.** The field is additive with a `0` = `UNSPECIFIED` default, per rules/proto.md,
   so an older agent answering a newer panel sends `UNSPECIFIED`. Both handlers treat
   `UNSPECIFIED` by falling back to `login_locked` — i.e. to exactly today's behaviour, which is
   conservative in both directions (suspension refuses, reactivation refuses). A skewed pair is
   therefore no worse off than today and never more permissive.
6. **No new surface.** No rpc, no port, no outbound call, no new binary executed; one enum field on
   an rpc that already exists, and no new agent capability.

## What the author could not verify

- That a second reviewer agrees `ABSENT` is safe to accept on reactivation. That is the whole
  judgement of this change and it is exactly what one author cannot check for themselves.
- The six OS-matrix members with no polygon image. Only Ubuntu 24.04 and AlmaLinux 9 are exercised.
- A site-local NSS backend that made `getent shadow` answer differently — inherited from the agent
  half's note, which carries the same limit.
- Whether any operator has hand-set a password on an ACCOUNT's own entry in a live install. Such an
  account behaves as `USABLE`/`LOCKED` and is handled by the normal path, but no field data exists.

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
