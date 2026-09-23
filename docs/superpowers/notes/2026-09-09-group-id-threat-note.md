# Threat note — resolving the web server's group id, and re-applying it to a customer's home root

Date: 2026-09-09
Surface: `agent/crates/agent-core/src/privs/group_id.rs` (`GroupId`, `GroupId::resolve`,
`suggested_buffer`), and its one consumer chain — `agent/crates/agent/src/services/backup/home_group.rs`
→ `ops::backup::restore_backup`'s `finalise` step.

## THIS NOTE IS LATE, AND THAT IS THE FIRST THING TO KNOW ABOUT IT

`rules/security.md` ("Sensitive change escalation") requires the threat note to be written
**first, before the change**, and it gives the reason: *a justification written after the choice
validates the choice; only one written forward from the question can contradict it.*

This note was not. `group_id.rs` landed in commit `8692e32` ("agent: make restore work, stop
leaking database passwords, and answer signals") on 2026-09-08 with three `unsafe` blocks and no
note of any kind. A verification pass on 2026-09-09 mapped every other unit under
`agent-core/src/privs/` to a note and found this one alone uncovered — the note that covers the
rest of the module, `2026-08-30-privs-threat-note.md`, names `account_ids.rs`,
`fork_as_account.rs`, `priv_error.rs` and the five directory wrappers, and predates this file by
nine days. **Reconstructed, therefore, not reasoned forward.**

Saying so at the top is not ceremony. A reader has to be able to tell "this design was argued
before it was written" from "this argument was assembled around code that already exists", because
only the first is evidence the design was considered, and the second is exactly the artefact the
architecture rule warns stops the next reader looking. Everything below was derived by reading the
shipped code and asking what an attacker would try — which is the honest thing this note can be,
and it is strictly weaker than what the rule asks for.

The compliance debt this creates is the same one the rest of the branch carries, recorded again
here so it is not lost: **the change is not compliant, and the note does not make it so.**

## Second reviewer: OUTSTANDING

This is the agent's `privs` module — named by `rules/security.md` as requiring a second human
reviewer and a threat note. **No second reviewer has read this. The requirement is OUTSTANDING.**
No agent session can be that reviewer, and this note is written by an author of the change, so it
closes nothing. `fix/live-findings` MUST NOT merge to `main` while this line stands.

## What this surface is, in one paragraph

`GroupId::resolve(name)` looks a system group up with `getgrnam_r`, refuses gid 0, and hands back
a `Copy` newtype over `libc::gid_t`. Its single production consumer is `home_group`, which resolves
the **web server's** group — a compile-time constant from the distro adapter, `www-data` on Debian
and `nginx` on RHEL, never a caller-supplied string. The number it returns is `chown`ed onto
`/home/<account>` at the last step of a restore, together with mode `0750`, because that is the
arrangement `AccountOperations` creates a home with and a restore that put the account's own group
back would 403 every site the account owns.

The attacker model is the one the `privs` note already sets: a hosting customer who fully controls
the contents of their own home, plus — new here, and the important addition — **anyone who can
influence the host's group database**. That is not the customer. It is the host's administrator, a
package post-install script, or a name service (sssd, LDAP, systemd-userdb) that `nss` will happily
answer from.

## What an attacker would try

Written as attacks, not as a list of what the author got right.

### 1. Put yourself in the web server's group — the sharpest one, and the type cannot see it

`GroupId` proves the group exists and is not root's. It proves **nothing about who is in it.** The
restore then group-owns a customer's entire home root to that group at `0750`, which is read and
traverse for every member.

So the attack is not on the lookup at all: it is `usermod -aG www-data <me>`, or a package that
adds a service account to `nginx`, or an LDAP group whose membership is maintained somewhere off
this host entirely. The moment that happens, every restored home on the server is readable by the
new member — and the restore reports success, because from the agent's point of view this is
exactly the intended outcome.

This is a **property of the product's home layout**, not a defect introduced by this file, and the
same exposure exists on a home `AccountOperations` created and never restored. It is stated here
because `GroupId`'s doc comment reads as a safety argument ("valid by construction", "refuses gid
0") and a reader can come away believing the type is a containment. It is a *lookup*, and the only
thing it excludes is gid 0.

### 2. Rename or delete the group between the resolve and the `chown` — a real window

`home_group` is called in `validated_restoration` (`backup_service.rs:442`), at the top of the
request, and the gid it returns is carried through `Placement::group` and applied in `finalise`
(`restore_backup.rs:744`) at the **end** of the restore — after the artifact is fetched, the
archive extracted and every database replaced. On a large account that is minutes.

`GroupId`'s own doc says, in capitals, `DO NOT CACHE A VALUE OF THIS TYPE`, and gives the right
reason: a group deleted and recreated takes a new number, and a stale one names whoever received
it. The restore path holds the value for the whole operation, which is caching it for the length of
the window that matters. Nothing in the type enforces the instruction — it is `Copy`, it carries no
expiry, and a `u32` is extracted from it one line later in `home_group`, after which even the
newtype is gone.

The exploit needs the group database to change mid-restore, which needs administrator-level access
or a name service, so it is not a customer's attack. The consequence is that root `chown`s a
customer's home to a gid it resolved a long time ago, which by then may belong to a group with
entirely different membership — attack 1, arrived at without anyone having to join a group.

**This is a behaviour question, not a comment question, so nothing about it is changed here.** The
fix, if the reviewer agrees it is one, is to resolve the group in `finalise` rather than in
validation, or to re-resolve there and refuse if the number moved — and to keep the early resolve
as the fail-fast check it also is, so a host with no web server group still refuses the restore
before it moves a byte.

### 3. Feed the lookup a name the C call answers differently than the wrapper assumes

What the three `unsafe` blocks assume, stated as assumptions so a reviewer can attack each:

- **Block 1, the `getgrnam_r` call.** Assumes: `name` outlives the call (it does — `CString` is a
  local held past it); `entry`, `found` and `buffer` are live, correctly typed and correctly
  aligned locals; `capacity` is exactly `buffer`'s length (it is — both come from the same
  variable); and `getgrnam_r` retains none of the three pointers after returning. That last one is
  the load-bearing one and it is POSIX, not an observation.
- **Block 2, `entry.assume_init()`.** Assumes: `code == 0` *and* `found` non-null is
  `getgrnam_r`'s contract for "the entry was written". This is the one place a hostile or merely
  buggy NSS module could hurt us, and the code narrows the blast radius correctly: **only the
  integer `gr_gid` is ever read.** The `char *` fields — `gr_name`, `gr_passwd`, `gr_mem` — point
  into `buffer` and would dangle the moment it drops at the end of the loop iteration; nothing
  reads them, so a truncated, oversized, or adversarially-crafted member list cannot be turned into
  a dangling read. **A change that reads any other field of `libc::group` breaks this and must be
  refused.**
- **Block 3, `sysconf`.** Assumes `_SC_GETGR_R_SIZE_MAX` reads no memory through pointers and is
  thread-safe. True, and the only thing that comes back is a size.

On a **truncated buffer**: `ERANGE` is the only signal, the loop doubles and retries, and the
answer is never a partial entry — because the only field read is the gid, which is not in the
buffer. On a **reused buffer**: there is none. A fresh `vec!` is allocated per iteration and
dropped with it, so no iteration can read what an earlier one wrote. That is slightly wasteful and
exactly right.

### 4. Make the growth loop the attack

The ceiling is 64 KiB and the doc argues it well: a `group` entry that does not fit is a broken or
hostile database, and growing unbounded turns a lookup into an allocation attack. Two things a
reviewer should still look at:

- **The ceiling guards the growth, not the first allocation.** `suggested_buffer()` returns
  whatever `sysconf(_SC_GETGR_R_SIZE_MAX)` says, and that value is allocated on the first iteration
  **before** any comparison against `MAXIMUM_BUFFER`. The check is only reached on `ERANGE`. On
  every supported platform `sysconf` answers a small number and this is theoretical; it is written
  down because "there is a ceiling" is a claim a reader will make about the whole function, and
  today it is a claim about the loop only.
- `capacity.saturating_mul(2)` cannot overflow, and saturation lands above the ceiling, so the
  refusal path is reached rather than a wrap. Correct.

### 5. Make an unreadable database look like an absent group

`ENOENT`, `ESRCH`, `EBADF` and `EPERM` are mapped to `NoSuchGroup`; everything else becomes
`GroupLookupFailed`. That mapping matches what glibc documents ("not found" may be reported as any
of these), and the security consequence is nil **only because both errors refuse**: `home_group`
maps either one to a system failure and the restore stops. If a future caller ever treats
`NoSuchGroup` as "fine, carry on with a default", this mapping becomes the bug — an `EPERM` from an
unreachable LDAP would then be read as "the web server has no group" and a fallback would pick the
gid. Any such caller must be rejected on sight.

### 6. Get gid 0 past the refusal

The check is `resolved.gr_gid == 0`, on the value actually returned, after `assume_init`, before
the newtype is constructed. There is no other constructor — the field is private and
`GroupId(libc::gid_t)` is a tuple struct in a module with no other `impl`. So a `GroupId` holding 0
cannot be built from outside. Worth confirming that stays true; the refusal lives in the
constructor and nowhere else, so a second constructor added later silently repeals it.

Note the refusal is **gid 0 only**. A host whose web server group is `shadow`, `wheel`, `sudo` or
`docker` is accepted, and the restore hands a customer's home to it. The agent cannot fix that —
the name comes from the distro adapter and those are the correct names for the two families — but
"the group cannot be root's" is a much smaller promise than "the group is safe to give a home to",
and the doc's framing invites the larger reading.

## What a second human reviewer must check

Each of these is a thing to look at and a place to look, not a gesture.

1. **That `GroupId` is still constructible only through `resolve`.** Read `group_id.rs` for a
   second `impl GroupId` block, a `From`, a `Default`, or a `pub` on the tuple field. The gid-0
   refusal lives in `resolve` and nowhere else; any of those repeals it silently. Today there is
   one `impl` and the field is private — confirm that, do not take this sentence for it.
2. **That nothing reads a field of `libc::group` other than `gr_gid`.** Grep `group_id.rs` for
   `gr_name`, `gr_passwd` and `gr_mem`. Every one of them points into `buffer`, which is dropped
   at the end of the loop iteration. Zero hits today.
3. **That the restore's gid is still the one the host holds when it is applied**, or that the
   reviewer accepts §2's window with the reason stated. Concretely: follow `home_group`'s return
   value from `backup_service.rs:442` through `Placement::group` to
   `restore_backup.rs`'s `finalise`, and decide whether a value resolved before a
   multi-minute extraction may be `chown`ed afterwards. This note says it may not; changing it is
   behaviour and is deliberately not done here.
4. **That `finalise` cannot be made to `chown` something other than the home root.** It calls
   `std::os::unix::fs::chown` — which **follows symlinks** — on `placement.home_root.join(account)`.
   The argument that this is safe is that only root can create an entry in `/home` (root-owned,
   `0755`) and the path was just `rename`d into place by this same operation. Confirm that argument
   holds on a host where an operator has changed `/home`'s mode, and decide whether `lchown` plus
   an `O_NOFOLLOW` open would be the honest form regardless. This is one line away from the
   `/etc/ld.so.preload` failure `rules/security.md` item 2 records.
5. **That no caller treats `PrivError::NoSuchGroup` as a recoverable condition.** Grep the
   workspace for `NoSuchGroup`. Today the only production consumer is `home_group`, which refuses.
   A fallback to the account's own group, or to a hardcoded gid, is the bug §5 describes.
6. **That the product's answer to §1 is deliberate.** Group-owning a customer's home to the web
   server's group at `0750` is a decision about who may read customer data, taken in
   `AccountOperations` and merely preserved here. The reviewer is being asked to agree with a
   product decision, not only a code one.

## Is the risk observable by any test?

Split honestly, because these are the entries a reviewer should read first.

**Observable, and covered today:**
- gid 0 refused — `agent-core/src/tests/privs/group_id_tests.rs`,
  `the_root_group_is_refused_rather_than_returned`.
- an absent group refused, and a name carrying a NUL refused as absent rather than truncated to a
  different group — same file.
- the inverse control (a real group from this host's database resolves to the gid the database
  records), so a lookup mutated to refuse everything does not pass the file.
- the intended end state on a real host — `/home/<account>` left
  `<account>:<web server group>:750` after a restore — `agent/crates/agent/tests/backup_on_a_real_host.rs`.

**Observable, and NOT covered — the test that should exist:**
- The §2 window. `a_group_recreated_during_a_restore_is_not_applied_from_the_stale_number`, a
  polygon test in `agent/crates/agent/tests/backup_on_a_real_host.rs`: create the account, start a
  restore large enough to hold the window open, `groupdel` and `groupadd` the web server's group so
  it takes a new gid, and assert the home ends up owned by the **new** number or the restore
  refuses. It would fail today. It is named rather than written because writing it means changing
  when the group is resolved, which is behaviour, and this change is comments only.

**Not observable by any test, and this is the important half:**
- §1, group membership. No test can assert who is in `www-data` on a customer's host; the
  membership is not this code's input and not this code's output. A polygon test could add a second
  account to the web server's group and demonstrate it can read a restored home — but that would
  be asserting the *intended* behaviour, not catching a defect. This risk is closed by a human
  agreeing with the layout, or not at all.
- §4's ceiling-vs-first-allocation point. `sysconf` has no seam and cannot be made to lie from a
  test; observing it would mean injecting the suggested size, which is a behaviour change for the
  sake of a theoretical platform.
- The `unsafe` blocks' assumptions themselves. They are POSIX contracts. A test that passes proves
  the libc on the machine that ran it behaved; it proves nothing about the contract, and a test
  that could observe a violation would be observing undefined behaviour. Miri does not run these —
  they are FFI into libc. **This is why the reviewer items 1 and 2 above are grep-shaped: they are
  the only mechanical check available for the property, and they are checks on the source, not on
  a run.**

## What the author could not verify

- No `getgrnam_r` behaviour was measured against a hostile NSS module; the assumptions in §3 are
  read from POSIX and the glibc manual, not observed.
- The §2 window was established by reading the call chain, not by racing a real restore against a
  `groupdel`. The window's existence is a fact about the code; its exploitability on a real host is
  reasoned, not measured.
- Nothing here was run on AlmaLinux with sssd or LDAP joined, which is the configuration where §5's
  `EPERM` mapping stops being theoretical.

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
