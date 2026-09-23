# Threat note — deleting a file-transfer login could `userdel` a neighbouring hosting account

Date: 2026-09-09
Surface: `ops::ftps::delete_ftps_user` and `ops::sftp::delete_sftp_user` — two operations of the
only root process on the server, driven from a customer-facing panel endpoint an ordinary tenant
may call for their own account. This is a **cross-tenant destructive defect on a privileged
surface**, so rules/security.md's escalation rule applies.

**Status: written BEFORE the change.** No file under `agent/` had been edited when this note was
created. A note written afterwards is a justification validating a choice already made, which
rules/architecture.md names as the mechanism that stops the next reader looking.

## Second reviewer: OUTSTANDING

**No second reviewer has read this change. The requirement is OUTSTANDING.** The author is an agent
session: it cannot dispatch a reviewer and it cannot be one — the rule exists because the author of
a privileged change is the person least able to see what they assumed. Per rules/security.md the
change may live on `fix/live-findings` and **MUST NOT merge to `main`** until a human has read this
note; `main` is protected and the second reviewer is a merge gate (rules/git.md). The owner is to
be told at hand-off that this surface carries an outstanding review.

A note recording the debt is the minimum, never the discharge of it.

## Why this is its own note and not an amendment to the SFTP password note

`docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md` is the adjacent note: it
argues the same jail-home enumeration on `set_sftp_password`, and it is where the ownership check
this change copies was first written down. It was still the wrong file to extend, for two reasons.

1. **Its subject is a different security property.** That note is about a *suspension bypass* — a
   business control defeated by one customer against their own account, and it says in as many words
   that "nothing here crosses a tenant boundary or escalates privilege". This defect is exactly the
   thing that note excludes: one tenant destroying another tenant's system identity. A reviewer sent
   to the password note to check a cross-tenant deletion would be reading an argument written to
   establish the opposite scope.
2. **Its blast radius and its reviewer checklist are different.** A password written onto a
   neighbour's login is a stolen credential; a `userdel` of a neighbour's login is an irreversible
   removal plus a uid left unallocated over a populated home. The checks a reviewer must run differ,
   and the note names them below.

The password note is cross-referenced from here rather than rewritten.

## The defect

A file-transfer login is its own passwd entry named `<account>_<suffix>`, created
`useradd --non-unique --uid <account uid>` and homed in the account's jail. `AccountName` permits
underscores (`^[a-z][a-z0-9_]{2,29}$`), so **the login `bob` of account `alice` and the hosting
account `alice_bob` are the same eleven characters**, and no decode of the name can tell them apart.
The account the panel authorised is the only thing that can, which is why every other operation on
these logins takes the account as a parameter and filters the passwd database by the jail home the
agent itself wrote.

Both delete operations did neither. Their whole body was:

```rust
let outcome = host.run(distro.userdel_binary(), &[user.as_str()])?;
```

No account parameter, no jail-home enumeration, no per-account lock. The service handler for the
FTPS one discarded the account it had just validated (`Ok((_, user))`), although
`validated_ftps_user`'s own doc comment says the account it returns "is what the login's jail is
derived from for the ownership check".

**Consequence.** A customer authorised for account `alice`, calling
`DeleteFtpsUser(account="alice", ftps_username="bob")` — or the SFTP equivalent — removes the passwd
entry of the hosting account `alice_bob`. `userdel` is run without `-r`, so `/home/alice_bob`
survives on disk owned by a uid that is now unallocated, and the next `useradd` on that host can be
given it: a cross-tenant destruction plus a latent ownership transfer, from an rpc whose stated
meaning is "revoke one of my own logins". The neighbour loses SSH/SFTP/FTPS access, their php-fpm
pool's user, and their cron spool's owner.

`create_*_user` cannot make the mistake, because `useradd` refuses a name already in use and that is
mapped to `AlreadyExists`. Delete was the one operation with no gate at all, and it is the
destructive one.

**Confirmed by execution, not by reading.** The Phase B/C review (finding F2, kept only as a
working report, not committed) added a witness test in a scratch copy that seeded
`FakeFtpsHost` with a foreign login `alice_bob` homed at `/home/alice_bob` and asserted the deletion
*succeeded and removed it*. The witness passed. The SFTP half is the identical hole and is **shipped
on real installs today**.

## The fix

Both operations now take `account: &AccountName`, and as the first statement of the operation:

1. take the existing per-account lock (`ops::accounts::take_account_lock`) — the same lock
   `set_*_password`, `create_*_user`, the deletion cascade, `php::write_pool` and the four backup
   operations take. **No sixth lock is introduced** (rules/rust.md, "What this agent serialises");
   it never waits, so it adds no edge to the wait-for graph;
2. enumerate `host.account_logins(distro.passwd_database(), account, jail.directory())` and refuse
   unless the requested name is in it.

The enumeration classifies by the passwd **home** — a value the agent itself wrote when it created
the login — and never by a prefix of the name, so a hosting account whose name happens to collide
is never mistaken for a login: its home is `/home/<account>`, not the jail.

**The refusal is `NotFound`, deliberately, and not a distinct "that login is not yours".** A
distinct answer would confirm to the caller that a system user of that name exists on the host,
which is the neighbour's existence — the same reasoning rules/security.md §6 applies to the panel's
own 404-never-403 rule for a resource another account owns. It also keeps the operation's
idempotency contract intact: a second deletion and a deletion of a name the account never held give
one answer, so a retry after a lost response stays safe. The cost is honest and is written on the
operation: an operator debugging a genuine typo sees the same answer as an attacker probing, and the
agent's audit trail is where the distinction lives.

## What a reviewer must check

1. **That the check is on the HOME and not on the name.** A prefix match (`name.starts_with(account)`)
   re-opens the whole defect: `alice_bob` starts with `alice`. Confirm the enumeration filters by
   `jail.directory()` and that both fakes model the home rather than ignoring it.
2. **That the account reaching the operation is the panel's authorisation and not a decode.** The
   handler must pass the `AccountName` from `validated_*_user`, never one parsed out of the login
   name. Confirm no `_` split exists anywhere on this path.
3. **That the lock is taken exactly once, at the entry.** The lock never waits, so a nested second
   take is a self-refusal. Confirm the deletion cascade (`AccountOperations::delete`) does not reach
   these entry points while holding it — it reaches `remove_account_ftps` / `remove_account_sftp`,
   which take no lock, and that must stay true.
4. **That the positive control exists.** A guard that refuses everything is indistinguishable from a
   guard that works unless a test deletes the account's *own* login successfully.
5. **The FTPS handler.** `agent/crates/agent/src/services/ftps/**` is a peer's area in this pass;
   confirm its `delete_ftps_user` handler stops discarding the account and passes it.

## What the author could not verify

- **Nothing was run on a real host.** The polygon images could not be rebuilt in this pass (a peer's
  in-flight installer edits make the build fail before any assertion runs), so every claim here rests
  on unit tests against the in-memory hosts plus reading. The real-host suites
  (`sftp_on_a_real_host`, `ftps_on_a_real_host`) were not executed.
- **The behaviour of a host whose passwd database was edited by hand.** The enumeration trusts the
  home the agent wrote; an operator who moved a login's home out of the jail makes that login
  invisible to this operation, which then refuses to delete it. That is the safe direction, but it
  is unverified against a real `getent` on a host with a directory service configured.
- **Whether any real install has already suffered this.** The SFTP hole is shipped. Nothing in the
  agent records enough to tell after the fact whether a `userdel` came from this path or from an
  operator, so the author cannot say the defect was never exercised.

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
