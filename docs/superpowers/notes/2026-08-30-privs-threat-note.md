# Threat note — `agent-core::privs`

Required by `rules/security.md` ("Sensitive change escalation"). Covers
`agent/crates/agent-core/src/privs/`: `account_ids.rs`, `fork_as_account.rs`,
`priv_error.rs`. **This change needs a second reviewer.**

Revised after review round 1: `EINTR` (§4), the gid-0 and system-account hole
(§3b) and inherited file descriptors (§6) were added as that round closed the
first two and recorded the third.

## What this surface is

The Maran agent is a root daemon. `privs` is the only place in the workspace where
`unsafe` is permitted and the only way the agent does work as a hosting customer
rather than as root. Every write inside `/home/<account>/` — a document root, an
ACME challenge file, a restored backup — is supposed to pass through it.

The public surface is two items:

- `AccountIds::resolve(&AccountName)` — uid and primary gid from `getpwnam_r`.
- `fork_as_account(&AccountIds, work)` — fork, drop, verify, run `work`, `_exit`,
  and in the parent map the child's exit status to a typed `PrivError`.

The attacker model is a hosting customer who fully controls the contents of their
own home directory (shell, SFTP, the file manager) and can influence the panel's
inputs, but does not control the panel's code or the agent's arguments.

## The syscall sequence, and what each position defends

```
fork()                      -> child only
  setgroups(1, [gid])       -> needs CAP_SETGID, which setuid gives away
  setgid(gid)
  setuid(uid)
  verify: getuid == geteuid == uid, getgid == getegid == gid,
          getgroups() is [] or [gid],
          setuid(0) FAILS
  work()                    -> narrowest possible unit, inside catch_unwind
  _exit(status)             -> never exit(): no atexit, no stdio flush
waitpid(WNOHANG, deadline)  -> restarted on EINTR; SIGKILL + reap at the deadline;
                               maps status to PrivError
```

## Threats considered

### 1. A symlink in the account's home pointing at `/etc/shadow`

**Attack.** The customer replaces `~/sites/example.com/public/.well-known/x` with a
symlink to `/etc/shadow` and waits for the panel to write an ACME challenge there,
or asks the panel to "read" it back through the files API.

**Why it fails now.** Two independent controls, and the design assumes either may
be bypassed alone:

- `resolve_in_home` (`validation/path.rs`) canonicalises before deciding
  containment, so the symlink resolves to `/etc/shadow`, `starts_with` the home
  fails, and the call returns `PathError::EscapesHome`. It returns the *canonical*
  path, so the caller does not reopen the attacker-controlled name.
- Even if containment were wrong, the write happens in a child running as the
  customer's uid with root's supplementary groups cleared. `/etc/shadow` is
  `root:shadow 0640`; the child is neither root nor in `shadow`, so the open
  fails with `EACCES` and the parent sees `PrivError::WorkFailed`.

The second control is the one that matters, because the first is a check and the
second is a capability. A check can be raced; a capability the process does not
have cannot be.

**Left open.** A symlink to a file the *customer* can already read or write is not
blocked and should not be — that is the customer's own data. A symlink to a
world-writable location outside the home (`/tmp/foo`) is refused by
`resolve_in_home`, not by the uid drop, so a caller that skips `resolve_in_home`
loses that half. Nothing in the compiler enforces the pairing; it is enforced by
review and by `rules/rust.md`. **That is the weakest link in this design.**

### 2. TOCTOU — the path is swapped between resolving it and using it

**Attack.** The panel resolves `~/sites/example.com/public` to a real directory. In
the microseconds before the child opens it, the customer `rename()`s that directory
away and puts a symlink to `/etc` in its place. The resolved *string* now names
something else.

**Why the damage is bounded.** The race is real and is not closed by path
resolution — no purely path-based check can close it, which is the reason the uid
drop exists rather than being an extra belt. What the customer wins by winning the
race is a write performed as themselves, to a place they could have written to
themselves. They cannot escalate: the child holds no capability they do not
already have.

**Left open.** The race is not eliminated, only made worthless for escalation. A
caller that wants it eliminated must open the directory once, hold the descriptor,
and work through `*at()` syscalls relative to it — the fd, not the string, is the
race-free handle. `privs` does not force that shape today; the follow-up is to
give `work` a directory fd rather than a path. Filed as a known gap, not fixed
here.

A second, subtler instance: `AccountIds` is resolved in the parent, before the
fork. If the account is deleted and recreated between resolution and the drop, the
child drops to ids that were correct when read. See §5.

### 3. A `setuid` that partially applies

**Attack.** Not attacker-initiated so much as attacker-*exploited*: any state in
which the child believes it dropped and did not. Causes include a `setgroups` that
silently no-ops under a restrictive seccomp/LSM policy, an `RLIMIT_NPROC` failure
mode, or a future edit that reorders the three calls.

**Why it fails now.** The child re-reads its own credentials after dropping and
refuses to run `work` unless *all* of the following hold: real uid == effective uid
== the requested uid; real gid == effective gid == the requested gid; the
supplementary group list is empty or exactly `[gid]`; and `setuid(0)` **fails**.

That last check is the important one and it is behavioural rather than
declarative. A saved-set uid left behind by an incomplete drop is invisible to
`getuid`/`geteuid` — the process looks unprivileged and can return to root at
will. Asking the kernel for root back and requiring a refusal is the only way to
observe it. On failure the child exits `77` (`EX_NOPERM`) and the parent returns
`PrivError::VerificationFailed`, which is deliberately a distinct variant from
`DropFailed` so an operator can tell "the drop errored" from "the drop lied".

`getgroups` returning `-1` (the list did not fit in 32 entries) is treated as a
verification failure, not as an inconclusive result.

**Left open.** The verification runs in the child, so it cannot detect a compromise
of the child's own code. It defends against a kernel/policy surprise and against a
future reordering, not against an attacker who already executes in the child.

### 3b. The account resolves to gid 0, or to a system account

**Attack.** The panel is induced to act as `postgres`, `nginx`, `mail` or `bin`.
`AccountName::parse` accepts any 3-30 character lowercase/digit/underscore name,
so every one of those is a syntactically valid hosting-account name. Worse, an
account whose `passwd` entry carries `pw_gid == 0` yields a child with real and
effective gid **root**.

**Why the verification does not catch it.** It cannot, and this is the important
part: the child would drop, re-read its credentials, find exactly the gid it asked
for, and proceed. Verification confirms *fidelity* — that the syscalls did what
they were told — not *safety*. The safety question is "were these the right ids to
ask for", and it belongs at resolution time.

**Why it fails now.** `AccountIds::resolve` refuses, with distinct typed variants:
`RootAccount` for uid 0, `RootGroup` for gid 0, and `SystemAccount` for an id below
its floor. The floors are `UID_MIN` and `GID_MIN` from `/etc/login.defs` — the same
file `useradd` consults when it allocates an id, so the agent and the tool that
created the account agree by construction — read as the two separate settings
`login.defs` defines them to be. They default to the same 1000 everywhere in the
support matrix, but substituting one for the other would fail permissively on a
host where an administrator raised `GID_MIN` above `UID_MIN`: a group id between
the two would clear the uid floor while the host itself considers it a system
group.

Both fall back to 1000 for **every** input that is not a usable floor — a missing
key, a missing value, a non-numeric or negative value, a commented-out line, a
`SYS_`-prefixed relative, and an explicit **zero**. Zero is the case that is not
obvious and the one that matters: `"0".parse::<u32>()` succeeds, so a naive parser
accepts it, and a floor of zero turns `id < minimum` into `id < 0`, which is never
true for an unsigned integer. Every system-account refusal would then stop firing —
one line in a config file, no error, no log, and only the explicit `== 0` checks
left standing. The parser therefore treats a parsed zero as no value at all.

The fallback errs only towards false rejection, which an operator can see and
report; it cannot admit a service account, because no supported distribution places
one above 1000.

**Left open.** The floor is a numeric threshold, not an allow-list. An account
created above `UID_MIN` by something other than Maran is indistinguishable from
one Maran created, and would be accepted. Binding accounts to the panel's own
database is the stronger control and lives above this module. An administrator who
lowers `UID_MIN` below the ids their own service accounts occupy also lowers this
floor, by design — the agent follows the host's definition of a human account
rather than asserting its own — but a zero, which would disable the check outright
rather than lower it, is refused.

### 4. The child is killed mid-write

**Attack.** The customer fills their quota, triggers the OOM killer, or an operator
kills the child while it is halfway through writing a config or restoring content.

**What happens.** `waitpid` reports `WIFSIGNALED`, and the parent returns
`PrivError::ChildSignalled { signal }` — never `Ok`. The caller therefore always
learns that the outcome is unknown; it is never told the work succeeded.

**Why the state is recoverable.** Two things carry the weight, and neither is in
this module:

- `work` is required to be the narrowest possible unit — create one directory,
  write one file. A small unit has few partial states.
- Every operation above `privs` is idempotent (`rules/rust.md` "Idempotency"), and
  system config writes go through `ops::safe_write`'s temp-file-then-atomic-rename
  protocol, so a killed child leaves a stale temp file rather than a half-written
  target.

**Left open.** A killed child writing *customer content* (a restore, not a config)
can leave a partially written file inside the home. That file belongs to the
customer and is not a privilege issue, but it is a correctness one, and it is the
calling operation's job to converge on retry. `privs` does not clean up after a
signalled child — it has no way to know what the child had done.

**Fixed in review round 1.** `waitpid` was not restarted on `EINTR`. The agent
registers tokio signal handlers and `systemctl stop` signals the whole cgroup, so
this was a path that would be taken, not a theoretical one: on it the parent
returned `WaitFailed` while the child kept running and was never reaped — a zombie
per occurrence, and a caller told the outcome was unknown for work that then
completed anyway. The call now loops while it returns `-1` with `errno == EINTR`.
Because an `EINTR` return reaps nothing, the pid cannot be recycled between
iterations, so the loop cannot come to name an unrelated process.

**Fixed in review round 2.** `waitpid` had no timeout, so a `work` closure that
blocked forever blocked its thread forever. It now polls with `WNOHANG` against a
`CHILD_PATIENCE` deadline of two minutes and, at the deadline, `SIGKILL`s the
child and reaps it, returning `PrivError::ChildTimedOut`. What that buys is not
only a reclaimed pool thread: the wedged child is POST-DROP, running at the
customer's uid and holding every descriptor the parent had at fork time (§6), so
an unbounded wait left that process alive for the life of the daemon. The
deadline is a private seam (`wait_until(child, patience)`), which is what lets the
give-up path be tested rather than reasoned about.

**Also fixed in review round 2.** The child called `std::panic::set_hook` to
silence the panic hook before running `work`. That takes the standard library's
process-global hook lock and drops the previous hook's box — a lock and an
allocator round trip, on EVERY call, in a forked child of a multi-threaded
process, which is precisely the hazard this module forbids its own callers from
creating. With no wait timeout at the time, the failure mode was a permanently
wedged customer-uid child rather than an error. It is gone: `catch_unwind` still
stops a panic unwinding out of the fork, the default hook is accepted as a hazard
confined to a path agent code must never take (clippy `panic = "deny"`), and the
new deadline bounds it if one ever does.

### 5. A recycled uid — the account was deleted and its uid reissued

**Attack.** Account `alice` (uid 1005) is deleted; her home directory or a stray
file elsewhere survives with uid 1005 still stamped on it. A new account `bob` is
later created and `useradd` reissues 1005. `bob` now owns every leftover file
`alice` left behind, and any work Maran does "as `bob`" can touch them.

**Why it is partly handled.** Ids are resolved by *name* through `getpwnam_r` at
the moment of use, never cached across operations and never supplied by the
caller. `AccountIds` has private fields and one constructor, so the panel API
cannot hand the agent a uid of its choosing — a caller who wanted to run as another
tenant would have to name that tenant, and the name is what the authorization layer
above checks. `resolve` also refuses uid 0 outright (`PrivError::RootAccount`), so
an account that somehow resolves to root is never run as.

**Left open, and this is a real gap.** Nothing in `privs` prevents uid recycling
itself — that is `ops::accounts`' responsibility at delete time (remove the home,
and do not reuse ids), and it is not enforced from here. The window in §2 also
applies: ids are read before the fork, so a delete-and-recreate racing the fork
lands the child on stale ids. The window is microseconds and requires
administrator-level timing, but it is not zero. Neither is closed by this change.

### 6. File descriptors inherited across the fork

**Attack.** `fork` duplicates the parent's entire descriptor table into the child.
The agent's root process holds the listening unix socket, every connected client
socket, log file handles, and whatever `ops` had open at the moment of the fork.
After the drop, all of those are held by a process running as the customer. If a
`work` closure — or anything a future edit lets run in the child — could be
steered into writing to one, a customer-uid process would be writing into the
panel's control channel or its audit log. Descriptors already open are not
re-checked against the new uid: the permission check happened at `open` time, as
root.

**What holds now — closed in review round 2.** The child sweeps its own
descriptor table between the verified drop and `work`:
`close_inherited_descriptors` calls `close_range(3, c_uint::MAX, 0)` — one
syscall, async-signal-safe, allocation-free — and falls back to a
`RLIMIT_NOFILE`-bounded `close` loop on a kernel older than 5.9, where
`close_range` answers ENOSYS. Descriptors 0, 1 and 2 are kept: the child shares
the daemon's stdio, and a process whose next `open` lands on descriptor 1 is a
worse hazard than an inherited terminal.

`O_CLOEXEC` was never the answer here and is worth recording as such: the
standard library sets it on what it opens, which covers `exec`, and this module
forks and never execs. Only an explicit sweep closes anything.

**What is enforced.** `a_dropped_child_does_not_inherit_the_daemons_open_descriptors`
in `agent/crates/agent/tests/privileges_on_a_real_host.rs` opens a file in the
parent, forks through the real `fork_as_account`, and the closure writes its
marker only when `/proc/self/fd/<n>` is gone in the child. Removing the sweep
turns that named test red in both polygons. It is polygon-only because the drop
it rides on needs root and a real account.

**What is still review-enforced.** A failure of the sweep is not reported — there
is no channel out of the child but an exit status, and refusing the work because
a descriptor would not close turns hardening into an outage. And the sweep runs
after the drop, so a `work` closure that captured a `File` opened before the fork
now finds it closed rather than usable: closures must open what they need.

## Deliberate design choices a reviewer should weigh

- **Raw `libc`, not `nix`/`users`/`caps`.** A wrapper that hides the three calls
  behind an RAII guard takes away the one property this module needs: that a
  reviewer can read the order the kernel sees. The cost is more `unsafe`; every
  block carries a `// SAFETY:` comment naming its invariant.
- **`_exit`, not `exit`.** `exit` runs atexit handlers and flushes stdio buffers
  that the child inherited *already full* from the parent — the parent's bytes
  would be written twice, from a process it does not know about.
- **No pipe back from the child.** The child's outcome is an exit status and
  nothing else, so `PrivError::WorkFailed` loses which error occurred. That is a
  real ergonomic cost, accepted deliberately: a channel from an unprivileged child
  into the root parent is a surface, and deserializing attacker-adjacent bytes in
  the root process is exactly the kind of thing this module exists to not do.
- **`catch_unwind` around `work`.** A panic unwinding out of the fork would let a
  second copy of the caller's stack keep running in a process the caller does not
  know exists. It is converted to `EXIT_WORK_FAILED`.
- **The child must not take locks, and its allocation rule is stated
  precisely** (amended after the challenge-write review, which found that every
  closure in the workspace broke the earlier blanket "must not allocate freely"
  while the rule was still being cited as observed). Short-lived allocation
  through the system allocator is permitted, because glibc and musl both take
  the malloc locks before `fork` and reinitialise the arenas in the child; a
  lock this program owns, the tokio runtime, and unbounded allocation are not.
  The drop-and-verify path itself stays allocation-free regardless, being held
  to async-signal-safety rather than to this rule. The full argument, including
  what would break it, is on `fork_as_account`. Only the forking thread
  survives into the child, so any mutex another thread held at fork time is
  locked forever in the child. This is a review obligation on every closure
  passed to it; it is not enforced by a type.

## Summary of what is NOT closed

1. The `resolve_in_home` + `fork_as_account` pairing is enforced by review, not by
   the compiler. A caller that uses one without the other loses half the defence.
2. TOCTOU is made worthless for escalation, not eliminated. Path-based, not
   fd-based (`*at()`); the follow-up is to hand `work` a directory fd.
3. A hanging closure costs a blocking-pool thread for `CHILD_PATIENCE` — two
   minutes — before the child is killed and the thread returns. Bounded, not free.
4. `PrivError::WorkFailed` does not say what failed.
5. uid recycling is prevented at account-delete time, elsewhere, and not by this
   module. The resolve-then-fork window is small but non-zero.
6. ~~Descriptors inherited across the fork~~ — **closed in round 2** (§6): the
   child sweeps them with `close_range` between the verified drop and `work`,
   and a named polygon test goes red when the sweep is removed. What remains is
   that a failed sweep is not reported, and that a closure must open what it
   needs after the fork rather than before it.
7. The id floor is a numeric threshold, not an allow-list of accounts the panel
   created.
8. The fork path itself is **not covered by an automated test in this change** —
   see the report. It cannot be exercised without root and a real account.

---

# Addendum — the ACME challenge write (`ops::files`)

Added with the `FilesService` implementation of `WriteFile` and `DeleteEntry`.
Required by `rules/security.md` ("Sensitive change escalation"): this adds a new
privileged, customer-facing surface and five new `unsafe` wrappers to `privs`.
**This change needs a second reviewer.**

## What the surface is

The panel's ACME issuance answers an HTTP-01 challenge by asking the agent to put
a token file at `sites/<domain>/.well-known/acme-challenge/<token>` inside the
customer's home, and to take it away once the authority has read it. Until now the
agent implemented no `FilesService` at all, so this is the first rpc that creates
a file inside a customer's home at the panel's request.

New code:

- `agent-core/src/validation/relative_path.rs` — `RelativePath`, a path stored as
  validated components rather than as text.
- `agent-core/src/privs/{directory_entry_name,create_file_in_directory,
  make_directory_in_directory,rename_in_directory,remove_file_in_directory}.rs` —
  the `*at` wrappers. `open_in_directory` existed already, from the log tail.
- `ops/src/files/{open_parent_directory,write_in_home,remove_in_home}.rs` — the
  walk, the write and the unlink, each run inside `fork_as_account`.
- `agent/src/services/files/` — the rpc handlers, the stream collector and the
  error mapping.

The attacker model is unchanged: a hosting customer who fully controls the
contents of their own home directory and can influence the panel's inputs, but
controls neither the panel's code nor the agent's arguments. What is new is that
they now control **every directory the agent walks through**, because
`sites/<domain>/` is theirs.

## §7 — What an attacker with a customer account can attempt, and what stops each

**a. Plant a symlink at a directory level, so the write lands outside the home.**
`rm -rf ~/sites && ln -s /etc ~/sites`, then ask the panel to issue a
certificate. Stopped by the descriptor walk: `open_parent_directory` opens
`/home/<account>` once and reaches every level below it with `openat` and
`O_DIRECTORY | O_NOFOLLOW`, so a level that is a link is refused rather than
followed. Killed by mutation M1.

**Corrected after review round 1, and the correction matters more than the
original claim.** This row used to say the attack was "independently stopped by
`resolve_in_home`" as well, and rested that on M1 and M12 dying separately.
Both halves were wrong. The walk starts at a fixed root, follows no symlink at
any level, and traverses a component list that provably holds no `..`, no `/`
and no empty component — **a descent with those three properties cannot end
outside the home**, so it is not one of two mechanisms, it is the mechanism.
`resolve_in_home` on the write path adds no reachable refusal, and M12's kill
was an ordering assertion against a recording mock: no-opping the real
`resolve_in_home` inside `ProcessFilesHost` survives the whole suite, and always
will.

**Resolved in review round 1 by deleting the call from the write path.** Keeping
it with a comment saying it was inert was the first fix; the ruling was that a
labelled no-op is the same object as an unlabelled one, because the next reader
sees a containment call in a security-critical function and reasons about it as
containment, and the label ages into staleness rather than into truth. The same
judgement in the same round deleted an unreachable second-header check in the
service layer, and the same judgement earlier in this plan deleted an
`IgnoreQueryFilters()` that no mutation could distinguish from its own absence.
So the write path now has exactly one containment mechanism and it is the walk.
`delete_entry` still calls `resolve_in_home` — see **h** — and that is a
difference between the two operations rather than an inconsistency: a removal
must locate an entry that already exists, and a write does not, because the walk
constructs the path as it goes.

**b. Plant a symlink at a level the agent is about to CREATE.** `mkdirat` answers
`EEXIST` for a name already taken, symlink or not, and the walk treats `EEXIST` as
"fine, it was already there" — so the safety of that whole branch rests on the
`openat` that follows, which carries `O_NOFOLLOW`. Named test:
`a_symlink_planted_at_a_level_that_is_about_to_be_created_is_refused`.

**c. Plant a symlink at the destination file name.** Stopped by the shape of the
write rather than by a check: the content goes into a NEW temporary file created
with `O_CREAT | O_EXCL | O_NOFOLLOW` and is then `renameat`d into place. `renameat`
REPLACES the name it is given and never resolves it, so a link at the destination
is unlinked rather than written through — including a dangling one, which a
naive open would have created for the attacker. Killed by mutation M24,
`renameat` → `renameat2(…, RENAME_NOREPLACE)`, which turns "replace the
destination" into "refuse if it exists" — the exact semantic the protection rests
on. This row previously claimed no mutation was possible; that claim was false,
and it was the same "no mutant exists" reasoning error this plan has now made
three times. The mutant is one identifier and one argument, and it kills three
tests.

**d. Pre-create the temporary file, or plant a symlink at its name.** `O_EXCL` is
what refuses it; without `O_EXCL` the bytes would go through a descriptor the
customer chose. Killed by M4.

**e. Leave a directory where the challenge file goes.** `renameat` refuses to
replace a directory (`EISDIR`/`ENOTDIR`), so the write fails and the directory is
untouched rather than emptied.

**f. Leave a FIFO where the challenge file goes, so the REMOVAL blocks.** The
removal opens the entry to judge it, and a FIFO with no writer blocks in the
kernel forever. That would be a forked child that cannot be reclaimed, costing the
parent its full two-minute `CHILD_PATIENCE` per removal — a denial of service a
customer can ask for as often as they like. Stopped by `O_NONBLOCK` in
`ENTRY_FLAGS` and by the `is_file` check that follows. Killed by M10. The test
that covers it would HANG rather than fail if `O_NONBLOCK` were dropped, which is
recorded on the constant.

**f2. Leave a file belonging to ANOTHER account at the challenge name.** Two
tenants; the second leaves a file they own in the first's challenge directory.
The unlink would succeed on permissions alone — Unix checks the DIRECTORY's write
bit, not the file's owner — so only `metadata.uid() != uid` on the entry can
refuse it. Added in review round 1: the check existed and had no test at any
level, and the walk's level check (**n**) is a different one. Killed by mutation
M25, in the polygon, where a second real account can be created.

**g. Hardlink another file to the challenge name before the cleanup.** `ln` makes
a name that is genuinely inside the home and is not a symlink, so every
path-based check passes it. Only `nlink == 1` on the inode gives it away. Killed
by M9. Note what this is and is not: the removal already runs as the account, so
this is not a privilege boundary — it stops the AGENT being made the instrument
of a removal the panel did not ask for, and stops "the challenge was cleaned up"
being a claim about a file that was never a challenge.

**h. Point the challenge name at a file outside the home and have the agent
delete it.** Stopped by `unlinkat` with no flags, which removes the ENTRY and
never follows it, and by `O_NOFOLLOW` on the open that judges the entry first.
`delete_entry` also runs `resolve_in_home` on the file's own path beforehand,
which canonicalises and answers `EscapesHome` — but, as in **a**, that is not an
independent stop: the removal is safe without it. What `resolve_in_home` IS the
only producer of here is the idempotent `NotFound`, because the forked child's
outcome is an exit status and every child-side refusal comes back as
`RemoveFailed`. That refusal is observable, and it is driven against a real
filesystem and a real privilege drop by
`a_challenge_that_is_already_gone_is_reported_as_not_found` in the polygon,
added in review round 1 — M14 alone was another mock-ordering kill.

**i. Swap a directory between the containment check and the write.** The window is
real and is not closed — no path-based check can close it (§2). What is closed is
its value: the write does not reopen the resolved path, it walks descriptors, so
the swap redirects nothing. The attacker's best outcome is a write performed as
themselves into a directory they own.

**j. Make the agent create a setuid file in the customer's home.** The mode is a
plain number from the panel, so a `0o4755` would produce a setuid file the
customer owns, written by a root daemon on request. Refused — not masked — in two
places: the operation (M13) and the child that performs the write (M5). Masking
would carry out a request the caller did not mean and report success.

**k. Exhaust the root daemon's memory through the write stream.** `WriteFile` is
client-streaming and the whole body must be in memory before the fork, so the
collector caps it at one mebibyte and refuses the chunk that would cross the line
before appending it. Killed by M21.

**l. Ask for a path that escapes, or a name a syscall would misread.** `..`, `.`,
a doubled separator, an absolute path, an interior NUL, another control character,
an over-long component, and a path deeper than eight components are all refused by
`RelativePath::parse`, which stores components rather than text so there is
nothing left to re-parse downstream. Killed by M16-M19. `directory_entry_name`
refuses the same shapes again at the syscall (M20).

**m. Ask the agent to remove a directory tree recursively.** `files.proto`
declares a `recursive` flag the agent does not implement. It is REFUSED rather
than quietly downgraded to a single-file removal: a caller told "done" for a tree
that still exists will proceed on that belief. Killed by M15, through the
handshake test over a real socket.

**n. Have the agent walk into a directory belonging to a DIFFERENT account.** Two
tenants; one leaves a world-writable directory where the other's `sites/` goes.
Stopped by `metadata.uid() != uid` on every level, home included. This is the one
row that cannot be tested without root — a unit test cannot `chown`, and handing
the walk a foreign uid makes the HOME check fire first, so a unit test written
that way would be named for the level check and killed by the home check. It is
covered in the polygon instead
(`a_challenge_directory_owned_by_another_real_account_is_refused`), and the
polygon fixture sets the planted directory to `0o777` **on purpose**: at `0o755`
the permission bits refuse the write on their own and the ownership check is never
reached, which is exactly the false-green this note is written to prevent. Killed
by M22, in the polygon.

## What this addendum does NOT close

1. §1's weakest link — the review-enforced pairing of `resolve_in_home` with
   `fork_as_account` — is **not** what this area relies on, and saying so is the
   correction of round 1. The containment on the write path is the descriptor
   walk alone, and the `resolve_in_home` that used to sit beside it was deleted
   once it was established that it could not fail there. What holds this surface
   up is the walk plus the dropped uid. `delete_entry` keeps the call for the one
   answer only it can give (**h**), which is a different job from containment.
2. §2's TOCTOU window is not eliminated. It is made worthless for escalation here
   by the descriptor walk — which is the follow-up §2 filed ("hand `work` a
   directory fd rather than a path"), now done for this area and only this area.
   `create_site` still creates its document root by path.
3. The child still cannot say WHY it refused (§4, "no pipe back"). So a removal
   that hit a FIFO, a hardlink, a symlink or another account's file comes back as
   `FilesOpError::RemoveFailed` and not as the narrower reason. It is deliberately
   NOT collapsed into `NotFound`, which would report an attack as an absence; the
   idempotent `NotFound` is produced by the root-side check instead. A
   consequence worth naming: `FilesOpError::NotARegularFile` is therefore
   **unreachable through `ProcessFilesHost`** today and exists only for the unit
   tests that drive `remove_in_home` directly. The follow-up is not a pipe — §4
   is right to refuse one — but a small set of distinct child exit codes, which
   `fork_as_account` already carries the mechanism for. Not done here.
4. `write_file` performs two separate privilege drops — one to create the
   directories, one to write the file. Since the containment check between them
   was deleted, the remaining reason for the split is that a write which could
   also build directories is a write that can be aimed at a tree nobody asked
   for, so the creating walk and the writing walk are asked for different things
   (`MissingParents::Create` against `Require`). A caller who wins the race in
   the gap gets §7i's outcome, which is nothing. Merging them into one drop is a
   reasonable future simplification and is not done here.
5. Seven of the nine declared `FilesService` rpcs are not implemented and answer
   `UNIMPLEMENTED`. That is the intended state, not a gap: each will be built with
   the caller that needs it, and reviewed then.
6. `O_DIRECTORY` and the `is_dir()` check in the walk are **jointly, not
   individually, observable**, and the code now says so on `DIRECTORY_FLAGS`
   rather than claiming each enforces something. Either alone can be deleted with
   no test going red; both together are caught only at the LAST level of the
   walk, because anywhere higher the next syscall on the opened descriptor
   returns `ENOTDIR` and produces the same error. The kernel, not either check,
   is what makes the middle of the walk safe. The pair is kept for the quality of
   the refusal, not because either half is load bearing.

---

# Addendum 2 — the whole feature: sites, multi-PHP and SSL

Written for Task 19, the last of the sites/PHP/SSL plan, and required by
`rules/security.md`'s escalation rule: this plan changed `privs`, so a threat
note is part of the review package rather than a courtesy.

The two sections above cover `privs` itself and the ACME challenge write. This
one covers the surfaces the rest of the plan added — **site rendering, the log
tail, the certificate store, and the per-pool PHP settings** — and, more
importantly, it states what is **left open**. A note that lists only what is safe
is not a threat note; the open items are the half a reviewer cannot reconstruct
for themselves.

## The attacker

The one this product is built against: somebody who **already holds an
unprivileged hosting account** on the server. They can run PHP as their own uid,
write anywhere their uid can write, and drive the panel's own API for the
resources they own. They are not root, they are not another tenant, and they are
not the operator. Every row below is written from that position.

## §8 — What that attacker can attempt against this plan's surfaces

**a. Break out of the vhost by putting a directive in a domain.** A `server_name`
line that ends early and starts a `root /etc` of the caller's choosing is the
panel's equivalent of SQL injection (`rules/security.md` item 4). Stopped at the
type boundary: `Domain::parse` admits only ASCII letters, digits, `-` and `.`, so
a newline, a carriage return, a `;`, a `{` or a control character is not a domain
and never becomes one. The value is **validated, not escaped** — the templates
escape nothing, deliberately, and that is safe only because nothing unvalidated
reaches them. The same holds for aliases, which are parsed as domains, and for
`Upstream`, whose grammar is host-or-host:port with no scheme, path or query.

**b. Point a reverse-proxied site at something on the internet, or at
`127.0.0.1:5432`.** `Upstream` constrains the *shape* of the value and nothing
constrains the *destination*. **This is open.** A site owner can proxy their
vhost at the panel's own database port, at the agent's socket host, at a
link-local metadata service, or at any address on the internet, and the server
will make the request on their behalf. It is a server-side request forgery
surface with a control panel in front of it. Nothing in this plan closes it, and
nothing in this plan pretends to: reverse-proxy sites are an operator-facing
feature today, and the moment they are exposed to customers this needs a
destination policy (a deny list for loopback, link-local and the panel's own
ports at minimum). Named here so it is a decision rather than a discovery.

**c. Escape `open_basedir`.** They can, and it is not a boundary. It is written
into every pool as a convenience against accidents, and the isolation that
actually holds is the pool's uid: the worker runs as the account, so the
filesystem refuses what the account may not read whatever PHP thinks. This is
said plainly in the plan's own out-of-scope list and repeated here so that nobody
later reads the template line as protection.

**d. Countermand the hardening with a per-site PHP setting.** A customer setting
is only ever a `php_value`, and `open_basedir`, `disable_functions` and
`cgi.fix_pathinfo` are written above them as `php_admin_value`, which `php_value`
cannot override at any position in the file. Before that even matters, the name
must be one of the whitelisted entries in `PhpOverride::ALLOWED` and the value
must pass its bound and a `char::is_control` rejection — so `disable_functions`
is not a name a customer can set at all, and a value carrying a newline cannot
smuggle a second directive into the pool. Refuse-don't-drop: an unknown name is
an error, never a silently discarded line.

**e. Run PHP as another account, or as root.** The pool declares `user` and
`group` as the account and php-fpm drops to them; a pool is per account × version
and its socket is `0660 www-data:www-data`, so one account's worker is not
reachable through another's vhost. Verified on a real host rather than argued:
in the Ubuntu polygon, `php-fpm: pool <account>-8.2` runs as the account and
`pm.max_children` equals the account's plan budget.

**f. Upload `evil.jpg` and have it executed as PHP.** Two independent stops, and
the vhost says they are independent: `try_files $uri =404` in the `\.php$`
location refuses a PATH_INFO request before `fastcgi_pass` is reached, and the
pool sets `cgi.fix_pathinfo=0` itself. Either alone closes it; neither is
labelled as relying on the other.

**g. Read another tenant's log through the log tail.** The tail names a site and
a log kind, never a path: the agent derives the file from the account and the
domain, so there is no path for a caller to supply and nothing to traverse. The
panel resolves the site through the tenant-scoped query first, so a site the
caller does not own is a 404 before the agent is asked.

**h. Get a private key into a log, or read one from disk.** The certificate store
is `/etc/maran/certificates/<domain>/`, mode `0700 root:root`, with `privkey.pem`
at `0600` — **outside every account's home, deliberately**, because a site's PHP
runs as that customer and a key inside the home is a key the site itself could
read. Verified on the real host. On the panel side there is no column holding
key material, the ACME account key is encrypted at rest, `ToString` is overridden
on every type that carries material, and `EnableSensitiveDataLogging` is off.

**i. Have the panel install a certificate for a domain they do not own.** The
install is resolved through the site, and the site through the tenant filter, so
the domain must be one the caller already owns. **What is not checked is the
material itself:** nothing compares the certificate's subject or SANs against the
domain it is being installed for. That is not a cross-tenant escalation — the
worst outcome is a customer breaking their own site's TLS — but it means the
panel will cheerfully serve a certificate for `other.example.com` on a site named
something else, and the operator has no signal. **Open.**

## §9 — What is left open, in one place

`rules/security.md` requires this section, and every item here is carried from
the plan's ledger or found by Task 19's golden path. None of them is a tidy-up.

1. **`MaxSites` is enforced by a count-then-insert race.** Two concurrent
   creations can both read a count below the plan's ceiling and both insert. The
   consequence was accepted as the mild one — Sites loses *before* touching the
   host, so the outcome is one site over quota and no host state — but it is a
   check that a determined caller can beat, and it is not a database constraint.
2. **Certificate installation spans two contexts and reports the failure
   wrongly.** Key material reaches the host first and the row is written second,
   and the write's `catch (DbUpdateException)` reports *every* database failure as
   "this domain is taken". So a connection drop, a timeout or a constraint nobody
   anticipated is shown to the operator as a domain conflict, while a key sits on
   disk with no row pointing at it — and the renewal pass, which selects from
   rows, will never see it again. The narrow catch this needs (the unique-index
   violation alone) was not written.
3. **The agent's log-tail idle clock resets on every delivered line.** The
   300-second idle guard therefore never fires for a chatty log, so a tail can be
   held open indefinitely by traffic the caller generates. The mitigation that
   shipped is a per-account concurrency budget of six streams, which bounds the
   damage without closing the mechanism: the limit is per API process, not
   cluster-wide, and the guard is still not a time bound.
4. **There is no ACME staging smoke test, and the client has never completed an
   issuance against a real authority.** Task 19 established exactly how far it
   reaches: `GET /directory` and `HEAD /new-nonce` succeed against Let's Encrypt
   staging and the signed `POST /new-acct` is refused with
   `urn:ietf:params:acme:error:invalidContact` for the default contact address.
   Order creation, challenge validation, finalisation and download have been
   exercised **only against a fake authority in tests**. The renewal logic, the
   nonce handling and the authorization-reuse fix are therefore unproven against
   the one implementation that matters.
5. **The daily renewal never runs.** `CertificateRenewalScheduler` publishes
   `CertificateRenewalRequested` once a day and the running host logs "No routes
   can be determined" for it: `CertificateRenewalJob` is not discovered as a
   handler, and no generated handler for the message exists in the host's output.
   Every certificate this panel issues therefore expires silently. Found by
   reading a live log, not by a test — 758 backend tests call `HandleAsync`
   directly and are all green.
   **Closed 2026-09-01** by naming the type `CertificateRenewalHandler`, which is
   the convention Wolverine discovers, and by a test that asks the RUNNING
   panel — not a registration — whether the message has a handler.
6. **A newly created PHP site has no php-fpm pool.** `create_site` writes the
   vhost, and `update_site_php_version` is the only writer of the pool — its own
   doc comment says so. A site created and never switched has a `fastcgi_pass`
   naming a socket that does not exist. Not a security hole; a functional one,
   recorded here because it is the same "each piece is right, nothing assembles
   them" shape as the rest of this list.
   **Closed 2026-09-01**: `create_site` writes the pool through the shared
   `write_site_pool`, which the version switch now also uses, so the two cannot
   drift apart again.
7. **No web server can read any document root.** `useradd --create-home` leaves
   `/home/<account>` at `0750` with a group the web-server user is not in, so
   nginx cannot traverse it. Proven on a real host: with the home as created,
   every request is refused with `stat() … (13: Permission denied)`; a single
   `chmod o+x /home/<account>` turns the same request into a `200`. Whether the
   fix is a traversal bit or a group is a design decision, and it is a security
   decision either way — a group makes the account's files readable by every
   process running as the web server, a traversal bit does not.
   **Closed 2026-09-01** by the group, not the traversal bit. The reasoning, what
   the group actually grants, and what is deliberately left undone are in
   Addendum 3 below.
8. **`Acme:CertificateStorePath` is read by nobody.** It is declared in
   `AcmeOptions`, documented in `.env.example` and in
   `installer/panel.env.example`, and used nowhere; the agent writes to a
   hard-coded `/etc/maran/certificates`. An operator who sets it will believe key
   material is somewhere it is not, which is the worst way for a configuration
   knob to be wrong.
9. **Certificate material is not checked against the domain it is installed for**
   (§8i above).
10. **A reverse-proxy upstream has no destination policy** (§8b above).

---

# Addendum 3 — the home directory's group, decided (2026-09-01)

Item 7 of §9 above was left as an open design decision: no web server can read
any document root, and the two ways to fix it are not equally safe. It is closed
here, and this section is the reasoning, because rules/security.md makes a change
to what an unprivileged principal may reach a change that needs a threat note
rather than a commit message.

## What was broken

`useradd --create-home` leaves `/home/<account>` at `0750`, owned by the account
and its own group. The web server's user is in neither. Every document root the
agent creates is inside a home, so a real nginx logged
`stat() "/home/<account>/sites/<domain>/" failed (13: Permission denied)` and
refused every request — to a static site as much as a PHP one. No site this
panel created could be served at all.

## The two candidate fixes

**`chmod o+x /home/<account>`.** Works, and is what the reproduction used. It
grants traversal to the "other" class, which is not a principal: it is every
local user that is not the owner and not in the group. On a shared hosting
server that class is *every other customer* — their PHP workers, their FTP
sessions, their cron jobs. Traversal alone does not grant a listing, but it does
grant reach: with the path of a file known or guessed, any of them can open
anything world-readable underneath, and world-readable is what a customer's
uploads and `wp-config.php` backups routinely are. The mode also cannot express
"only the web server", so it cannot be narrowed later without breaking whatever
came to rely on it.

**`chgrp <web server group> /home/<account>`, mode kept at `0750`.** Chosen.
Traversal is granted to exactly one principal — the group the web server runs
as — and "other" still gets nothing at all.

## What the chosen fix actually grants, stated plainly

The web server's group can traverse `/home/<account>` and can then open anything
below it that its own mode allows. In practice that is the account's site files,
which is the point. Two consequences a reviewer should weigh rather than assume:

- **It is not the account's own group.** The home's group is no longer the
  account's, which is a visible change: `ls -ld /home/<account>` shows
  `www-data` (or `nginx`) where it showed the account. Files *inside* the home
  are unaffected — they are created by the account, with the account's primary
  group — so per-file group permissions still mean what they meant.
- **Anything running as the web server can reach every account's home.** That is
  the honest cost, and it is smaller than the alternative's rather than zero: a
  process that has become `www-data` has, on any panel with this shape, already
  escaped whatever confinement mattered. The reason it is acceptable here is that
  a customer's PHP does **not** run as the web server — each account has its own
  php-fpm pool running as its own uid (spec §11, `write_pool`) — so the set of
  things running as `www-data` is nginx itself and nothing a customer supplies.
  Were that to change, this decision changes with it.

## What is deliberately NOT done

- **The mode is not widened past `0750`.** It is restated as `0750` on creation
  rather than inherited, because `useradd` honours `HOME_MODE`/`UMASK` from
  `/etc/login.defs` and a host setting could otherwise leave a home
  world-readable — which would silently undo the whole point of the group.
- **Existing accounts are not repaired.** The step runs on creation and nowhere
  else. Re-applying it would be the agent re-owning a directory it did not
  create, which is the one mistake `create` already refuses to make for a
  pre-existing user. **Consequence, stated rather than hidden: an account created
  by an earlier build of the agent still cannot serve a site**, and an operator
  has to run the `chgrp` by hand. There is no migration command in this pass.
- **The group name is not a literal.** It comes from the distro adapter
  (`web_server_group`): `www-data` on Debian, `nginx` on RHEL. An account created
  on AlmaLinux with a Debian group name would be created *successfully* and fail
  only when a customer's site 403s.

## How it fails, and where that is proved

If the web server's group does not exist — no web server installed — `chgrp`
refuses and **account creation fails**. That is deliberate: an account whose home
the web server cannot enter is an account whose sites cannot be served, and
reporting the creation as a success would hide it until a customer noticed.

The proof is a polygon test, not an argument:
`a_real_nginx_serves_a_site_out_of_the_accounts_own_home` creates a real account,
creates a real site, writes an `index.html` **as the account**, and asks the
polygon's own nginx for it over a real socket on port 80. It passes on Ubuntu
24.04 and on AlmaLinux 9. Removing the `chgrp` turns it red with the default
server's response instead of the site's.

---

# Addendum 4 — the php-fpm pool lifecycle (2026-09-01)

Not on §9's list, because nobody had looked for it. It was found by the polygon
the moment site creation started writing pools: one test's account was deleted,
its pool file stayed, and the next test's `php-fpm -t` failed for a reason that
had nothing to do with what that test was checking.

## Why this is an availability defect and not a tidiness one

A pool file names the account it runs as, and php-fpm resolves that name **at
startup**, not per request. Once the account is gone:

```
ERROR: [pool <account>-8.3] cannot get uid for user '<account>'
ERROR: FPM initialization failed
```

and the master **refuses to start or reload at all**. So a leftover pool is not
one broken customer. It is a trap that goes off at the next reload — a
certificate renewal, a PHP settings change, a package upgrade, a reboot — and
takes PHP down for **every tenant on the server**, including everyone who did
nothing. Cause and symptom separated by days is the worst shape a defect can
have, and a routine, supported operation was what armed it.

Nothing in the agent removed a pool. Not `ops::accounts`, not `ops::php`.

## What now removes what, and in which order

| Operation | What goes | Order, and why that order |
|---|---|---|
| Delete account | **Every** pool the account has, across the whole supported version set | Pools **before** `userdel`. While the account exists each pool is valid, so `php-fpm -t` passes and each master reloads cleanly. Reversed, every remaining pool names a user that no longer resolves — and because the removal protocol validates AFTER unlinking and restores on refusal, the file would be put back and become **unremovable by the operation meant to remove it**. |
| Delete site | The site's pool, only when the panel says no other site of the account uses that version | Vhost **before** pool. Between the two the site is already gone from nginx, so the pool has no traffic left to serve; reversed, a live vhost points at a dead socket and every request in the window is a 502. |
| Switch version | The pool of the version being left, on the same condition | Pool **last**. The new pool is bound before the vhost moves, and the old one goes only once nothing points at it. |

The account case asks the **closed supported set** rather than being told which
versions to clean up, deliberately: nobody knows which versions an account has
used. The panel's row says what a site is bound to *now* and does not remember
that the account ran 8.1 for a year — and a pool left over from a version
nothing currently uses is precisely the one that survives every targeted
cleanup.

## The one decision that is the panel's, and why

**A pool belongs to an ACCOUNT and a version, not to a site.** Two of an
account's sites on 8.3 share one pool and one worker budget — that is the design,
because a worker budget belongs to a plan. So deleting a site, or moving one to
another version, does **not** by itself mean its pool may go.

Whether it may go is the panel's answer and cannot be the agent's. The panel
holds the site rows; the agent holds a directory of rendered vhosts, which is a
rendering of those rows rather than a second copy to read back
(rules/architecture.md — truth lives in PostgreSQL). An agent that counted
`fastcgi_pass` lines to decide would be inventing a second source of truth for
the one question where being wrong takes a site nobody touched off the air. So
`DeleteSiteRequest.retired_php_version` and
`UpdateSitePhpVersionRequest.remove_previous_pool` are panel-owned, and **absent
means "leave it alone"** — the safe default, which is also the common case.

The identity a removal is built from is a validated `AccountName` and a validated
`PhpVersion`, never a string formatted from the request: an unlink that escaped
its directory destroys a file nobody notices, where a write that escaped it
leaves one somebody finds.

## Failure is not best-effort

A pool that cannot be removed **aborts the account deletion**, with the account
still present. That is the recoverable half: an account that is still there can
be deleted again once whatever refused is fixed, whereas an account that is gone
with its pool left behind cannot be repaired by any operation this agent has.

## What is proved, and where

`deleting_an_account_leaves_a_host_the_real_php_fpm_will_still_start`
(polygon, both families) creates a real account with a real PHP site, deletes the
account through the real `AccountOperations::delete`, and asserts that the real
`php-fpm -t` still succeeds — asked **first**, deliberately before the weaker
file-existence check that would otherwise fire first and hide it. Removing the
pool removal turns it red with the real error above.

## Still open

- **A pool orphaned by an earlier build stays orphaned.** Removal happens on the
  operations above and there is no sweep. A host that already has one is still
  armed, and `rm /etc/php/<v>/fpm/pool.d/<gone>.conf` is the manual repair.
- **The socket file is not removed with the pool.** php-fpm unlinks its own
  socket when the master reloads without that pool, so this is a cleanup php-fpm
  does rather than one the agent needs — but it is stated rather than assumed,
  because it is the agent's directory.
