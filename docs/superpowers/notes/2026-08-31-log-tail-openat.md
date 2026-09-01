# Threat note — `agent-core::privs::open_in_directory`, and the log tail that uses it

Required by `rules/security.md` ("Sensitive change escalation"): this change adds a
fourth module to `agent/crates/agent-core/src/privs/`, the workspace's only home of
`unsafe`. **This change needs a second reviewer.**

Companion to `2026-08-30-privs-threat-note.md`, which covers `account_ids.rs`,
`fork_as_account.rs` and `priv_error.rs` and is not superseded. This note covers the new
`open_in_directory.rs` and its one caller, `ops::sites::follow_log`, because the
primitive is only as safe as what the caller does with the descriptor it returns — and
the caller is a root process reading a file a hosting customer owns.

## What this surface is

`TailSiteLog` streams the tail of `~/<account>/logs/<domain>.access.log` (or
`.error.log`) to the panel. The agent runs as root. The file, the directory it sits in
and every component of the path below the home are under the customer's full control.

The new public item is one function:

- `open_in_directory(&File, &OsStr, libc::c_int) -> io::Result<File>` — `openat(2)`
  relative to a directory descriptor the caller already holds.

The attacker model is the same as the companion note's: a hosting customer with shell
or SFTP access to their own home, able to influence the panel's inputs, not able to
change the panel's or the agent's code. Reaching this rpc at all additionally requires a
peer-cred-authorised panel connection (`peercred/peer_guard.rs`); a customer reaches it
only indirectly, by asking the panel to show them their own log — which is the intended
use.

## Why this is not `fork_as_account`

The companion note's §2 says the fix for path-based TOCTOU is *"open the directory once,
hold the descriptor, and work through `*at()` syscalls relative to it — the fd, not the
string, is the race-free handle"*, and files it as the follow-up. This change implements
exactly that shape, for reads.

It does **not** drop privileges, and that is the deviation a reviewer should weigh
hardest. `fork_as_account` was rejected here for a stated reason: a streaming tail needs
bytes to flow continuously from the child to the parent, and the companion note refuses
that on purpose — *"a channel from an unprivileged child into the root parent is a
surface, and deserializing attacker-adjacent bytes in the root process is exactly the
kind of thing this module exists to not do"*. Adding a data pipe to `fork_as_account`
would weaken the primitive every write in the agent depends on, in order to strengthen
one read.

So the read happens as root, and the compensating controls are the ones below. **This is
the single largest judgement call in the change**, and the honest summary is: a
capability we do not have cannot be abused, whereas a set of checks can be defeated if
any one of them is wrong. This surface relies on checks. It is narrower than
`fork_as_account`'s surface (read-only, one file, one inode proved before a byte is
read), which is why the trade was judged acceptable — not because checks are as good as
a dropped uid.

## The sequence, and what each position defends

```
resolve_in_home(account, "logs")            -> canonical directory path, ONCE
open(path, O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC) -> directory fd, held for the whole tail
fstat(dirfd): is_dir && uid == account
loop, every 500 ms:
  sink.is_listening()  and  idle deadline    -> may end the tail
  openat(dirfd, name, O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC)
  fstat(fd): is_file && uid == account && nlink == 1
  seek + read, bounded                       -> only now are bytes read
```

The path is named exactly once, in line 2. Everything after that is relative to a
descriptor, and a descriptor names an inode.

## Threats considered

### 1. A symlink in place of the log

**Attack.** `rm ~/logs/example.com.access.log && ln -s /etc/shadow
~/logs/example.com.access.log`, then ask the panel to show the access log. Root opens
it; the panel renders the shadow file to the customer.

**Why it fails.** `O_NOFOLLOW` in `LOG_FLAGS` makes the `openat` fail with `ELOOP`.
`open_in_directory` returns the operating system's own `io::Error` rather than a
flattened one specifically so the caller can tell this from `ENOENT`: `follow_log`
treats `NotFound` as "no traffic yet, nothing to send" and **every other refusal as
`LogUnreadable`**. A symlink attempt is therefore reported, not silently rendered as an
empty log.

**Left open.** Nothing here. `O_NOFOLLOW` is a kernel guarantee on the final component.

### 2. A hardlink in place of the log

**Attack.** `ln /etc/shadow ~/logs/example.com.access.log`. This is not a symlink, so
`O_NOFOLLOW` says nothing about it, and the path genuinely is inside the customer's
home, so every path-containment check ever written passes it.

**Why it fails.** Only the inode gives it away, and the inode is what is checked:
`metadata.nlink() != 1` refuses it. The obvious follow-up — hardlink, then unlink the
original so `nlink` returns to 1 — is refused by the second check,
`metadata.uid() != account uid`, because the inode is still `root:root`. **Neither check
alone is sufficient and both are present.** The `fstat` is on the descriptor
(`File::metadata`), never on a path, so there is no stat-then-open window: the inode
that was checked is provably the inode that is read, because `link(2)` afterwards
creates a directory entry and cannot repoint an open descriptor.

**Left open.** A hardlink to a file the customer *already owns* passes, correctly — that
is their own data, and they could read it by other means.

**Not relied upon.** `fs.protected_hardlinks=1` would also stop the `/etc/shadow` case
on most hosts. It is an unverified sysctl on someone else's server, so nothing here
depends on it.

### 3. A FIFO in place of the log

**Attack.** `rm ~/logs/example.com.access.log && mkfifo ~/logs/example.com.access.log`.
`open(O_RDONLY)` on a FIFO with no writer **blocks in the kernel indefinitely**. The
tail runs inside `tokio::task::spawn_blocking`, and every operation in the agent — site,
SSL, PHP, account — is dispatched through that same pool. Repeat it and the pool
(512 threads) is exhausted and the daemon stops answering. That is denial of service on
a root process from an unprivileged account, and it costs the attacker one command.

**Why it fails.** `O_NONBLOCK` is in `LOG_FLAGS`, so the open returns immediately, and
`metadata.is_file()` then refuses the FIFO before a single byte is read. The flag is
never cleared afterwards — there is no `fcntl(F_SETFL)` anywhere in the crate — so the
hang cannot be reintroduced by a later edit to the read path. On a regular file
`O_NONBLOCK` is a no-op, so keeping it costs nothing.

`is_file()` covers a device node, a directory and a socket in the same expression.

**Left open.** `open_in_directory` deliberately does not force `O_NONBLOCK` — the flag
set a safe open needs differs by what is being opened, and a function that silently
added flags would hide which protections a call site actually asked for. A future caller
that omits it inherits both the hang risk and an unhandled `EINTR`. This is now stated
in the function's own documentation rather than disclaimed, and it is the main way a
later change could reopen this hole.

### 4. The directory swapped underneath a running tail

**Attack.** The tail runs for as long as an operator leaves the log tab open — minutes
or hours, polling every 500 ms. Between two polls: `rmdir ~/logs && ln -s /etc ~/logs`.
A tail that reopened by path would then follow the symlink, because `O_NOFOLLOW` covers
only the final component and `logs` is an intermediate one. Repeating the swap every
poll interval gives the attacker unlimited retries against a single stream.

**Why it fails.** The directory is opened once and its descriptor is held for the life
of the tail; every later open is `openat(dirfd, name, …)`. A descriptor names an inode,
so no rename, `rmdir` or symlink planted at that path is ever consulted again. This is
the control the whole design exists for, and it is why the request type carries
`directory` and `file_name` separately instead of one `PathBuf` — a single path would
have to be re-resolved every poll, which is the race itself.

Outcomes of the variants:

- `rm -rf logs` — the fd names an unlinked inode. `openat` returns `ENOENT`, the tail
  goes silent and ends at its idle ceiling. Nothing can be created inside an unlinked
  directory by anyone, so no attacker-controlled file can appear behind that fd.
- `mv logs logs.old && ln -s /etc logs` — the new `logs` is never named, so it is never
  opened. Closed.
- `mv logs logs.old && mkdir logs` — the tail keeps reading `logs.old` while nginx
  writes to the new `logs`. **Accepted staleness, not an escalation:** everything still
  reachable behind that fd is the account's own file, re-checked for `uid` and `nlink`
  on every poll. The stream goes silent and idles out.

The directory itself is opened with `O_DIRECTORY|O_NOFOLLOW` and then `fstat`ed for
`is_dir()` and the account's `uid`, so a `logs` that was already a symlink or already
someone else's directory at the moment the tail started is refused before the loop
begins.

### 5. `../` and embedded NUL in the file name

**Attack.** `openat` resolves a *relative path*, so a `name` of `../../../etc/shadow`
would walk straight out of the pinned directory and undo threat 4 entirely. The subtler
variant is `"access.log\0/../../etc/shadow"`: a naive `CString` conversion truncates at
the NUL and the caller's own inspection of the string sees something else.

**Why it fails.** `open_in_directory` refuses a `name` that is empty, contains `/`, or
is `.` or `..`, before the syscall; `CString::new` then rejects an embedded NUL outright
rather than truncating. This is stricter than `openat` requires, deliberately.

It is also the second check, not the first: the name reaching this function is built by
`SitePaths` from a validated `Domain`, which cannot contain `/`, `..` or a NUL. Nothing
a request carries becomes a file name. The syscall-level check exists because
`rules/security.md` asks for defence in depth and because this function is `pub` and
will acquire callers this note's author did not write.

**Left open.** Nothing for the current caller. A future caller that passes an
attacker-controlled name gets the component check but not the `Domain` guarantee.

### 6. An oversized log — memory exhaustion

**Attack.** The customer runs `truncate -s 50G ~/logs/example.com.access.log`, or simply
fills it, and asks the panel to show the log. A naive `read_to_end` reads fifty
gigabytes into the root daemon's address space and takes every other tenant's control
plane down with it. Nothing about this requires a race or a symlink; it is one command.

**Why it fails.** The line cap (`MAXIMUM_HISTORY_LINES`, 1000, from `sites.proto`)
bounds what is *sent* and is explicitly documented as not being a memory bound. The
memory bounds are separate and are in `follow_log`:

- the history is read **backwards** in 256 KiB chunks and stops at whichever comes first
  — enough newlines, `HISTORY_BUDGET` (4 MiB), or the start of the file;
- each follow poll reads at most `FOLLOW_CEILING` (256 KiB), and a log written faster
  than the client reads has its excess **skipped and reported**, never buffered;
- the worst realisable total is roughly 12 MB per stream (the 4 MiB window, one lossy
  UTF-8 copy of it, and the `&str` index over it), **independent of the file's size**.

The line-index path is worth naming because it is the one that is not obvious: 4 MiB of
bare newlines would be ~4.2 M `&str` entries. It cannot happen, because the scan's
second guard stops after the first chunk once enough newlines are seen — buffer size and
index size trade off against each other.

**Left open.** Per-stream memory is bounded; the number of concurrent streams is not
bounded by this module. A panel that opened thousands of tails would multiply the 12 MB.
That bound belongs to the panel and to the peer-cred gate, and it is not enforced here.

### 7. Truncation racing the read

**Attack.** Less an attack than a daily event the customer can also trigger at will:
`logrotate` with `copytruncate` empties the file between the `fstat` that reported its
length and the `read_exact` that reads it. The customer can reproduce it on demand with
`: > ~/logs/example.com.access.log` in a loop.

**Why it is not a fault.** A short read is distinguished from a real IO error:
`ErrorKind::UnexpectedEof` returns "the file shrank", not an error. The follow loop
answers it exactly as it answers a truncation seen *between* two polls — restart from
offset 0 — and the history scan sends what it had already read. Treating it as a system
failure, as an earlier revision of this code did, would kill every operator's tail at
midnight and would hand the customer a one-line denial of their own log.

**Left open.** A tail cannot distinguish `copytruncate` from a customer deliberately
truncating; both restart. That is the correct behaviour for both.

### 8. Descriptors and the blocking pool

Two availability threats that are not about the file at all.

`O_CLOEXEC` is set on both the directory and the log descriptors. This module never
execs, but the agent does spawn processes (`nginx -t`, `systemctl`, `openssl`) from
other threads, and a descriptor into a customer's home leaking into one of those is
avoidable for free. Note the companion note's §6: `O_CLOEXEC` does **not** survive
`fork`, so this is not a defence against `fork_as_account`'s inherited-fd gap, which
remains open and is unchanged by this note.

The blocking-pool exhaustion threat has three exits, because a tail that cannot end
itself is a thread leak and `spawn_blocking` tasks cannot be aborted from outside:
`sink.is_listening()` at the top of every poll (independent of whether a line arrived,
so a silent log is covered); a `MAXIMUM_IDLE` of five minutes since the last delivered
line; and a bounded send inside the sink, because a client that stops reading *without*
closing would otherwise park the thread between two checks where neither guard can fire.
The last of those is the subtlest and was found in review.

## Deliberate design choices a reviewer should weigh

- **`io::Error`, not a flattened error type.** The caller must be able to tell `ENOENT`
  from `ELOOP`; collapsing them would report an attack as "nothing here yet".
- **The primitive does not `fstat`.** It proves only *which inode was opened*, and says
  so. It cannot know what the caller came for, and a check that looks complete but does
  not fit the caller's need is worse than an explicit obligation.
- **Flags are the caller's.** See threat 3's "left open". The alternative — forcing a
  flag set — hides which protections a call site asked for.
- **Read as root, not as the account.** See "Why this is not `fork_as_account`" above.
  This is the choice most worth a second reviewer's disagreement.

## Summary of what is NOT closed

1. **The read happens as root.** Every defence here is a check, not a capability. If
   any one of `O_NOFOLLOW`, `O_NONBLOCK`, `is_file`, `uid` or `nlink` is later removed
   or reordered, the hole it closed reopens silently. A dropped uid would have failed
   safe; this fails only as safe as its checks.
2. `open_in_directory` does not force `O_NONBLOCK`, so a future caller can reintroduce
   the FIFO hang. Documented, not enforced.
3. The staleness case (`mv logs logs.old && mkdir logs`) leaves the tail reading the old
   inode until it idles out. Inherent to pinning an inode; no privilege consequence.
4. Concurrent stream count is not bounded by this module; only per-stream memory is.
5. The companion note's open items are unchanged. In particular §6 — descriptors
   inherited across `fork` — is not addressed here, and `O_CLOEXEC` does not help with
   it.
6. Whether `/home` itself, or the account's home directory, could be swapped *before*
   `resolve_in_home` runs is out of this note's scope; it is the same window the
   companion note's §2 describes, and it is unchanged.

## Tests

`agent/crates/agent-core/src/tests/privs/open_in_directory_tests.rs` covers the `../`,
`.`, `/`, empty and embedded-NUL rejections, the `O_NOFOLLOW` symlink refusal, and the
`ENOENT`-versus-refusal distinction the caller depends on.
`agent/crates/ops/src/tests/sites/follow_log_tests.rs` covers, against a real temporary
directory the test itself owns: the FIFO refusal, the hardlink refusal, the wrong-uid
refusal, the symlink refusal, the swapped-directory case, mid-tail truncation, the
history cap and the skipped-byte marker. None of these needs root.

What still cannot be tested unprivileged, and is therefore **not covered**: the refusal
of a file owned by *another real account* (the tests use a synthetic uid and reach the
same branch, which is close but not identical), and anything requiring a real
`getpwnam_r` entry — `AccountIds::resolve` is exercised only through its own failure
path here. Those belong on a root polygon.
