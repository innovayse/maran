# Threat note: every process the agent starts gets a deadline, a group and a ceiling (issue #90)

Written **before** the change, as `rules/security.md` ("Sensitive change escalation") requires. No
code on `fix/90-agent-bounded-spawn` had been edited when this note was written. The tree it
describes was first the branch head `7e75b67`, and is now `1eb8dde` (the current `dev`). The
claims below held for `7e75b67` and `697cad1`; the fifth pass re-checked them against `1eb8dde`
and marks what changed.

Three passes of the same stage wrote it, all before any code change. The first was interrupted.
The second checked every claim against the tree, corrected one that was wrong (the FTPS candidate
spawn already has a deadline), and added three more program seams, the `unsafe` constraint on the
core, the archive pre-scan as a security consumer of truncation, and the residuals at the end. The
third pass split the plan into the five parts that are written in parallel, and it pinned the
public API each part takes from another ("The contract between the parts"). It also settled four
questions the second pass had left open: how the call class reaches the core, where
`AdapterBinary` sits relative to the core, what `php_fpm_binary` takes, and where the account-side
archiver's process group goes.

A fourth pass, also before any code change, re-ran every count in this note against the tree (all
held: 10 `Command::new` sites, 13 `spawn_argv` callers, 50 program-seam lines, one `"--"`, 84
`CommandOutcome {` lines, nine `allow(unsafe_code)` lines) and re-read rustix 1.1.5's
`kill_process_group`, `waitid`/`WaitIdOptions::NOWAIT`, `pidfd_open`, `pidfd_send_signal` and
`Pid::from_child`. It corrected three things in the contract that would not have compiled or would
have left a hole: `MonitorError` derives `Copy`, which a `Bound(BoundExceeded)` variant cannot keep;
the list of conversions that must carry `Bound` by name had left out `From<PhpOpError>`; and the
status files do not share one arm shape. It also added: where this plan departs from the issue's
proposed fix, the exact import paths, the validate-and-reload seams that run outside the protocol,
which folders the part names in the task text actually mean, and a thread-and-task budget item. Each
correction is marked "fourth pass" where it lands.

A fifth pass, also before any code change, found that the branch had been rebased onto a `dev`
(`1eb8dde`) that moved under the note: `git diff 7e75b67 HEAD --stat -- agent installer proto`
now prints 167 files. Most of that is unrelated (reserved account names, the contract revision
window, the create-site race), but three changes land on this surface, and the note is corrected
for them. Each correction is marked "fifth pass" where it lands.

1. **The PHP install no longer spawns a package manager.** Since issue #93 the agent runs
   `systemctl start maran-php-install@<package>.service` and PID 1 runs `apt-get` or `dnf` in a
   unit that has its own `TimeoutStartSec=1800`. So the `PackageInstall` class describes a
   blocking `systemctl start`, its deadline must sit ABOVE the unit's, and the adapter's
   `package_manager` is executed by nothing in the agent any more, so it does not become an
   `AdapterBinary`.
2. **Managed accounts.** `CronHost`, `FtpsHost` and `LoginsHost` now take a `ManagedAccount` proof.
   That changes signatures, not the spawn surface; the seams parts keep those signatures as they
   are on `dev`. The `From` census moved its line numbers (ten conversions, 13 lines, both unchanged
   in number).
3. **The split of the `ops` seams is now three parts, not two.** `seams-a` (accounts, sites,
   safe_write, php, cron), `seams-b` (backup, firewall, monitor, db) and `seams-c` (every other
   `ops/src/` area: ftps, logins, sftp, ssl, files and the empty ftp). Where this note earlier said
   "seams-b" for the remainder, it now means seams-b or seams-c; the section "The change planned,
   by part" names which.

Counts re-run on `1eb8dde`: 10 `Command::new` sites, 13 `spawn_argv` callers in `ops`, 50
program-seam lines and nine `allow(unsafe_code)` lines (all unchanged); one production `"--"` (the
second hit is a test); `CommandOutcome {` is 91 lines (was 84, the difference is new test fakes).

A sixth pass, still before any code change, re-read the note against the nine rules files that bind
it and re-ran the two census commands on `1eb8dde`: 10 `Command::new` sites and 13 `spawn_argv`
callers, unchanged. It found nothing to correct. The worktree held only this note as an untracked
file (`git status --porcelain`), so no code had been edited.

A seventh pass (a restart of this stage) re-read the six rules files that bind it (README, git,
security, testing, architecture, rust) against the note and found nothing to correct. By then the
worktree already held Phase A code written by later stages from this note, which is the intended
order: the note predates it. The pass spot-checked that the contract's public names exist in that
code (`CallClass`, `BoundExceeded`, `SpawnError`, `SpawnRequest`, `spawn_argv` under
`agent-core/src/utils/`, and `AdapterBinary` in `distro/src/adapter_binary.rs`); it did not compile
or test anything. This pass does not review that code, and the second review stays OUTSTANDING.

**Second reviewer: OUTSTANDING.** Agent sessions do the work, and they cannot obtain a second
reviewer. The branch may carry the change. It MUST NOT merge to `main` until a human second
reviewer has read this note and the diff and recorded a verdict below it.

## Which part of this is a privileged surface

- **The spawn path of the only root process.** `maran-agent` is the one process in the product that
  runs as root (rules/architecture.md). Every program it starts runs as root unless it is started
  inside `fork_as_account`. Changing how those programs are started, waited for, signalled and read
  changes the privileged boundary itself. That is why the escalation rule applies even though no
  rpc, no privilege drop and no file path changes meaning.
- **`agent-core::privs::fork_as_account` is NOT changed on this branch.** None of the five parts
  owns `agent-core/src/privs/`. The module bounds its own child (`CHILD_PATIENCE`, 120 s) but
  signals only the child's pid, so whatever that child spawned survives the kill (see "Also
  found"). This branch works around that instead of fixing it (plan item 7). A diff that touches
  `privs/` has left this plan, and the reviewer reads that diff first.
- **`installer/systemd/maran-agent.service`.** Only the `SystemCallFilter=` and capability lines
  are in scope, and only if they need to change. The measurement below says they do not. If the
  branch edits them anyway, that edit is an installer privileged step, and this note covers it.

## What the code does today

This was read from the tree, not reproduced on a host. Regenerate the census with:

```
/usr/bin/grep -rnE 'Command::new\(' agent/crates --include=*.rs | /usr/bin/grep -v '/tests/'
```

That command prints **10** lines (re-run by the third pass). One is the shared
`agent-core/src/utils/spawn_argv.rs`, which 13 call sites in `ops` reach
(`/usr/bin/grep -rnE 'spawn_argv\(' agent/crates/ops --include=*.rs | /usr/bin/grep -v '/tests/'`).
The other nine are spawns kept beside their owners on purpose (rules/rust.md, "the rule of two"):

- the database client (`db/process_db_host.rs`);
- two in `cron/crontab_spool.rs`;
- two in `ftps/process_ftps_host.rs`;
- one each in `sftp/process_sftp_host.rs` and `ssl/process_ssl_host.rs` (stdin piped, because
  they carry passwords and private keys);
- two in `backup/process_backup_host.rs`: `load_dump`, and `run_archiver`, which runs inside a
  forked child for the account-side extraction and directly in the agent for the root-side one.

Positive control for the pattern: the same command matches the spawn in `spawn_argv.rs` itself.

None of them has the following:

- **A deadline, with one exception.** `spawn_argv` is `Command::output()`, which waits with no
  bound. The hand-kept spawns wait with `wait`, `wait_with_output` or `status`, which are also
  unbounded. Two of them bound their captured OUTPUT (the database client and `crontab -l`), but
  neither bounds TIME. **The exception, which the first pass of this note missed:**
  `FtpsHost::run_candidate` (`ftps/process_ftps_host.rs:185-230`) polls `try_wait` against a
  caller-supplied `deadline` and kills the child when the deadline passes. There, reaching the
  deadline is the SUCCESS case (`CandidateOutcome::StillRunning`: the daemon read the candidate,
  bound its socket and kept serving). It kills only the pid, with `Child::kill`, and it reads both
  pipes only after the child has exited or been killed.
- **A process group of its own.** Several children fork: `tar` runs its compressor as a child (on
  the write side, through `/bin/sh -c`), `apt-get` and `dnf` run maintainer scripts, and
  `systemctl` talks to PID 1. None of them can be killed together with its descendants, even if
  something wanted to kill them.
- **An output ceiling on the shared path.** `spawn_argv` reads both streams whole into `String`s.
  `ProcessBackupHost::list_members` then copies all of `tar --list` a second time, into a
  `Vec<String>`, and the archive it lists is one that a restore reads back.
- **A typed program.** Every seam takes the program as `&str`:
  - `SystemHost::run`, `ConfigHost::run`, `FirewallHost::run`, `LoginsHost::run`,
    `MonitorHost::run` and `SftpHost::run`;
  - `FtpsHost::run`, together with `run_candidate` and `run_with_stdin`;
  - the two `SslHost::run_with_*` methods and `SslHost::generate_material` (`ssl/ssl_host.rs:195`);
  - the cron spool's private `run` and `run_bounded`, and the database host's `run_client`;
  - the backup host's `String` fields `tar_binary`, `dump_binary`, `client_binary` and
    `gzip_binary`, and the free function `run_archiver`.

  Two more carry the program INTO a seam rather than being one: the `program: &'a str` fields of
  `safe_write::model::Validator` (`validator.rs:54`) and `Reload` (`reload.rs:12`), which
  `render_validate_swap` hands to `ConfigHost::run`. Typing the seam `&AdapterBinary` and leaving
  those two fields as `&str` would only move the hole one step back. This is more seams than the
  issue lists. Regenerate them with:

  ```
  /usr/bin/grep -rnE '(program|_binary): (&str|&.a str|String)' agent/crates/ops --include=*.rs \
    | /usr/bin/grep -v /tests/
  ```

  On `dev` this prints 50 lines, including `validator.rs:54` and `reload.rs:12`; those two are the
  positive control that the `&'a str` alternative matches. The hits on `*_error.rs` files are
  error fields that carry a program name for the operator log, and they are not seams. Today the
  allow-list exists only as a doc comment ("must come from the `DistroAdapter`"), and the compiler
  accepts any string.
- **End-of-options guards.** Exactly one call site passes `--`: `monitor/get_service_statuses.rs:24`.
  The command `/usr/bin/grep -rnE '"--"' agent/crates --include=*.rs | /usr/bin/grep -v /tests/`
  prints only that line, which also proves the pattern can hit.

## What an attacker can do with this surface today

Who can reach the surface: the agent serves one peer-credentialed unix socket, so every request
comes from the panel's uid. That leaves two actors. **A hosting customer** acts through panel
operations on their own account. **A compromised panel process** can call any rpc directly.
Someone who can already write root-owned files is out of scope, because that person already has
root.

1. **Freeze every configuration change on the host (cross-tenant availability).** The validator and
   the reload run *inside* `config_tree_lock`. `safe_write/render_validate_swap.rs:104` takes the
   lock, and the children at `:154` and `:169` run under it. `write_config_set.rs` and
   `remove_config.rs` have the same shape. If either child hangs, the lock is held until the agent
   restarts. From then on, every config change on the host queues behind it, for every tenant:
   site create, delete and suspend, PHP changes, certificate installs and renewals, and SFTP and
   FTPS changes. None of the causes needs an attacker: a hung mount, a stalled PID 1 job queue for
   `systemctl`, or a validator that is really a mutation. The FTPS and jail-mount trees pass
   `systemctl restart` or `daemon-reload` as their validator (`render_validate_swap.rs:49-53`).

   **Where the code contradicts the issue:** the issue's example cause is "`nginx -t` resolves every
   `proxy_pass` hostname at parse time". A customer cannot use that here. `Upstream`
   (`agent-core/src/validation/web/upstream.rs`) accepts only a loopback or RFC 1918 *address*, so
   no tenant-controlled value makes `nginx -t` perform a DNS lookup. The hang is still real, but it
   comes from the host and not from a tenant's input.

2. **Exhaust the blocking pool, so the whole agent stops answering.** Each waiter on the lock holds
   one `spawn_blocking` thread, and a dropped client future cancels nothing
   (`services/wire/run_blocking.rs`). `server.rs` builds tonic with no timeout and no concurrency
   limit (`Server::builder()`, near line 201). So retries and scheduler cycles park one thread
   after another until tokio's ceiling of 512 threads is reached. After that, every rpc that leaves
   the async workers waits, including reads that never touch the lock. A compromised panel reaches
   this directly, just by calling. A customer reaches it only through whatever retry behaviour the
   panel has.

3. **Park threads from inside your own database (customer-reachable, NOT reproduced).** The database
   client (`run_client`) and the restore's `drop_database` and `load_dump` all wait with no bound.
   A customer can hold a metadata lock from a connection of their own: an open transaction that has
   read a table is enough. A `DROP` aimed at that database then waits for MySQL's
   `lock_wait_timeout`, whose default is one year. Each panel operation that reaches such a
   statement parks one blocking thread for as long as that wait lasts. This path was found by
   reading the code and the server's documented defaults. It was not driven on a host.

4. **Make a root process hold a customer-sized amount of memory and CPU (customer-reachable).**
   `list_members` runs `tar --list` **as root** over an archive that a restore reads back. The
   customer controls that archive's contents (`backup/process_backup_host.rs` says so for the
   extraction half). Millions of members, or very long names, are held twice in the root daemon's
   memory. A gzip stream that expands enormously costs root CPU, and nothing bounds how long that
   takes.

5. **Run a program that did not come from the adapter.** **No path does this today.** Every program
   string in the tree comes from the adapter; the second pass checked that seam by seam. One input
   comes close: `php::reload_pool_trees` builds the php-fpm validator path from a version string
   that `list_php_versions` *enumerated from the host*, not from a `PhpVersion`. So the program
   path depends on what a directory listing returned. The exposure is a future regression: a
   contributor who threads a request-derived string into `run` gets a clean compile. This is an
   "allow-list by convention" finding, not an exploit, and it is reported as one.

6. **Smuggle an option through an operand.** **No path does this today either.** Operands are
   validated types: names start with a letter, domains are grammar-checked, and paths are absolute,
   so none of them can start with `-`. A `--` is a second wall behind validation, not a fix for
   anything reachable.

## Where this plan departs from the issue's proposed fix

The issue's measurements are the spec. Where the code or the rules disagree with its proposed fix,
this plan follows the code and the rules. Each departure is listed here so a reviewer does not have
to find it:

- **Truncation is an error, not a truncated outcome.** The issue proposes a reader that "records
  that it truncated" and a test that "returns a truncated outcome". This plan makes reaching the cap
  `BoundExceeded::OutputTooLarge` and returns no captured byte, because a shortened answer handed to
  a parser changes what it concludes ("What this change could itself break", items 3 and 15). The
  issue's closing test becomes: a child that prints more than the cap returns `OutputTooLarge`, and
  the daemon does not retain more than the cap.
- **The timeout is a typed error, not a case of the outcome.** The issue proposes that the outcome
  "gains a `TimedOut` case". Here `CommandOutcome` is unchanged and a timeout is
  `BoundExceeded::TimedOut` inside `SpawnError::Bound`. A caller that matches on `status` cannot
  read a kill as a refusal, because it never receives an outcome at all.
- **The `proxy_pass` DNS example is not reachable by a tenant.** See attack 1. The hang is real, and
  its cause is the host.
- **"Mapped to a gRPC status" means the in-band `AgentError`.** Every `*_status.rs` maps to
  `AgentError` inside the response, as rules/proto.md "Response shape" requires (plan item 11). No
  rpc gains a transport-level status for this.
- **The tonic concurrency limit is optional, not item 6 of the fix.** See plan item 12 and "What
  this change could itself break", item 10. The panel uses one HTTP/2 connection, so a
  per-connection limit is a panel-wide limit, and long-lived streams hold slots.
- **The "a `&str` cannot reach a seam" proof uses no new dependency.** A `compile_fail` doctest on
  `AdapterBinary` in the `distro` crate needs no `trybuild`. It is paired with a doctest that DOES
  compile (a real adapter's value reaching the same kind of call) as the inverse control, because a
  `compile_fail` doctest passes for any compile error at all. `scripts/lib/suite.sh:534` already
  parses `Doc-tests` lines, so the harness counts them. The test pass decides whether this or a
  `maran structure` check is the gate, and records the count in the baseline.

## The change planned, by part

The parts are written in parallel and owned by path. **Fourth pass: what the folder names in the
task text mean.** "databases" is `ops/src/db/`. "ftp" is `ops/src/ftp/`, which holds only a
zero-byte `.gitkeep` (rules/rust.md), so there is nothing to migrate there; the shipped FTP is
`ops/src/ftps/`, which is seams-c. "system" names no `ops/src/` folder. `SystemHost` is
`ops/src/accounts/system_host.rs`, so it belongs to seams-a. `ops/src/logins/` is seams-c. **Fifth
pass: the split is three parts.** seams-b is exactly `backup`, `db`, `firewall` and `monitor`.
seams-c is `ftps`, `logins`, `sftp`, `ssl`, `files` (which spawns nothing) and `ftp` (empty), and
anything else under `ops/src/` not named for seams-a or seams-b. Wherever the text below says
"seams-b" for `ftps`, `logins`, `sftp`, `ssl` or `files`, read seams-c; for `backup`, `db`,
`firewall` and `monitor` it is seams-b as written. Each part's tests live in the `src/tests/`
mirror of the modules it owns (rules/testing.md), and those mirrors belong to the same part. Some
shared test files belong to no part and are updated in the test pass:

- `ops/src/tests/support/recording_commands.rs`, which can stay as it is, because fakes call
  `record(program.as_str(), …)`;
- `agent/src/tests/**` (apart from the status-mapping tests);
- the polygon suites under `agent/tests/**`.

### spawn-core: `agent-core/src/utils/**`

1. **One bounded core, which `spawn_argv` and every hand-kept spawn use.** Each hand-kept spawn
   keeps what makes it different (stdin piped, a bounded read, a window that means success) and
   takes the wait from the core. The core guarantees the following for every child:
   - it is started with `process_group(0)`, so it leads a new group in the agent's session;
   - **stdin is set explicitly** to null, a pipe the core writes, or a file. It is NEVER inherited
     on the threaded path (item 2 below: `spawn()` inherits, which `output()` did not);
   - the environment is `apply_child_environment` and nothing else;
   - stdout and stderr are drained concurrently through readers that stop *retaining* at a cap.
     Reaching the cap is an ERROR (`BoundExceeded::OutputTooLarge`). The group is killed, and no
     captured byte is returned, so no caller ever parses a shortened answer;
   - the wait has a deadline. When it passes, the **whole group** gets `SIGKILL` while the leader
     is still unreaped (it is observed with `waitid(WNOWAIT)` or a pidfd). The leader is then
     reaped under a reap ceiling of its own, the same discipline as `fork_as_account`'s
     `REAP_PATIENCE`;
   - nothing that can block past *deadline + reap ceiling* is joined. A reader or stdin-writer
     thread that a detached descendant keeps blocked (item 8) is left to finish on its own and is
     not waited for. The core's doc comment says so;
   - **no new `unsafe` and no new third-party dependency.** Group kill and a non-reaping wait are
     syscalls that `std` does not offer, and `unsafe` lives only in `agent-core::privs`
     (rules/rust.md "unsafe"). `rustix` is already a workspace dependency, locked at 1.1.5, with
     its `process` feature on. That version exposes `kill_process_group`, `waitid` with
     `WaitIdOptions::NOWAIT`, `pidfd_open`, `pidfd_send_signal` and `Pid::from_child` as safe
     functions. The third pass re-read them in
     `~/.cargo/registry/src/*/rustix-1.1.5/src/process/{kill,wait,pidfd}.rs` and `src/pid.rs`. So
     the core belongs in `agent-core::utils` beside `spawn_argv`, not in `privs`.
2. **The caller states the deadline by naming a call class.** One global number is not used. The
   principle is that **a deadline means "wedged", never "slow"**. Every class sits well above the
   slowest legitimate run of its programs. Each value below is a pinned plan, and its reason goes
   into the doc comment of `CallClass::deadline`. The seams parts pick each call site's class from
   this list:

   - **`Query`, 60 s:** `getent`, `id`, `quota` reads, `passwd -S`, `systemctl is-active` and
     `show`, `nft list`, `openssl` reads of a certificate or a key, and `crontab -l`.
   - **`Validate`, 60 s:** the pure validators, `nginx -t` and `php-fpm -t`.
   - **`Reload`, 240 s:** every `systemctl` that starts, stops, restarts, reloads or runs
     `daemon-reload`, **including the four mutating validators**. A restart may legitimately take
     systemd's stop timeout plus its start timeout, 90 s + 90 s by default, and systemd's own
     verdict must arrive before ours.
   - **`Mutation`, 120 s:** `useradd`, `usermod`, `passwd -l`/`-u`, `chpasswd`, `setquota`,
     `quotaon`, `pkill`, non-recursive `chmod` and `chgrp`, the crontab install, `nft -f`, and
     `openssl` key generation.
   - **`Database`, 300 s:** every database-client statement (`run_client`). **As written (seams-b):**
     the backup area's restore `drop_database` is `Bulk`, not `Database`: it is the restore's point of
     no return, and a client killed at 300 s does not stop a drop the server has begun.
   - **`PackageInstall`, 2100 s:** the one `systemctl start maran-php-install@<package>.service`
     (**fifth pass: this was `apt-get install` and `dnf install` until issue #93 moved the package
     manager into a unit outside the agent's sandbox**). The unit carries `TimeoutStartSec=1800`,
     and `Type=oneshot` makes `systemctl start` block until the run is over, so systemd's verdict
     must arrive first: the agent's deadline is that value plus five minutes, never equal to it. A
     kill at the agent's deadline cancels nothing in PID 1 (concern 5), so it can no longer
     interrupt a `dpkg` half-way; it only stops the agent waiting.
   - **`Bulk`, 43 200 s (12 h):** `mysqldump`, `tar` create, list and extract as root, `load_dump`,
     **`userdel --remove`** (it removes a whole home), and any recursive `chgrp` or `chmod` over a
     home. *(Superseded in two places by "Independent review, third round" below: the `tar --list`
     pre-scan has its own `Listing` class of 3 600 s, and the account-side extraction of a restore
     runs under 100 s, not 12 h.)*

**As written (spawn-core, code stage).** The contract's public items are implemented as stated. The
points the code settled, none of which changes a signature:

- The core's own parts are PRIVATE modules of `utils` (`launch_child` is the workspace's single
  `Command::new`; `has_exited`, `kill_group_and_reap`, `supervise_child`, `start_reader`,
  `start_writer` and small helpers). `run_bounded` delegates to a private `run_until(request,
  deadline)` so the timeout path can be driven in milliseconds from the test module that sits
  beside it; `run_for_window` and `run_unthreaded` already take a `Duration`.
- **Policy for a leader that exited cleanly while a descendant still holds a pipe:** the drain sits
  under the same deadline. When it passes, the group is killed and the call is `TimedOut`.
- **A leader that survives `SIGKILL` for the 5 s reap ceiling** is left uncollected (one zombie
  until the agent exits) and the bound that caused the kill is reported as usual.
- **Input delivery:** any write error on the input pipe, a closed pipe included, is
  `SpawnError::Input`, which is what the hand-kept spawns did before. The writer thread owns a copy
  of the bytes and overwrites it after the write (best effort).
- **Signal number:** `CommandOutcome` is unchanged, so a child that died of a signal the core did
  not send reports `-1`. The database host's `ClientKilled { signal }` cannot be derived from it;
  that is a decision for the seams-b part and the test pass (see the test plan, item 4).
- Reader threads are one per captured stream, none for `Discard`, one writer for
  `ChildInput::Bytes`; nothing is joined past deadline + reap ceiling.

### adapter-binary: `distro/**`

3. **`AdapterBinary`** is a newtype in the `distro` crate with a private field and `pub(crate)`
   constructors only. Every adapter method that names a program the agent *executes*, directly or
   as the compressor `tar` runs, returns it by value. The methods are listed in the contract below.
   `sh_binary`, `nologin_shell`, the service and unit names, and every path stay `&str` or
   `String` (item 12).
4. **`php_fpm_binary` takes `&PhpVersion`.** The PHP validator path is then built only from a
   validated version, which closes the host-enumerated string in attack 5. This needs one new
   *internal* dependency edge: `maran-distro` depends on `maran-agent-core` by path.
   `maran-agent-core` depends on no `maran-*` crate, so the edge creates no cycle. It adds no
   third-party code, but it does change `agent/Cargo.lock`'s dependency list for `maran-distro`
   (reviewer check 13).

### seams-a: `ops/src/{accounts,sites,safe_write,php,cron}/**` — seams-b: `backup`, `db`, `firewall`, `monitor` — seams-c: every other `ops/src/` area

5. **Every seam listed in "What the code does today" takes `&AdapterBinary` and a `CallClass`**,
   with the exact signatures given in the contract. The bodies that are private to a process host,
   such as `run_client`, `run_bounded` and `run_archiver`, take `&Path`. They are reachable only
   from a host that holds an `AdapterBinary`, which is what lets a test point them at a stand-in
   without a public constructor (item 11). This also covers each area's existing named sentinel:
   the cron spool's `PROGRAM_UNAVAILABLE` and the backup area's `ARCHIVER_REFUSED_UNDER_ACCOUNT`
   keep their meanings, and a timeout reaches each area as its new `Bound` variant.
6. **The safe-write protocol returns the typed bound.** `ConfigHost::run` failing with
   `SafeWriteError::Bound` at the validator or the reload goes through the existing
   `finish_with_rollback`, the same path every other step failure takes. The target is restored,
   and `config_tree_lock` is released by the return. The longest a holder can keep the lock is then
   a stated sum: validator deadline + reload deadline + two reap ceilings. That is 60 + 240 + 2×5 s
   for a pure validator, and 240 + 240 + 2×5 s for a mutating one. `config_tree_lock`'s doc comment
   is corrected to state this sum and its residual (the holder's own I/O is still unbounded, see
   below).
7. **The account-side archiver keeps a group of its own, and its deadline is below
   `CHILD_PATIENCE`.** This resolves the second pass's item 16 for this branch. `fork_as_account` is
   not changed, so its outer kill still reaches only the forked child. The inner archiver is
   therefore started by the unthreaded core in a group of its own, with a deadline of 100 s,
   strictly below `CHILD_PATIENCE` (120 s) and leaving room for the 5 s reap. That makes the inner
   group kill the one that fires, and it takes `tar` and its compressor down together, which today
   nothing does. The constant is a private const in `backup/process_backup_host.rs`. Its doc comment
   names `CHILD_PATIENCE` and the reason, because the privs constant is private and cannot be
   referenced. The root-side extraction uses the `Bulk` deadline.
8. **`list_members` is bounded, and a hit refuses the archive.** It reads `tar --list` through the
   core with an explicit stdout cap, under the `Bulk` deadline (*superseded: the `Listing` class, 3 600 s,
   see the third review round*), and it refuses past a member-count
   bound and a name-length bound. Hitting any of the three refuses the whole archive as
   `BackupError::ArchiveFailed` or `BackupError::Bound`. It never returns the members read so far
   (item 15). The part states, in the doc comment of each bound, how that bound relates to what
   `CreateBackup` can produce (item 7).
9. **`--` before operands**, only for programs whose parser treats it as the end of options on
   *both* families, and only where an operand follows it. Each one is named in a comment with the
   source of that claim (item 13).
10. **The database statement gets a server-side bound where it can carry one** (item 6). The option
    seams-b has is a session `lock_wait_timeout` below the `Database` deadline, passed as a client
    option in argv and not as a second statement, because `run_client` refuses anything that is not
    a single statement. If it is not done, that is recorded as a residual, not claimed.

**As written (seams-c, code stage).** `ftps`, `sftp`, `ssl` and `logins` follow the contract and
change no signature in it. The points the code settled:

- Each of the four area errors implements `From<SpawnError>`, so a host `run` is `Ok(spawn_argv(..)?)`:
  a bound becomes the area's `Bound`, and `Start`, `Input` and `Wait` keep today's "could not run
  it" answer (`program_unavailable()`, or `ToolUnavailable` whose reason is the spawn error's text,
  which carries no io text for `Input`). The `From<SafeWriteError>` and `From<SitesOpError>` in
  `ssl_op_error.rs` carry `Bound` through by name.
- **The FTPS and SFTP `write_config` mappings have a `Bound` arm of their own.** The FTPS
  `other =>` arm and the SFTP catch-all to `JailFailed` would otherwise have turned a killed
  validator into a verdict. `ensure_account_jail` and the SFTP `ensure_jail` keep `Bound` apart from
  `JailFailed` for the same reason.
- **`run_candidate` goes through `run_for_window`** with a 64 KiB cap per stream
  (`CANDIDATE_OUTPUT_CAP`). A candidate that fills a pipe is `FtpsError::Bound`, not a daemon that
  is serving. Its argument vector gets no `--`: the daemon's own argument loop has none.
- **`--` guards:** `useradd`, `userdel`, `usermod`, `getent` (before the database name and the
  login) and every `systemctl` verb that names the FTPS or jail unit. `FtpsUnit`'s four vectors now
  end with `--` and the unit, so an index into them moved; the error sites name `AgentPaths::FTPS_UNIT`
  instead. **No guard** on `pkill` (its only operand is the argument of `--uid`), `chpasswd` (no
  operand), `openssl` (no operand) or `passwd -S`: the Debian `passwd` is a `getopt_long` program
  but the RHEL one is a `popt` program that was not measured, and one family answering differently
  is worse than no guard.
- **Classes:** `systemctl` restart, stop, disable and daemon-reload are `Reload`; `is-active` is
  `Query` (but `Reload` where the config-write protocol runs it, as in the inventory's F2);
  `useradd`, `userdel` (without `--remove`), `usermod`, `chpasswd`, `pkill` and key generation are
  `Mutation`; `getent`, `passwd -S` and the `openssl` reads are `Query`. The `ssl` host chooses
  `Query` and `Mutation` itself, as the contract says.
- The `ConfigHost::run` of `ProcessFtpsHost` and `ProcessSftpHost` repeat a five-line mapping of
  `SpawnError` to `SafeWriteError`. A shared `From<SpawnError> for SafeWriteError` in `safe_write`
  (seams-a's) would remove both copies; it is left to that part.

### server-limits: `agent/src/server.rs`, `agent/src/services/*/*_status.rs`, two unit lines

11. **The bound is mapped as a domain outcome, by name.** The task text says "mapped to a gRPC
    status". The code and rules/proto.md say domain outcomes travel as `AgentError` inside the
    response, and every area's `*_status.rs` maps that way today. So each status file gains an
    explicit `<Area>Error::Bound(_)` arm reaching `ErrorCode::SystemFailure`, placed ahead of the
    `_` wildcard, with `message: error.to_string()`. **Fourth pass: the arm takes each file's own
    shape, because the files do not share one.** Eight match to a bare `ErrorCode`
    (`account_status`, `backup_status`, `cron_status`, `db_status`, `ftps_status`,
    `logins_status`, `monitor_status`, `sftp_status`), so there the arm is
    `<Area>Error::Bound(_) => ErrorCode::SystemFailure`. Four match to a `(ErrorCode, String)` pair
    (`firewall_status`, `php_status`, `site_status`, `ssl_status`), so there it is
    `<Area>Error::Bound(_) => (ErrorCode::SystemFailure, String::new())`. Regenerate the split with
    `/usr/bin/grep -nE '_ => \(?ErrorCode' agent/crates/agent/src/services/*/*_status.rs`.
    `tool_output` stays empty: the program was killed, and what it printed is neither complete nor
    safe to forward. A dedicated
    `ErrorCode` would be an additive proto change. No part owns `proto/`, so that is left as an
    owner decision.
12. **tonic limits: optional, and no server-side `timeout`.** A server timeout drops the handler
    future, which frees nothing (the blocking work and the lock keep running), and it reports a
    failure for a write that then succeeds. rules/rust.md already names that as the panel-side
    hazard: "a call it can no longer hear is not a call that did not happen". A concurrency limit
    is added only if it meets reviewer check 11, with the justification written beside it.
    Otherwise the part leaves `server.rs` alone and says why.
13. **`SystemCallFilter=`: no change expected.** This was measured. Each supported family's polygon
    image expands `@system-service` (through `@process`, `@io-event`, `@ipc` and `@default`) to
    include `setpgid`, `kill`, `pidfd_open`, `pidfd_send_signal`, `wait4`, `waitid`, `ppoll`,
    `pipe2` and `execve`. The images checked were ubuntu22 (systemd 249), ubuntu24 (255), debian12
    (252), debian13 (257), alma9 (252), alma10 (257) and rocky9 (252). The negative control on the
    same probe found `quotactl`, `reboot` and `kexec_load` absent, which agrees with the unit's own
    comment about `quotactl`. `CAP_KILL` is already in the bounding set, and a root parent
    signalling its own root children needs no capability anyway. In the account-side extraction,
    the forked child signals its own child's group at the same uid, which needs none either.

    **The second pass re-measured, and states its method, because the obvious method is blind.**
    `systemd-analyze syscall-filter @system-service` prints the group's DIRECT members and the
    NAMES of the groups it includes. A flat grep of that output reports `kill`, `wait4`, `waitid`,
    `execve` and `pidfd_*` as ABSENT; the second pass saw exactly that on alma9 and debian12. The
    groups have to be expanded recursively. The re-run used `maran-install-polygon:alma9` (systemd
    252, 370 syscalls after expansion) and `maran-install-polygon:debian12` (systemd 252, 376), in
    throwaway `docker run --rm` containers, with a script that recurses into every `@group` line.
    `setpgid`, `kill`, `pidfd_open`, `pidfd_send_signal`, `wait4`, `waitid`, `execve`, `pipe2`,
    `ppoll`, `clone3` and `vfork` are all present. `clone3` and `vfork` matter because `std` may
    start a child through `posix_spawn`, and glibc implements that with them. The negative control
    still holds: `quotactl`, `reboot` and `kexec_load` are absent. The other five images were not
    re-measured in that pass, and the third pass ran nothing (it was a code-free stage).

**As written (seams-a).** No signature of the contract changed. Four facts a reviewer will look for:

- `--` is one `pub const END_OF_OPTIONS` in `agent-core::end_of_options`
  (`agent/crates/agent-core/src/end_of_options.rs`), imported by every area that adopts it. It
  was first written in `ops/src/accounts/` and later moved there; no area keeps a local copy.
  Check: `/usr/bin/grep -rn 'const END_OF_OPTIONS' agent/crates --include=*.rs` prints exactly one
  line, which is also the positive control.
- `--` is NOT added to `passwd -S` (a `popt` program on RHEL, unchecked there), to `crontab -u <a> -l`
  and `-r` (no operand follows), or to `nginx -t` and `php-fpm -t` (no operand). It is added to
  `useradd`, `userdel`, `usermod`, `id`, `getent`, `quota`, `quotaon`, `setquota`, `chgrp`, `chmod`,
  `crontab -u <a> <file>` and every `systemctl` of this branch.
- `reload_pool_trees` parses the enumerated version into a `PhpVersion` and reports a version that
  does not parse as `NotReloaded` without running anything.
- A `Bound` inside `SitesOpError`, `PhpOpError`, `DbError`, `SftpError`, `FtpsError`, `CronError` or
  `LoginsError` reaches `AccountError` as `AccountError::Bound`, including through the two
  observation conversions in `suspension_state`. The cron spool maps a stdout or stderr
  `OutputTooLarge` to `TABLE_TOO_LARGE` and keeps `Bound` for time only. Stderr over the ceiling is
  now an error, where the hand-kept drain kept a prefix.

## The contract between the parts

The parts are written at the same time, so each one compiles against the signatures below and
against nothing else from another part. A part that needs something not listed here stops and says
so. It does not invent the other side. Names, argument order and variant shapes are binding. Doc
comments and private helpers are each part's own business.

### From spawn-core (`maran_agent_core::utils::*`), one public item per file

**Fourth pass: import paths.** `agent-core/src/utils/mod.rs` holds `pub mod` lines and no
`pub use`, and `ops` imports every item by its full module path today
(`maran_agent_core::utils::spawn_argv::spawn_argv`). So every item below is reached as
`maran_agent_core::utils::<file stem>::<Item>`, for example
`maran_agent_core::utils::call_class::CallClass` and
`maran_agent_core::utils::bound_exceeded::BoundExceeded`. spawn-core adds one `pub mod` line per new
file and no re-export, and the other parts import by that path. The cron spool already has a
private METHOD named `run_bounded` (`cron/crontab_spool.rs:243`). A method does not clash with the
free function, but `crontab_spool.rs` should call the core by its path or under an alias rather than
import it bare beside a method of the same name.

```rust
// call_class.rs
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CallClass { Query, Validate, Reload, Mutation, Database, PackageInstall, Bulk }
impl CallClass {
    #[must_use]
    pub const fn deadline(self) -> std::time::Duration; // the table in plan item 2
}

// output_stream.rs — Display: "standard output" / "standard error"
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OutputStream { Stdout, Stderr }

// bound_exceeded.rs — the ONE error a bound produces; areas wrap exactly this type
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum BoundExceeded {
    TimedOut { program: String, deadline: std::time::Duration },
    OutputTooLarge { program: String, stream: OutputStream, cap: u64 },
}

// spawn_error.rs
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SpawnError {
    Start { program: String, reason: String },  // could not be started at all
    Input { program: String },                  // stdin could not be delivered (no io text: it may be a key)
    Wait { program: String, reason: String },   // could not be waited for
    #[error(transparent)]
    Bound(#[from] BoundExceeded),               // deadline or output cap; the group was killed
}

// child_input.rs
pub enum ChildInput<'a> { Null, Bytes(&'a [u8]), File(std::fs::File) }

// child_output.rs
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChildOutput { Capture { stdout_cap: u64, stderr_cap: u64 }, Discard }
impl ChildOutput {
    pub const DEFAULT_CAP: u64;   // 4 MiB per stream
    pub const DEFAULT: Self;      // Capture { DEFAULT_CAP, DEFAULT_CAP }
}

// spawn_request.rs
pub struct SpawnRequest<'a> {
    pub program: &'a std::path::Path,
    pub arguments: &'a [&'a str],
    pub class: CallClass,
    pub input: ChildInput<'a>,
    pub output: ChildOutput,
}

// run_bounded.rs — the core. Discard yields empty stdout/stderr. status is the exit code,
// or -1 for a signal the core did NOT send (CommandOutcome's contract is unchanged).
pub fn run_bounded(request: SpawnRequest<'_>) -> Result<CommandOutcome, SpawnError>;

// spawn_argv.rs — kept as the default path, now bounded: ChildInput::Null + ChildOutput::DEFAULT.
pub fn spawn_argv(
    program: &std::path::Path,
    arguments: &[&str],
    class: CallClass,
) -> Result<CommandOutcome, SpawnError>;

// window_outcome.rs + run_for_window.rs — run_candidate's inverted meaning: reaching the
// window is SUCCESS. Input is Null; on expiry the whole group is killed and Outlived returned.
pub enum WindowOutcome { Exited(CommandOutcome), Outlived }
pub fn run_for_window(
    program: &std::path::Path,
    arguments: &[&str],
    output: ChildOutput,
    window: std::time::Duration,
) -> Result<WindowOutcome, SpawnError>;

// run_unthreaded.rs — for a forked child (and the root-side extraction): starts NO thread,
// stdout and stderr are null, the child gets its own group, and the wait is polled.
// Returns the exit code, -1 for a signal the core did not send.
pub fn run_unthreaded(
    program: &std::path::Path,
    arguments: &[&str],
    stdin: std::process::Stdio,
    deadline: std::time::Duration,
) -> Result<i32, SpawnError>;
```

`CommandOutcome` (`agent-core/src/command_outcome.rs`) is unchanged and is not owned by any part.
Because a bound is an error, a `CommandOutcome` is always a complete answer, and none of its struct
literals has to change. Most of them are in test fakes:
`/usr/bin/grep -rn 'CommandOutcome {' agent/crates --include=*.rs` prints 84 lines, and that count
includes the definition and its `impl` line.

### From adapter-binary (`maran_distro::AdapterBinary`, re-exported at the crate root)

It is also reachable as `maran_distro::adapter_binary::AdapterBinary`, but the parts import the
root re-export.

```rust
// adapter_binary.rs
#[derive(Clone, PartialEq, Eq, Hash)]
pub struct AdapterBinary(/* private */ std::borrow::Cow<'static, str>);
impl AdapterBinary {
    pub(crate) const fn fixed(path: &'static str) -> Self;
    pub(crate) fn versioned(path: String) -> Self;     // php_fpm_binary only
    #[must_use] pub fn as_str(&self) -> &str;
    #[must_use] pub fn as_path(&self) -> &std::path::Path;
}
// Debug and Display print the path (a path is not a secret).
// AsRef<std::ffi::OsStr>, AsRef<std::path::Path>, AsRef<str>  — so Command::new(binary) works.
// PartialEq<str> and PartialEq<&str>                            — so tests can compare paths.
// NOTHING that builds one from outside the crate: no From, FromStr, Default, Deserialize,
// pub constructor, or cargo feature — in any build, test builds of other crates included.
```

**Fifth pass: `package_manager` is not in this list.** Nothing in the agent executes it any more
(`install_php_version` starts a unit through `service_manager`; the unit's program is rendered by
the installer). A type that says "the agent may execute this" is not put on a method whose value
nothing executes, so it stays `&'static str` like `sh_binary`.

These `DistroAdapter` methods change their return type to `AdapterBinary`: `nginx_binary`,
`service_manager`, `openssl_binary`, `tar_binary`, `gzip_binary`,
`database_dump_binary`, `mysql_client_binary`, `vsftpd_binary`, `useradd_binary`, `userdel_binary`,
`usermod_binary`, `pkill_binary`, `passwd_binary`, `getent_binary`, `setquota_binary`,
`quota_binary`, `quotaon_binary`, `id_binary`, `chmod_binary`, `chgrp_binary`, `chpasswd_binary`,
`crontab_binary` and `nft_binary`. One method also changes its argument:

```rust
fn php_fpm_binary(&self, version: &maran_agent_core::validation::web::php_version::PhpVersion)
    -> AdapterBinary;
```

`sh_binary`, `nologin_shell`, `php_fpm_service`, `php_fpm_pool_directory`, `php_package` and every
path and name method keep their current signatures.

### From seams-a, used by seams-b and server-limits

```rust
// ops/src/safe_write/config_host.rs — implemented by sites/php (a) AND sftp/ftps/ssl (b)
fn run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)
    -> Result<CommandOutcome, SafeWriteError>;

// ops/src/safe_write/model/validator.rs — constructed in sites/php (a) AND sftp/ftps/ssl (b)
pub struct Validator<'a> {
    pub program: &'a AdapterBinary,
    pub arguments: &'a [&'a str],
    pub class: CallClass,   // Validate for nginx -t / php-fpm -t; Reload for the four mutating ones
}
// ops/src/safe_write/model/reload.rs — the protocol always runs it as CallClass::Reload
pub struct Reload<'a> { pub program: &'a AdapterBinary, pub arguments: &'a [&'a str] }

// SafeWriteError gains:
Bound(BoundExceeded),
// Every other public signature in safe_write (write_config, write_config_set, remove_config)
// and in sites (write_vhost, render_vhost, delete_site, is_suspended_vhost, resolved_site_paths,
// ProcessSiteHost::new, RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT) keeps its current shape.
```

`SystemHost::run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)` stays inside
seams-a.

**Fourth pass: the validator and the reload also run OUTSIDE the protocol.**
`SiteMaintenanceHost::validate_and_reload` and `PhpHost::validate_and_reload` take a `Validator`
and a `Reload` and run them through `ConfigHost::run` with no write and no `config_tree_lock`. They
serve `sites::reload_web_server`, the startup reconciliation in `php::reload_pool_trees`, and
`ProcessSslHost`'s delegation at `ssl/process_ssl_host.rs:417`. Their trait signatures do not change.
Every implementation (`ProcessSiteHost`, seams-a; `ProcessPhpHost`, seams-a; `ProcessSslHost`,
seams-b, which delegates to its sites host) passes `validator.class` for the validator and
`CallClass::Reload` for the reload, exactly as the protocol does. A bound there is returned as the
area's `Bound`, never as `NginxValidation`, `PoolValidation` or `ReloadFailed`.

### The `Bound` variant in every area error — both seams parts, read by server-limits

Each of these enums gains exactly one variant, `Bound(BoundExceeded)`, a tuple variant whose
Display names the program and the bound. A `#[from] BoundExceeded` is allowed:

- seams-a: `AccountError`, `SitesOpError`, `SafeWriteError`, `PhpOpError`, `CronError`;
- seams-b: `BackupError`, `FirewallError`, `MonitorError`, `DbError`, `FtpsError`, `SftpError`,
  `SslOpError`, `LoginsError`.

`FilesOpError` gets none, because `files` spawns nothing.

**Fourth pass: `MonitorError` loses `Copy`.** It is the one area error that derives it
(`monitor/monitor_error.rs:25`, `#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]`),
and `BoundExceeded` carries a `String`, so `Bound(BoundExceeded)` does not compile beside `Copy`.
seams-b removes `Copy` from that derive and keeps `Clone`, `PartialEq` and `Eq`. Regenerate with
`/usr/bin/grep -rn 'derive(.*Copy.*thiserror' agent/crates --include=*.rs | /usr/bin/grep -v /tests/`,
which prints that one line and so is its own positive control. The enum's doc comment says that no
variant carries a program's output. That stays true: `Bound` carries the program's path and the
bound, and never a byte the program printed. Any place that moved a `MonitorError` twice relying on
`Copy` is fixed by the part that owns the file. `services/monitor/monitor_service.rs` hands the error
to `to_agent_error` by reference, so no such place is known in the agent crate. The other twelve
enums already derive nothing that `BoundExceeded` lacks: they derive at most `Debug`, `Clone`,
`PartialEq`, `Eq` and `thiserror::Error`.

Mapping rules that both seams parts follow:

- `SpawnError::Bound(b)` becomes `Area::Bound(b)`.
- `Start`, `Input` and `Wait` (and the `#[non_exhaustive]` rest) keep going to the area's existing
  "could not run it" variant (`SpawnFailed`, `CommandUnavailable`, `ToolUnavailable`,
  `program_unavailable()`, `ClientUnavailable`, and so on), so today's tests keep their meaning.
- Where an area already has a typed refusal for "too much output", it may keep it for
  `OutputTooLarge`, but never for `TimedOut`. Those refusals are the cron spool's `TABLE_TOO_LARGE`
  and the database host's `Unparsable`.
- Every `From<SafeWriteError>`, `From<SitesOpError>`, `From<PhpOpError>`, `From<SftpError>`,
  `From<FtpsError>` and `From<DbError>` conversion carries `Bound` through as `Bound`, by name.
  **Fourth pass: `From<PhpOpError>` was missing from this list**, although `PhpOpError` gains
  `Bound` and two areas convert from it (`account_error.rs:281` and `sites_op_error.rs:175`). The
  full set on `dev` is ten conversions: `ssl_op_error.rs:150` and `:174`, `php_op_error.rs:176`,
  `account_error.rs:319`, `:335`, `:350`, `:360` and `:375`, and `sites_op_error.rs:176` and
  `:227` (fifth pass: line numbers re-read on `1eb8dde`). Regenerate with
  `/usr/bin/grep -rn 'impl From<' agent/crates/ops/src --include=*.rs | /usr/bin/grep -v /tests/`,
  which prints 13 lines: those ten, plus `From<PrivError>` and two `From<PathError>`, which convert
  no spawn error. The rule also covers the FTPS mapping at `ftps/process_ftps_host.rs:156-166` (fifth pass: unchanged) and
  its `other =>` arm, and the PHP install's `.map_err(|e| PhpOpError::PackageManager
  { stderr: ... })` (fifth pass: now two sites, the unit start at `php/install_php_version.rs:137` and the later enable at `:166`, which maps to `ServiceEnable`). The account conversions flatten
  today (into `PoolRemoval`, `DatabaseRemoval`, `SftpRemoval`, `FtpsRemoval` and `SiteInspection`).
  A `Bound` inside them becomes `AccountError::Bound`, not one of those. Those variants say that the
  account still owns the resource, and a removal that was killed at its deadline has not
  established that: it may have finished on the host after the agent stopped waiting.
- A bound must never become `ValidationFailed`, `NginxValidation`, `PoolValidation`,
  `ServiceRefused`, `ReloadFailed`, `PackageManager`, `ConfigWrite` or any other variant that states
  a verdict on a configuration or a tool's answer (item 17).

server-limits maps `Bound(_)` explicitly in each of `account_status`, `backup_status`,
`cron_status`, `db_status`, `firewall_status`, `ftps_status`, `logins_status`, `monitor_status`,
`php_status`, `sftp_status`, `site_status` and `ssl_status` (plan item 11).

### The host seams after the change (each owned by the part that owns its area)

```rust
// seams-b
FirewallHost::run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)
LoginsHost::run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)
MonitorHost::run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)
FtpsHost::run(&self, program: &AdapterBinary, arguments: &[&str], class: CallClass)
FtpsHost::run_with_stdin(&self, program: &AdapterBinary, arguments: &[&str], stdin: &str,
                         class: CallClass)
FtpsHost::run_candidate(&self, program: &AdapterBinary, contents: &str, arguments: &[&str],
                        deadline: Duration)            // the window; unchanged meaning
SftpHost::run(&self, program: &AdapterBinary, arguments: &[&str], stdin: Option<&str>,
              class: CallClass)
SslHost::run_with_certificate(&self, program: &AdapterBinary, arguments: &[&str],
                              certificate_pem: &str)   // Query, chosen inside the host
SslHost::run_with_private_key(&self, program: &AdapterBinary, arguments: &[&str],
                              private_key_pem: &str)   // Query, chosen inside the host
SslHost::generate_material(&self, program: &AdapterBinary, request: &SelfSignedRequest)
                                                        // Mutation, chosen inside the host
ProcessBackupHost { tar_binary, gzip_binary, dump_binary, client_binary: AdapterBinary }
```

The return types are unchanged. Accounts (seams-a) consumes `LoginsHost`, `SftpHost`, `FtpsHost` and
`DbHost` (seams-b) only through these signatures and through the existing
`remove_account_sftp`/`remove_account_ftps`/`account_logins`/`drop_account_databases` functions,
whose signatures do not change.

## Why this makes the surface safe

- Attack 1 becomes a bounded delay. A hung validator or reload is killed together with its
  descendants, the target is rolled back, the lock is released, and the next tenant's write
  proceeds.
- Attack 2 loses its fuel. Every blocking thread now returns within its call's deadline, so the pool
  drains on its own instead of filling up until the agent restarts.
- Attack 3 is bounded on the agent's side by the `Database` deadline. It is **not** bounded on the
  server's side unless plan item 10 lands (reviewer check 6).
- Attack 4 is bounded in memory by the stdout cap and the count and length bounds, and in CPU and
  time by the deadline.
- Attack 5 becomes a compile error, and attack 6 gets a second wall. That is what a regression
  class needs.

**What stays open after the change.** These are residuals, not things the change claims to fix:

- **The lock holder's own I/O is still unbounded.** The deadlines bound the CHILDREN that run under
  `config_tree_lock`. They do not bound the agent's own `write`, `fsync` and `rename` of the
  temporary file inside the same critical section, and on a hung filesystem those block in the
  kernel with the lock held. So "each holder is short" becomes true for the validator and the
  reload, and stays conditional for the write. The corrected doc comment should say so and not
  promise an overall bound.
- **Host-side work that outlives the kill.** A queued systemd job (item 5), a server-side database
  statement (item 6) and a detached descendant (item 18) all survive the agent's kill. The change
  makes the agent stop waiting. It does not make the host stop working.
- **The deadline values themselves.** They are argued from the programs' documented behaviour, not
  measured under load (see "What the author could not verify"). A value set too low turns attack
  1's availability problem into rollbacks of writes that were valid.
- **`fork_as_account`'s own kill** still reaches only its child (see "Also found"). Plan item 7
  makes the inner kill fire first. It does not fix the outer one.
- **A reader thread held by a detached descendant is leaked, not joined** ("What this change could
  itself break", item 19). It is bounded in memory by its cap, and it is a task for as long as the
  descendant keeps the pipe open.

## What this change could itself break

This list is the main input for the reviewer. Each item is a way the fix could become a new defect.

1. **Signalling a recycled group.** `kill(-pgid)` sent as root after the leader has been reaped
   aims at a number the kernel may have given to someone else's group. The group must be signalled
   while the leader is still a zombie: observe the exit with `waitid(WNOWAIT)` or a pidfd, then
   signal, then reap.
2. **Inheriting the customer's archive as stdin.** During an account-side extraction, the daemon's
   fd 0 *is* the customer's archive (`STDIN_IS_THE_ARTIFACT`, `backup/process_backup_host.rs`).
   `Command::output()` gave every child a null stdin, but `Command::spawn()` inherits it. A bounded
   core built on `spawn()` that forgets `Stdio::null()` hands a customer's bytes to whichever root
   program another thread starts during that window. `run_unthreaded` takes its `Stdio` from the
   caller on purpose: its only callers are the extraction itself, inside the fork, and the
   root-side extraction with an opened file.
3. **Feeding a parser a shortened answer.** Truncation must be an error for every caller that
   parses stdout (`nft` listings, `getent`, `quota`, `systemctl` states). A capped string handed to
   a parser silently changes what it concludes. For example, a firewall rule read as absent is a
   rule that gets re-added or removed. The contract makes this structural: a capped stream is
   `BoundExceeded::OutputTooLarge`, never a `CommandOutcome`.
4. **Killing work that has no safe midpoint.** `RestoreBackup` and `InstallPhpVersion` run to
   completion by design (rules/rust.md "Async and blocking"). A deadline that fires on a *slow*
   restore load creates the half-applied state those classes exist to avoid, such as a
   half-loaded database. (Fifth pass: for the package install the kill lands on `systemctl start`
   and not on `dpkg`, so the interrupted-`dpkg` half-state is the unit's own `TimeoutStartSec`, not
   ours.) Their deadlines have to mean "wedged".
   A kill there must flow into the existing restore-recovery path, not around it. The same applies
   to `userdel --remove`, which is why it is `Bulk`: a removal killed half-way leaves a half-removed
   home, which the idempotent re-run of the deletion then has to finish.
5. **A killed `systemctl` does not cancel PID 1's job.** When a reload or a mutating validator
   (FTPS, jail mounts) times out, the file is rolled back on disk while the job systemd queued may
   still run, against either the old content or the new. The `Bound` Display or its doc must say
   "restored on disk; the service manager may still act". It must not claim a live rollback.
6. **A killed database client does not cancel the server's statement.** A `DROP` waiting on a
   metadata lock can stay queued on the server after the client dies, and a pending exclusive lock
   blocks the customer's own readers. The implementation should give the statement a server-side
   bound (plan item 10). If it does not, the reviewer should treat this as an open residual.
7. **A restore bound below what a backup can produce.** If `list_members` refuses N members while
   `CreateBackup` happily archives N+1, a customer's backup exists and can never be restored. The
   restore bound must be at least anything creation can produce, or creation must refuse at the
   same bound.
8. **The pipe outliving the leader.** A descendant that keeps stdout open makes the drain wait after
   the leader has exited. The drain must sit under the same deadline. A descendant that has
   *detached* (item 18) survives the group kill and can hold a pipe forever, so the core must not
   *join* a reader or a stdin writer past deadline + reap ceiling. The policy for a leader that
   exited cleanly while a descendant still holds the pipe must be stated in the core's doc comment,
   not discovered.
9. **Threads in a forked child.** `run_archiver` runs inside `fork_as_account`, after a fork of a
   multi-threaded process. The core it uses there must not start reader threads, which is why
   `run_unthreaded` exists: null output, and a polled wait only.
10. **tonic limits that become the new hang.** The panel opens ONE HTTP/2 connection
    (`Maran.Agent.Client/Channels/AgentChannel.cs`, default `SocketsHttpHandler`), so a
    per-connection limit is a panel-wide limit. Long-lived streams (`TailSiteLog` follow, backup and
    restore streams) hold slots for minutes or indefinitely. A limit at or below the number of
    streams that can be open at once freezes every other rpc. Add one only if it sits above that
    number and queues rather than sheds, with the justification written beside it.
11. **An escape hatch on `AdapterBinary`.** `ops` tests need stand-in programs. A test-only
    constructor behind a cargo feature is the obvious answer, and features unify. The contract
    therefore gives NO constructor outside `distro`, in any build. Tests use a real adapter's value,
    or they drive the private `&Path` body (plan item 5), or they test the core directly. That
    includes `From<&str>`, `FromStr`, `Default`, `Deserialize`, a `pub` constructor, and a feature
    that production builds turn on.
12. **The wrong things typed as executable.** `sh_binary` and `nologin_shell` are paths *written*
    into a crontab and a passwd entry, and the agent never spawns them. They must stay `&str`.
    Typing them `AdapterBinary` would make a shell something a seam accepts. `php_fpm_binary` takes
    `&PhpVersion` (plan item 4), so the one `AdapterBinary` built with `format!` is exactly as
    closed as the version grammar.
13. **`--` given to a parser that does not know it.** A program whose parser does not accept `--`
    reads it as an operand or refuses to start. On a validator, that turns *every* config write into
    a rollback. `nginx -t` and `php-fpm -t` have no operands at all, and `vsftpd` has its own
    argument parser. Neither gets a `--` without a measurement on both families.
14. **Doc comments that still describe the old behaviour.** These all change meaning with this
    change: `spawn_argv`'s comment ("waits for it"), `CommandOutcome`'s `-1` contract (still true,
    but now only for a signal the core did not send), `config_tree_lock`'s "each holder is short",
    and the rules/rust.md lines that describe `spawn_argv.rs`.
15. **A truncated member list that the pre-scan reads as the whole archive.** Item 3 is worse for
    `list_members` than for any other caller, because its output is security evidence and not just
    data. `backup/archive/scan_members.rs` is "the brace": it decides on the WHOLE listing and
    refuses the archive if one member is outside the layout or carries `..`. Root then extracts the
    archive. If a listing cut at a cap reaches the scan as complete, a hostile member placed after
    the cap gets past the scan, and only `tar`'s own stripping, "the belt", remains. Hitting the
    bound must refuse the archive. It must never return the members read so far.
16. **Where the archiver's group is.** Today the account-side `tar` is the forked child's child, and
    both are in the agent's process group, so no group kill can address them. Plan item 7 gives
    `tar` a group of its own, inside the fork, with a deadline below `CHILD_PATIENCE`. The cost of
    that choice: a later change that makes `fork_as_account` kill its *child's* group would not
    reach `tar`. That change is not on this branch, and whoever makes it has to kill the inner group
    too, or make the forked child the group leader and stop giving `tar` a group of its own. The
    archiver's doc comment says this.
17. **A timeout reported as a verdict on the configuration.** rules/proto.md defines
    `ERROR_CODE_VALIDATION_FAILED` as "rendered config failed its validator; state rolled back", and
    `sites/site_status.rs` sends `NginxValidation` there. A timed-out validator has said nothing
    about the configuration. If the timeout is folded into `ValidationFailed` (for example as
    `ValidationFailed { stderr: "timed out" }`), the panel tells an administrator that their config
    is wrong when it is the host that is wedged. The same goes for the FTPS mapping
    (`ftps/process_ftps_host.rs:156-160`), which turns `ValidationFailed | ReloadFailed` into
    `ServiceRefused`. The timeout has to be its own variant (`Bound`) and reach `SYSTEM_FAILURE`.
18. **Descendants that leave the group.** A descendant that calls `setsid` or `setpgid` for itself
    (for example, a daemonising helper in a maintainer script) is outside the group, and
    `kill(-pgid)` misses it. Only the unit's cgroup contains everything (`KillMode=control-group`,
    `maran-agent.service:130`), and that applies only when the agent stops. Services started
    through `systemctl` run in PID 1's cgroups and are not the agent's descendants at all. The
    group kill is complete for programs that do not detach, and it should be described that way,
    not as "kills everything the child started".
19. **Reader threads the daemon did not have before (fourth pass).** `Command::output()` drains
    both pipes on the calling thread and starts no thread of its own. A core that drains them
    with one reader thread per stream, plus a writer thread for `ChildInput::Bytes`, adds up to
    three tasks per spawn to the agent's cgroup. Those count against the unit's task limit. The
    unit sets no `TasksMax=` (`/usr/bin/grep -n TasksMax installer/systemd/maran-agent.service`
    prints nothing, while the same command with `KillMode` prints line 130), so systemd's
    `DefaultTasksMax=` applies. systemd documents that default as 15% of the smallest of
    `kernel.pid_max`, `kernel.threads-max` and the root cgroup's `pids.max`, which is 4915 on a
    host whose `pid_max` is 32768. That was read in the documentation and not measured here. At
    attack 2's ceiling of 512 parked blocking threads, each running a spawn with three helpers and
    a child, the agent holds about 2,560 tasks before counting any grandchild. That is under the
    limit, but not far under it. Item 8 makes it worse: a reader that a detached descendant keeps
    blocked is NOT joined, so it outlives its call, and one such thread is leaked for as long as the
    descendant holds the pipe. It retains nothing past its cap, but it is still a task. So the core
    starts as few threads as it can: at most one per captured stream, none for
    `ChildOutput::Discard`, and a writer only for `ChildInput::Bytes`. Its doc comment states the
    leak as a residual and does not claim that every thread ends at the deadline. A pure `poll`
    drain on the calling thread would remove the readers altogether. It needs rustix's `event`
    feature, which the workspace does not turn on today
    (`rustix = { version = "1.1.4", features = ["process", "thread", "fs"] }`). Turning it on adds
    no crate, but it changes `agent/Cargo.toml`, which reviewer check 13 does not expect. If the core
    takes that route, its doc comment and the hand-off say so.

## What a second reviewer must check

1. Run the census command above against the branch and confirm that every one of the ten
   `Command::new` sites reaches the bounded core. Any site left out must say in its doc comment why
   it is exempt.
2. Items 1, 2, 8 and 9 above, in the core itself: the signal order relative to the reap, an explicit
   stdin on the threaded path, no join past deadline + reap ceiling, and no threads in
   `run_unthreaded`.
3. That the timed-out test kills **a grandchild** too, and that the same test fails on today's tree.
   It hangs there, so it runs under an outer timeout. This is the inverse control the issue asks
   for.
4. That a second write by another tenant completes right after a timed-out one, which proves the
   lock was released.
5. That no parser is ever handed a truncated stdout. Read each caller of `run_bounded` and
   `spawn_argv`.
6. Each `CallClass` deadline against items 4, 5 and 6 above, and each call site's class against the
   table in plan item 2. In particular, check the `Bulk` and `PackageInstall` classes, the class of
   `userdel --remove`, and whether the database statements carry a server-side bound (plan
   item 10).
7. That `list_members`'s bounds are at least what `CreateBackup` can produce (item 7).
8. `AdapterBinary`: no construction path outside `distro` in any build (item 11). Check
   `cargo tree -e features -p maran-agent` on a non-test build. Also check that `sh_binary`,
   `nologin_shell` and the unit names are still not typed as executable (item 12), and that the
   compile-fail or structure check proving that a `&str` cannot reach a seam really refuses a
   planted violation (positive control).
9. Every `--` added, against item 13. Each one should be backed by a polygon run on both families.
10. Every `*_status.rs` named in the contract: it maps `Bound(_)` by name, ahead of its
    `_ => SystemFailure` wildcard. The wildcard would compile a missing arm silently, so check the
    arm is present, not merely that the outcome is right. Also check that the message does not
    claim a live rollback (item 5).
11. If tonic limits were added: item 10, with the number and its justification.
12. That `agent-core/src/privs/` is unchanged (`git diff dev -- agent/crates/agent-core/src/privs`
    prints nothing). If it was changed anyway, check the signal-before-reap order of any group kill,
    and that the privilege-drop order (`setgroups` → `setgid` → `setuid`, then verify) is untouched.
13. That the core adds no `unsafe` outside `agent-core::privs`, no `#[allow(unsafe_code)]`, and no
    third-party crate. `/usr/bin/grep -rn 'allow(unsafe_code)' agent/crates --include=*.rs` should
    still print the nine lines it prints on `dev`, all in `agent-core/src/privs/mod.rs` (eight
    attributes and one module doc line, `:49`); re-counted by the third pass. In
    `git diff dev -- agent/Cargo.toml agent/Cargo.lock agent/crates/*/Cargo.toml`, the only expected
    change is the internal `maran-distro → maran-agent-core` path edge (plan item 4). Anything else
    has left the plan.
14. Item 15: that `list_members` refuses at its bound, and that a test plants a hostile member AFTER
    the bound and sees the archive refused. A test that plants it only before the bound passes
    whether or not a truncated list reaches the scan.
15. Item 16 and plan item 7: the archiver's group, and that its deadline is strictly below
    `CHILD_PATIENCE` with room for the reap.
16. Item 17: that a bound never becomes `ValidationFailed`, `NginxValidation`, `PoolValidation`,
    `ServiceRefused`, `PackageManager` or any other variant that states a verdict. Walk every
    `From` conversion the contract names.
17. `run_candidate`: that the window still means "still serving, accepted", and that the kill is
    now a group kill.
18. The seams in "What the code does today", including `generate_material`, `run_archiver` and the
    `Validator`/`Reload` `program` fields. Re-run the regeneration command given there on the
    branch. Every remaining hit should be an error field, or a private `&Path` body reachable only
    from a host holding an `AdapterBinary`.
19. The contract itself: that each part's public signatures match the section above, and that
    nothing outside it was relied on across parts. A part that widened another part's API without
    saying so is the place a hole hides.
20. Fourth-pass items: that `MonitorError` lost `Copy` and nothing else in its derive changed; that
    all ten `From` conversions named in the contract carry `Bound` by name, `From<PhpOpError>`
    included; that the three `validate_and_reload` implementations pass `validator.class` and
    `CallClass::Reload`; and that the core's thread count per spawn matches "What this change could
    itself break", item 19, with the leak stated in its doc comment.

## Also found while reading (for the owner, and possibly outside this branch)

- **`fork_as_account` kills its child, not its child's group.** `give_up_on_with` sends `SIGKILL` to
  the pid ("cannot address a process group", by design). In the account-side extraction, that child
  is the parent of `tar`, and `tar` is the parent of the compressor. A child killed at
  `CHILD_PATIENCE` therefore leaves `tar` running at the customer's uid, still writing into the
  restore's staging tree, while the agent reports that the extraction failed and moves on. Plan
  item 7 makes the inner kill fire first on this branch. The outer kill is unchanged.
- **`CHILD_PATIENCE` is 120 s for every `fork_as_account` call, including that extraction.** From
  the code, an account-side extraction that legitimately takes longer than two minutes will be
  killed and reported as `ExtractionIdentityUnavailable`. With plan item 7 it is killed at 100 s
  instead, and reported as the bound. That was read, not measured. It needs a run with a large home
  before anyone acts on it, and the real fix is a separate patience for that call.

## What the author could not verify

- **No second reviewer.** This is an agent session.
- **That the contract compiles.** It was written before any code, from reading the definitions it
  names (`spawn_argv`, `CommandOutcome`, `apply_child_environment`, the `DistroAdapter` trait, every
  host trait, `SafeWriteError` and the protocol's `finish_with_rollback` path, the status files, and
  rustix 1.1.5's process module). The verification phase builds it. Until then, the signatures are
  a plan.
- **Any hang on a real booted host.** The polygon images were used only to expand the seccomp
  groups with `systemd-analyze`. UNOBSERVED HERE: the agent running under its real unit with the
  filter applied. The bounded spawn's syscalls should be confirmed by the polygon suites that run
  the agent, and on aarch64, which was not checked at all.
- **Which programs accept `--`** on each family. No claim is made for any of them until measured.
- **The database metadata-lock path** (attack 3) and **the 120 s extraction ceiling**. Both come
  from reading the code and documented defaults, and neither was reproduced.
- **What the deadline numbers should be on a slow, loaded host.** They are argued, not measured.
- **The panel's reaction to the new outcome.** The backend maps `SYSTEM_FAILURE` today. Whether any
  scheduler retries it, and how fast, decides how much of attack 2 the panel can still generate.
  The backend is outside this branch.
- **Rules edits.** These need the owner's approval, and this note makes none of them:
  - rules/rust.md's canonical layout needs a place for the new `agent-core/src/utils/` files and
    for `distro/src/adapter_binary.rs`;
  - its description of `spawn_argv.rs` and of the hand-kept spawns ("the rule of two") changes
    meaning;
  - the new `maran-distro → maran-agent-core` edge is worth a sentence in it.

  A further discrepancy was found and is reported, not fixed. rules/rust.md "Service anatomy" says
  `<area>_status.rs` holds "the single `From<XOpError> for tonic::Status` mapping". Every status
  file in `agent/crates/agent/src/services/*/` maps to the in-band `AgentError` instead (for
  example `sites/site_status.rs`, `to_agent_error`), which is what rules/proto.md "Response shape"
  requires. The code agrees with proto.md, so the rust.md sentence is the stale one. Correcting it
  is a rules edit, and that needs the owner.
- **Whether `run_candidate`'s vsftpd stays one process.** Reviewer check 17 assumes that the
  candidate daemon, started with the staged config, does not fork before the window ends when no
  client connects. That was read from how the method uses it, not measured.
- **The task budget** (item 19 of "What this change could itself break"). `DefaultTasksMax=` and
  its 15% rule come from systemd's documentation. Neither the value on a supported host nor the
  agent's task count under load was measured.
- **The test-side reach of the `AdapterBinary` change.** No production file in `agent/src` calls
  an adapter's binary method, but the polygon suites and fixtures under `agent/tests/` and
  `agent/src/tests/server_tests.rs` do, for example `Command::new(distro.crontab_binary())` and
  `distro.php_fpm_binary("8.3")`. They belong to the test pass. `Command::new` keeps compiling
  through `AsRef<OsStr>`. Comparisons against a `String`, `&'static str` fields and
  `php_fpm_binary("8.3")` do not. The census is 59 lines, all of them test code:

  ```
  /usr/bin/grep -rnE '\.(nginx_binary|service_manager|openssl_binary|package_manager|tar_binary|gzip_binary|database_dump_binary|mysql_client_binary|vsftpd_binary|useradd_binary|userdel_binary|usermod_binary|pkill_binary|passwd_binary|getent_binary|setquota_binary|quota_binary|quotaon_binary|id_binary|chmod_binary|chgrp_binary|chpasswd_binary|crontab_binary|nft_binary|php_fpm_binary)\(' agent/crates/agent --include=*.rs
  ```

  Adding `| /usr/bin/grep -v /tests/` leaves nothing, which is the "no production call site" half.
  The unfiltered 59 are the positive control that the alternation can hit.
- **Five of the seven seccomp expansions.** The second pass re-measured alma9 and debian12 only
  (plan item 13). The first pass's figures for ubuntu22, ubuntu24, debian13, alma10 and rocky9 are
  carried forward unverified, and the first pass did not record its expansion method.

## Independent agent review (Phase A, code only)

Five review lenses read the Phase A diff: correctness, security, rules, compatibility and design.
These are agent reviews. They are **not** the human second review, which stays OUTSTANDING (last
section). Nothing had been compiled or tested when they ran, so every "yes" below is a reading of
the code and not a measurement. Fixers were applying the findings while this section was written, so
it records the decision taken on each blocking or major finding, not the diff.

Verdicts: correctness approve, security approve, rules **changes required**, compatibility **changes
required**, design **changes required**. None of the five closes issue #90.

### Security-lens checklist (rules/security.md, run verbatim)

| # | Item | Result | Note |
|---|---|---|---|
| 1 | Input validated at the boundary | yes | No new external input. `php_fpm_binary` takes the validated `PhpVersion`, which closes the one `format!`-built path. |
| 2 | Paths: descriptor walk | n/a | No path-write logic changed. Account-side extraction still runs inside `fork_as_account`; its inner 100 s kill fires before the fork's own patience. |
| 3 | Shell: argv only, allow-listed binaries | yes | `launch_child` is the only production `Command::new` in the workspace: `/usr/bin/grep -rnE 'Command::new\(' agent/crates --include=*.rs | /usr/bin/grep -v '/tests/'` prints `launch_child.rs:36` as its one code hit (it is also the positive control), plus two doc-comment mentions in `distro/src/adapter_binary.rs`. No test enforces this workspace-wide; `run_unthreaded_tests.rs` scans only the core's own files, so the grep is the check. Environment cleared to `PATH` and `LC_ALL`. `--` guards added where the binary accepts them. Stated limit: `spawn_argv` and `run_bounded` accept any `&Path`, because agent-core cannot depend on distro. The `AdapterBinary` guarantee is therefore enforced at the seams by convention, as this note says. |
| 4 | Config-file injection | n/a | No new config writers. |
| 5 | SQL parameterized | n/a | No SQL in C# touched. Database statements still travel on stdin, never argv. |
| 6 | AuthZ | n/a | Agent side only. No endpoint changed. |
| 7 | Configuration documented, no secrets | yes | No new variable. No secrets in the diff. |
| 8 | Secrets and customer-facing errors | yes | `ChildInput` Debug never prints bytes; the writer and the database host zero their copies. `SpawnError` and `BoundExceeded` carry the program path and the deadline or cap, never argv or output. Every status mapper maps `Bound` to `SystemFailure` with empty tool output. A cap overflow returns no bytes. |
| 9 | Crypto | n/a | No change. |
| 10 | Surface | yes | No port, daemon or outbound call. Tonic limits deliberately not added (justification in `server.rs`). The unit's syscall filter is unchanged; `CAP_KILL` is only documented. |
| 11 | Dependencies | yes | One internal path dependency (distro to agent-core), no cycle. `Cargo.lock` was edited by hand; Phase B must confirm cargo writes the identical diff. |
| 12 | Agent: closed, typed, audited | yes | No new rpc. Timeouts surface as the typed `Bound` error. |
| 13 | Module reach declared | n/a | No new agent service or `AgentCapability`. |
| Escalation | Threat note first, second review OUTSTANDING | yes | This file; contract spot-checked against the code with no divergence found. The branch must not merge to `main`. |

### Blocking and major findings, and the decision on each

No lens raised a blocking finding. Six major findings, from three lenses, with the decision on each.

1. **`ConfigHost::run` and the `SpawnError` to `SafeWriteError` mapping are written four times**
   (sites, php, ftps, sftp). Raised by rules (major), design (major) and correctness (minor).
   Decision: fix in this branch. One conversion lives in `ops/safe_write`, one item per file, and the
   four copies are deleted so a fifth cannot drop the `Bound` arm. The program label cannot come from
   `SpawnError`, so a constructor taking the program is acceptable in place of a bare `From`.
2. **`END_OF_OPTIONS` was defined again in firewall, backup and monitor, and lived in `accounts/`**
   (rules, design). Resolved: one definition remains, in `agent-core::end_of_options`, and
   every area imports it from there. No local copy is left and no owner decision is open.

4. **The folder maps and `agent/CLAUDE.md` now say false things** (rules, major; design, minor).
   They describe `spawn_argv` as the one plain spawn body and the stdin-piping spawns as hand-kept,
   and the map lists none of the new `utils/` files, `distro/src/adapter_binary.rs` or the shared
   constant. Decision: `rules/rust.md` (utils and distro rows, the allow-list sentence near line 686,
   the rule-of-two example) needs the owner's approval and is **not edited by this branch**. The
   owner is asked to approve it in this branch or a follow-up. `agent/CLAUDE.md` is not a rules file
   and is corrected in the branch. Until then both documents are known to be stale.
5. **`list_members` can refuse a backup the panel created before the upgrade** (compatibility,
   major). It refuses above 2,000,000 members or 256 MiB of listing, while `CreateBackup` bounds only
   archive bytes (64 GiB), so a large maildir home could become unrestorable. Decision: fix in this
   branch, so that restore never refuses what creation can write: the caps are raised or creation
   gets a matching limit, and a restore test lists a large archive. Whichever figure is chosen is
   recorded here after Phase B. Regardless of the fix, the residual is: a backup taken before the
   upgrade that exceeds the caps is refused with `Bound`, and the operator must be told.
6. **`process_backup_host.rs` grew from 547 to 807 lines** against a 400-line review trigger
   (design, major). Decision: fix in this branch. `list_members` with its limits moves to its own
   unit under `backup/`, the discard-spawn helper and `bound_or` move out if shared, and
   `ProcessBackupHost` stays the thin trait impl.

### Minor findings, and the decision on each

- **Signal number lost** (correctness, security, compatibility, design). `CommandOutcome` reports
  -1 for a signal the core did not send, so `DbError::ClientKilled` now carries an unknown signal and
  `process_db_host_tests.rs` (expects 9) is wrong. Decision: **owner decision.** Either accept the
  loss and change the test, or give the core a signal-aware outcome (additive). Settled in the test
  pass. The wire contract is unchanged (`Bound` maps to the existing `SystemFailure`).
- **`userdel --remove` runs under `Bulk` (12 h) while `account_lock` is held**, also in the
  `undo_creation` rollback. A wedged `userdel` would block every account operation for hours.
  Decision: use a shorter class for the rollback `userdel`; and state the worst-case lock hold in the
  lock's doc comment either way, as is done for `config_tree_lock`.
- **Unmeasured argv additions:** `quotaon -p --`, `setquota -u --`, `quota`, `getent`, `id`,
  `crontab -u user -- file`, `chmod`/`chgrp --`, `nft -j -- list set`, and
  `mysql --init-command=SET SESSION lock_wait_timeout=240`. They rest on parser documentation. A
  rejecting tool fails closed, so this is an availability risk, but the mysql option would refuse
  every database statement. Decision: **no merge on reading alone.** Phase B runs the account, quota,
  cron, firewall, ftps, sftp, db and backup polygon suites on ubuntu22, ubuntu24, debian12, debian13,
  alma9 and alma10 (rocky where available). If a tool rejects `--` the guard is dropped for that
  family; if mysql rejects the option the `SET` moves to the first line of stdin.
- **A stdin write error is always `SpawnError::Input`**, so a child that exits early (`openssl`,
  `chpasswd`) can be reported as unavailable instead of with its own status. Decision: treat `EPIPE`
  from the writer as benign once the leader has exited so the exit status decides; the test pass adds
  the case.
- **`list_members` is bounded, not streamed.** Peak memory is about two to three times the 256 MiB
  cap (reader buffer, lossy copy, member strings), the unit sets no `MemoryMax`, and the listing runs
  under the 12 h `Bulk` deadline. Decision: lower the cap or parse from the byte buffer without the
  extra copy. The residual is carried below. This note does not claim streaming.
- **A reload that times out** rolls the files back, but the running service may already have loaded
  the new configuration, and nothing reloads again. The lock can be held for up to 310 s (490 s with
  a mutating validator). Decision: accepted as the existing `ReloadFailed` behaviour, now reachable by
  timeout, and stated as a residual below. No second reload is added.
- **`PackageInstall` (2100 s) is consistent only if the install unit's `TimeoutStartSec` is lower.**
  Decision: Phase B checks the unit and adds a test pinning the relation. If the unit allows longer,
  the agent gives up while the install continues and reports `Bound`, as its doc already says.
- **The systemd comment states a measurement that does not exist yet** (rules, compatibility): it
  says the syscalls are in `@system-service` on every measured image, while scout items M1 to M4 are
  not taken. Decision: reword to "expected, to be verified", and let Phase B run `systemd-analyze
  verify` and a deadline-kill spawn under the real unit on Debian and RHEL, with a negative control.
  aarch64 was not checked at all.
- **`Caps` is a second type in `supervise_child.rs`** (298 lines). Decision: move it to its own file.
- **`login_locked` has no doc comment** although its body was edited. Decision: add one, and check
  the other private methods in those impl blocks.
- **`Cargo.lock` was edited by hand.** Decision: Phase B lets cargo write it and confirms the diff
  is identical.
- **Many `From<SpawnError>` impls and `bound_or` helpers** across the sftp, ftps, logins, ssl, db,
  backup, cron, firewall and monitor error types (design). Decision: accepted, because each area error
  owns its mapping and a shared helper would couple the areas; the non-`Bound` arm differs per area.
  Whoever adds an area error must carry the `Bound` arm by name.
- **Process gap.** Server-limits and parts of seams-b and seams-c read only a head of the rules and
  not rust.md or testing.md, and the compatibility and security lenses did not read every rules file
  either. No violation traced to it, but the duplication findings above got through. Recorded for the
  orchestrator, no code change.

### Residuals this review adds

- The restored configuration after a timed-out reload is on disk while the service state is unknown.
- `list_members` holds a multiple of its cap in memory and runs under a 12 h class.
- A leader that survives `SIGKILL` for the 5 s reap ceiling is left as a zombie, and a descendant that
  left the process group can hold a reader thread past the call.
- Backups written BEFORE this change whose listing exceeds 2,000,000 members or 256 MiB are refused
  at restore with `Bound`/`ArchiveFailed(LISTING_REFUSED)`. From this change on, `create_archive`
  lists the archive it has just written with the same bounds and fails the backup if restore would
  refuse it, so no new backup is unrestorable for its size. The extra listing costs one more pass
  over the archive on creation. The operator must be told about older oversize backups; a restore
  test that lists a large archive belongs to the test pass.
- Signal number: the owner decision is taken as ACCEPT. `CommandOutcome` is built by struct literal in
  many tests and adding a field is not additive for them, so `DbError::ClientKilled` carries signal 0
  (unknown) for a client killed from outside; `process_db_host_tests.rs` (two assertions of signal 9)
  changes to 0 in the test pass.
- `rules/rust.md` and `agent/CLAUDE.md` are stale until the owner approves the edits.
- Not compiled, not tested, polygon suites not run, the unit not verified under the real sandbox.

### Second agent review round (Phase A, after the fixers of the first round)

The same five lenses read the diff again. Still agent reviews, still not the human second review,
still nothing compiled or run. Verdicts: correctness **approve**, security **approve**, compatibility
**approve**, rules **changes required**, design **changes required**. No lens raised a blocking
finding and none closes issue #90 on its own except correctness, which says the issue's measured
defects are closed by the code as read. Phase B has to confirm that.

**Security-lens checklist, second round.** Items 1, 3, 8, 10, 11 and 12 are "yes"; items 2, 4, 5, 6,
7, 9 and 13 are "n/a" (no path, config-writer, SQL, endpoint, variable, crypto or agent-service code
changed). The escalation line is "yes": this note exists and the second reviewer is OUTSTANDING.
Points the lens made that qualify the "yes" answers: (item 3) the allow-list holds because every ops
host takes `&AdapterBinary`, while agent-core takes `&Path` since it sits below distro; (item 8) a
copy of stdin bytes is zeroed after the write but not on the early-return paths, and captured stdout
(a generated private key) is not zeroed at all; (item 11) `Cargo.lock` was edited by hand.

**Major findings, and the decision on each.**

1. **Folder maps and the process rules are stale** (rules lens, major; design lens, major;
   compatibility and security, minor). `rules/rust.md` (utils tree, distro tree, rule-of-two example,
   "Process execution") and the distro tree in `rules/architecture.md` still describe the unbounded
   spawn and lack `adapter_binary.rs` and the new `utils/` files. Decision: **not edited by this
   branch**, because a rules edit needs the owner's approval. It is flagged to the owner, who is asked
   to approve a doc-only edit in this branch or a follow-up. The branch must not merge while the
   rules and the code disagree. `agent/CLAUDE.md` is already corrected.
2. **The branch base is behind dev** (design lens, major; correctness, security, rules and
   compatibility, minor). `git diff dev` shows the #91 hardening and `scripts/lib/check-structure.sh`
   as deleted; the branch's own unit hunk is comments only. Decision: **merge dev into the branch
   before Phase B**, then confirm that `git diff dev -- installer backend proto scripts` shows only
   the intended comment hunk. This is a git action and belongs to the owner or the orchestrator, not
   to an agent in this workflow. Until then the diff against dev is not evidence of what ships.

**Minor findings, and the decision on each.**

- **`useradd` killed at its 120 s deadline** returned `Bound` from `create()` without the rollback
  that later steps get (correctness, security). DONE: `create()` now probes `user_exists` after a
  `Bound` from `useradd` and runs the same `Mutation`-class `undo_creation` rollback. Non-timeout `useradd` failures are rolled back by shadow-utils itself.
- **Stdin input copy not zeroed on early return** (security). NOT DONE in this branch:
  `supervise_child` still builds the copy with `bytes.to_vec()` and the early-return paths of
  `start_helpers` drop it unzeroed. It is a residual below, together with the unzeroed captured
  stdout.
- **`list_members` memory** (security, correctness, design): up to 256 MiB listed, a transient peak
  of two to three times that. Decision as in the first round: lower the cap or parse from the byte
  buffer; measure the agent's RSS at the cap in Phase B. The extra `tar --list` pass in
  `create_archive`, and the 256 MiB and 2,000,000-member limits, are argued and not measured;
  Phase B measures them and the baseline records them.
- **Reload timeout leaves the service and the file out of step** (security). Decision: unchanged from
  the first round, accepted and stated as a residual; no second reload is added.
- **Unmeasured `--` and option forms** (correctness, security, compatibility): `nft ... -- <words>`
  including `-j list`, `mysql --init-command=SET SESSION lock_wait_timeout=240`, `quotaon -p --`,
  `systemctl <verb> -- <unit>`, `crontab -u <user> -- <file>`, and `passwd -S --` on RHEL
  (deliberately left out). A rejecting nft would make `list_bans` skip a set silently, so the ban
  list would come back empty. Decision: **no merge on reading alone.** Phase B runs each form on a
  Debian and an RHEL image and drops the guard where a family rejects it. `list_bans` is NOT changed:
  it still skips a set when `nft` exits non-zero (`if listed.status != 0 { continue; }`), because its
  doc comment defines that as "the bans table is not loaded, so no bans". A guard that a family
  rejects would therefore read as an empty ban list, which is the dangerous outcome, and only the
  Phase B run of the form on each family removes the risk. Residual below.
- **A timeout is reported as in-band `SystemFailure`** although the host may have applied the change
  (compatibility). The proto is unchanged and old backends keep working. Decision: no proto change in
  this branch. The owner is asked to check that the backend's handling of `SystemFailure` on mutating
  calls is idempotent or reconciles; if it is not, a later additive `ErrorCode` for a timeout is the
  answer, never a reused one.
- **Older oversize backups are refused at restore** (compatibility). Decision: as in the first round,
  recorded as a residual and in the baseline and release notes.
- **`END_OF_OPTIONS` formerly sat in `accounts/`** (design), and its
  doc says the nft sites are not individually annotated. Decision: first round,
  superseded: the constant now lives in `agent-core::utils` (see the seams-a facts above). The doc
  sentence about unannotated nft sites is still to be removed or the sites annotated.
- **The `Bound` passthrough is repeated in six area error types** (design). Decision: accepted, each
  area error owns its mapping. Ftps and sftp config-write sites move to `SafeWriteError::from_spawn`
  where the target is the same; elsewhere a comment states that the arms differ on purpose.
- **Test fixture `in_process_agent.rs` lacks the file-level allow** of `unwrap_used`, `expect_used`
  and `panic` (rules). Decision: fix in this branch by adding the same header `gated_cron_host.rs`
  uses.
- **Deadline-kill under the real sandbox is NOT measured**, and the unit comment says so
  (all lenses). Decision: Phase B measures a kill under the real unit on Debian and RHEL, on x86_64
  and aarch64, and replaces that sentence with the result. `Cargo.lock` is rewritten by cargo and the
  diff compared to the hand edit.

**Residuals this round adds or restates.** Not implemented, though decided earlier: the zeroing of the stdin copy on early return, and a loud
failure of `list_bans` on a non-zero `nft` status (a rejected `--` guard would show as an empty ban
list until Phase B measures it). Captured stdout, including a generated private key, is held in
unzeroed buffers. The rules and `agent/CLAUDE.md` statements about the process API are stale until the owner
approves the edit. The branch base is behind dev until dev is merged in. Everything in this section is
a reading of the code: nothing was compiled, no test or polygon suite was run.

**The human second review is still OUTSTANDING.** Nothing in this section discharges it.

### Independent review, third round (one reviewer, reading only), and the decision on each finding

One further independent agent review read the working tree (reading only: no `cargo`, no `maran`, no
polygon). It is an agent review like the two above and **does not discharge the human second review**.
The author of this section did not compile or run anything either, so every statement below is a
reading of the code and says "not measured" where it is one. Statements about what the code does now
were checked against the working tree on 2026-10-28 (the commands are given), and the fixers were still
editing it while this was written.

**B2 - the human second review is OUTSTANDING, and the branch MUST NOT merge to `main`.** Three agent
rounds have now read this change; none of them is the reviewer rules/security.md asks for, and the
more rounds there are the easier it is to read their number as a verdict. It is not one. At hand-off
the owner is told which privileged surfaces carry the outstanding review: the spawn path of the root
process (`agent-core/src/utils/**`, and every `ops` host that calls it), `agent-core/src/privs` by
being its caller (unchanged: `git diff HEAD --stat -- agent/crates/agent-core/src/privs` is empty),
and the comment block of `installer/systemd/maran-agent.service`. This note is an untracked file at
the time of writing; rules/security.md puts the note first, so the commit order is this note, then
the code, and either needs the owner's go.

**B1 - the rules.** `rules/rust.md` is still untouched by this branch (`git status --porcelain |
/usr/bin/grep rules/` prints nothing). The proposed edit is a unified diff for the owner to approve
(the folder map for the new `agent-core` files, `adapter_binary.rs`, the backup units, `tests/support`;
the rule of two; "Process execution"; the lock-hold sentence; a line in "Enforcement" for the
structure check on the spawn core). The branch must not merge while the rules and the code disagree.
`agent/CLAUDE.md` is not a rules file and has been brought to the code. One placement is a proposal
inside the proposal: `END_OF_OPTIONS` is imported by most `ops` areas, so it does not belong to
`accounts/`; the diff files it at the `agent-core` crate root beside `command_outcome.rs`, and the
code in the working tree had it in `agent-core/src/utils/end_of_options.rs` until the third round moved it to the crate root, `agent-core/src/end_of_options.rs` (the second-round
statement above). Whichever the owner picks, the map and the code must say the same.

**M1 - the archive pre-scan: time and memory.**
- *Time, decided and in the code:* `list_members` now runs under a new `CallClass::Listing` of
  3 600 s, not `Bulk` (12 h). A gzip stream that expands enormously now costs one core for at most one
  hour while `backup_lock` is held, not twelve. `/usr/bin/grep -n 'CallClass::Listing'
  agent/crates/ops/src/backup/list_archive_members.rs` shows the class named at the call. The number
  is argued, not measured: 64 GiB at 100 MB/s on a cold disk is about 11 minutes, and 3 600 s leaves
  a factor of five over that; it is raised in `call_class.rs` if a real artifact proves it tight.
- *Memory, corrected:* the earlier statements "two to three times the cap" (first round) and "about
  twice" (a doc comment) were both wrong. Read from the code, the worst case at the two caps
  (256 MiB of text, 2 000 000 members) is the reader's buffer (up to 256 MiB, grown by doubling, so a
  transient above that) plus one owned `String` per member built beside the kept text: about
  **2.3 times the cap, roughly 590 MiB** by this reading. The independent reviewer's range of 550 to
  800 MiB had its top overstated. **Not measured**: Phase B measures the agent's RSS at the caps.
  The lossy copy the reviewer named (`String::from_utf8_lossy(..).into_owned()`) is only a transient
  inside the core, and the core now tries `String::from_utf8` first; removing it does not lower the
  later peak.
- *Residual:* the unit sets no `MemoryMax` (`/usr/bin/grep -n Memory
  installer/systemd/maran-agent.service` prints only `MemoryDenyWriteExecute`), so on a 1 to 2 GB host
  the listing is bounded but not small. The caps are NOT lowered, because lowering them would make a
  restore refuse what creation can write (decision 5 above). The structural fix is a streaming
  pre-scan, which needs a streaming API in the core and is out of scope here.

**M2 - a writer's error no longer pre-empts the child's verdict.** The first round decided to "treat
`EPIPE` from the writer as benign once the leader has exited so the exit status decides", and the code
did not do it: the first write error returned `SpawnError::Input` and killed the group, so a
`chpasswd`, `mysql` or `openssl` that failed early was reported as an input failure or not depending
on which of the two events reached the supervisor first. Now an input failure is recorded and the wait
continues; the child's own status decides, and `Input` is returned only when the child exited **0**
without having received its input (reporting success for a password that was never delivered would be
wrong). A child that closed its stdin but keeps running is bounded by the deadline like any other.
`/usr/bin/grep -n 'input_failed' agent/crates/agent-core/src/utils/supervise_child.rs` shows the
flag and the exit-0 condition. **Not run:** the two tests the plan names (a non-zero exit before the
input is read reports its own status; an exit 0 reports `Input`) are written against a 1 MiB input so
the writer blocks, and they are a prediction until the suite runs.

**M3 - the `AdapterBinary` guarantee holds at the seams, not in the core.** The core takes `&Path`
because `agent-core` cannot depend on `distro`, and so do the private seams of `db`, `cron` and
`backup`. The earlier wording ("a string has no way to become one") was true only at the host traits.
The docs now say "at the seams", and `maran structure` gains check 17d, which lists the sanctioned
process-host files in the script and rejects a call of a spawn-core entry point from any other file,
with a stale-entry check as its positive control. What it still cannot see: a program path built in a
sanctioned host from a value that is not an `AdapterBinary`; the per-family tests pin every adapter
method to an absolute, `..`-free path, but the type does not check it. Not run against a planted
violation yet; that inverse control is owed in the PR.

**M4 - the account-side restore extraction is killed at 100 s: PRE-EXISTING, and its fix is its own
issue.** `ACCOUNT_ARCHIVER_DEADLINE` (100 s) sits below `fork_as_account`'s `CHILD_PATIENCE` (120 s) so
the inner kill fires first. Before this branch the outer 120 s killed the forked child and orphaned
`tar`; this branch only makes the kill reach `tar` and be reported as a refusal. A home that takes
longer than 100 s to extract is not restored, and the panel reads it as "archiver refused", because a
typed bound cannot cross the fork's single exit status. This branch states it: in the `Bulk` doc, in
the host's docs, and here, so that the 12 h table is not read as a promise for a restore. The real fix
(a patience argument on `fork_as_account`, which is the privs module and so needs its own threat note
and its own second reviewer) is **not** done here and goes to its own issue. Not measured: the 100 s
figure against a real large home.

**M5 - a timed-out reload: still "accepted, no second reload". PENDING OWNER CONFIRMATION.** The
independent review asks to reopen the first-round decision: after a `Bound` on the reload the file is
restored, but the service manager may already have accepted the reload, so the running service has the
new configuration and the disk the old one until some other write reloads. This note still records the
first-round decision as in force - accepted as the existing `ReloadFailed` shape, now reachable by
timeout, no second reload - because the owner has not decided otherwise. **The decision is the
owner's**, and the cheap alternative is on the table: one best-effort reload of the restored tree after
a reload `Bound`, which would raise the worst-case lock hold from 310 s / 490 s to about 555 s / 735 s
and would not help if that second reload also times out. It is not implemented; until the owner
confirms, the 310 s / 490 s figures in `config_tree_lock.rs` and in "Process execution" stand.

**M6 - the systemd sandbox: still not measured, and a measurement is owed BEFORE `main`.** Nothing in
this round changes that. The unit's own comment says so, and the polygon images boot no systemd, so
no container can observe it (rules/testing.md: `UNOBSERVED HERE: this image boots no systemd`). One
syscall answering `EPERM` under `SystemCallErrorNumber=EPERM` would break every spawn the root agent
makes. Owed, on real hosts with systemd as PID 1 (Ubuntu 24.04 and AlmaLinux 9 at least, one aarch64
host because syscall names differ by architecture and aarch64 was never checked): the recursive
`@system-service` membership of `setpgid`, `kill`, `waitid`, `wait4`, `clone3`, `vfork`, `pipe2`,
`poll`/`ppoll`; the spawn-core tests run under a COPY of the real unit's sandbox; a negative control
(the same run with `SystemCallFilter=~setpgid` must FAIL, or the measurement observes nothing); and
the kernel log showing no `SECCOMP` record for the real run and the denied syscall for the control.
This is a runbook and not a code change; when it is done the unit comment is replaced by the result.

**M7 - `CreateBackup` fails at the end of the work.** A home over 2 000 000 members was archived (up
to 12 h) and only then refused by the post-archive listing, and the backup deleted. The measuring walk
that already runs now also counts members and refuses a home over the ceiling BEFORE any dump or
archive, reusing the same `LISTING_REFUSED` status so the panel sees one error early or late. It
refuses only when sure (the archive adds a handful of members), so a home exactly at the ceiling still
falls through to the authoritative post-archive listing. Bytes of the listing are not estimated and
that axis stays post-archive. Residual: backups written before this change that exceed the caps stay
unrestorable (restated, first round). Not run: the refusal test and its inverse control are
predictions until the suite runs.

**Minor findings with no change in this branch.** `m1` (a "deadline passed" race reporting `TimedOut`
for a program that finished) was read again and is **wrong as stated**: `has_exited` runs after the
timed receive in the same iteration, so an exit during the nap is seen before the next deadline check;
the residue is a microsecond window. `m3` (a failure to reap is silent) is deferred: it needs `tracing`
as a dependency of `agent-core`. `m5` (the `Database` class at 300 s, `LOCK_WAIT_OPTION`) is covered by
the db polygon runs this note already demands. `m6` (`nft list table inet maran_bans` printing every
ban only to test existence, which fails every ban attempt past the 16 MiB cap) and `m7` (a timeout is
one gRPC error code) are real and are follow-ups, the latter an additive `ErrorCode` plus the C# side.
Whether the file-size findings are closed is not asserted here; `process_backup_host.rs` and
`account_operations.rs` are measured with `wc -l`, not quoted.

**Residuals this round adds or restates.** The human second review (B2). The systemd sandbox
measurement (M6). The 100 s account-side limit (M4), to its own issue. The unreloaded service after a
timed-out reload (M5), pending the owner. The pre-scan's roughly 590 MiB peak with no `MemoryMax` (M1),
not measured. The Phase B polygon suites, none of which has run. Everything in this section is a
reading of the code.

**The human second review is still OUTSTANDING.** Nothing in this section discharges it.

## Phase B verification evidence (round 2, current tree)

Everything above this heading is a reading of the code. This section records what was built, run and
measured on the tree after the independent-review fixes. Earlier-round numbers are stale and are not
used. Machine load was high (load average 9 to 27) during the measurements, so timings carry that.

### Gates

- `maran check`: all 8 tools OK. `maran structure`: STRUCTURE-OK. `maran proto`: PROTO-OK (the branch changes nothing under `proto/`).
- `maran format --check agent`: first FAILED on two unformatted files, fixed with `maran format agent`, re-check OK.
- `maran agent check` (fmt, clippy `-D warnings`, test, doc): AGENT-CHECK OK after the fixes below.
- `maran test rust`: TEST VERDICT OK, 33 targets, 2618 passed / 0 failed / 147 ignored, recorded in `scripts/test-baseline.txt`. `maran_agent_core` rose 461 to 466 and `maran_ops` 1618 to 1623; no row fell. The final run on the strengthened tree matched the baseline exactly.
- **Defect found by the gates and fixed.** A first `--accept` was refused because `a_child_that_exits_zero_without_reading_its_input_is_an_input_failure` failed once. Cause: a real race in `supervise_child`, which finished once the child had exited and the readers were done, without waiting for the writer thread's verdict, so a late input failure could be dropped and an exit-0 child that never read its input could come back as success. Fix: `ChildEvent::InputDelivered`, sent by `start_writer` on success, so the writer reports exactly once; the supervisor counts the writer as pending. The test then ran 0 failures in 300 runs.
- The baseline edit belongs in its own commit; the accept path wiped the baseline header prose, which was restored by hand.

### Measurements, before (dev tip c47c53c) and after (this branch)

Scratch copies, probe harness driving `safe_write::write_config` with a fake validator.

| Case | dev | branch |
|---|---|---|
| Validator that hangs under `config_tree_lock` | still running at 75 s, other tenant's write blocked, leader and grandchild alive | typed `Bound(TimedOut)` at 60017 ms, group gone, previous bytes restored, other tenant's write done 3 ms later |
| 5 MiB of output | held whole in a String (peak memory up about 10 MB) | refused at the 4 MiB cap, none kept; exactly 4194304 bytes accepted, 4194305 refused |
| Exits 0 at once (inverse control) | Ok in 6 ms | Ok in 9 ms |
| Over the service layer (php install rpc, called directly) | not measured | `ErrorCode::SystemFailure`, empty `tool_output`, message names the program; about 67 s wall including load |

**Disagreement with the task text, stated plainly.** The task expected a gRPC `DeadlineExceeded`. The branch does not do that: the string appears nowhere in `agent/crates` or `proto/`. The timeout travels in band as `SYSTEM_FAILURE` inside an Ok tonic response. This was a deliberate server-limits decision (no tonic timeout and no per-connection concurrency limit, justified in `server.rs`; `rules/proto.md` keeps the transport status for transport; proto stays additive-only). The owner must decide whether a new `ErrorCode` (a contract change plus the C# side) is wanted.

### Mutation proof

About 75 local-lane mutants, each scored against the whole suite, one at a time, files restored and checked with `cmp`. Almost all were killed by the named test. Details that matter:

- **Survivors found and closed by strengthening tests.** (1) Swallowing a bound on a non-final `DROP DATABASE` in `drop_account_databases` survived, because the fake times out on every later statement; the test now also asserts the statements sent equal the answered ones plus one, and the mutant dies. (2) A guarded arm `SslOpError::Bound(..) if false =>` survived the status census, because the wildcard answered the same code; the census now rejects a guarded arm and has a guarded inverse control. The per-area `ssl_status_tests` scan alone is still blind to a guard; only the shared census catches it.
- **Polygon survivor, not closed.** Deleting `.process_group(0)` leaves the polygon test `a_validator_that_hangs_on_a_real_host_is_killed_at_its_deadline_...` green on both families, because the hung process is a single `nginx -t` and the leader kill is enough. The defect is caught locally by about 8 to 10 tests that do create grandchildren. The polygon test is therefore not the guard for the group kill.
- **Hangs, not kills.** Four lock-leak mutants (`mem::forget` on the firewall lock and on `render_validate_swap`) deadlock the whole suite and were stopped at 600 s. They are recorded as hangs; the named test was not isolated.
- **Not attributable or not run.** `distro 3` (a `From<&'static str>` impl) was not scored to a named kill; `distro 1` and `distro 4` ended ABORTED by a harness parser disagreement on the doctest shape (kills read from raw evidence for 1, unconfirmed for 4). Polygon runs for `END_OF_OPTIONS` (ABORTED), `OPTION_REFUSALS` and `MAXIMUM_NAME_BYTES` (named tests already red at the polygon baseline) could not be scored. "Bound arm below the wildcard" needs a two-line edit the harness refuses.
- **Old unbounded host evidence.** With the real-process test host switched to plain `Command::output`, 5 of 6 `safe_write_bound_on_real_processes` tests FAIL (71 s, waiting on pipes held by the grandchild); only the live-process probe control passes.
- **Polygon baseline is not clean.** With no mutation, 2 tests on ubuntu24 and 15 on alma9 are red, including `the_option_refusal_check_sees_every_program_it_judges` and `an_archive_whose_member_name_exceeds_the_bound_is_refused_before_anything_is_extracted`. These polygon failures are unexplained and unattributed.

### Still unverified or failing

- `maran handshake`: FAILED (UNMEASURED). The API could not log in to the shared dev Postgres (Npgsql 28P01, user `maran_dev`). The agent side built and listened. The agent-to-API contract was not observed on the wire.
- `maran licenses --check`: FAILED; the diff lists only NuGet test packages and no cargo lines. Attributed to the machine's restore state and the branch touching no backend file, but not proven against a pristine dev copy.
- No backend, SPA or migration gate was run (the branch touches none); backend is left to CI.
- The polygon (real-host) suites are not green and are not a clean verdict. The `--` acceptance suite (`binary_paths_on_a_real_host`) and the assumptions about `nft --`, `mysql --init-command`, `quotaon -p --`, `passwd -S --`, `systemctl is-active -- unit` were not confirmed as passing on a real host.
- The deadline-kill spawn under the real systemd sandbox (the unit-file comment says NOT measured) is still unmeasured.
- No gRPC status over a real unix socket was observed; the in-process fixture cannot inject a hanging binary.
- Not measured: descendants that leave the group via `setsid`, thread leak across many timeouts, `tar --list` with millions of members, the 2,000,000-member and 256 MiB bounds (argued, not measured).
- The branch is behind dev (merge-base 1eb8dde, dev at c47c53c). Rebase conflicts are expected in the unit file and `scripts/test-baseline.txt`; merging dev must not reintroduce the deleted #91 hardening lines.
- `rules/rust.md` and `rules/architecture.md` edits (utils tree, rule of two, Process execution, distro tree) still need owner approval and have not been made.

**The human second review is still OUTSTANDING.** Nothing in this section discharges it; passing gates and killed mutants are evidence for the reviewer, not a substitute.

## Second review — OUTSTANDING

rules/security.md "Sensitive change escalation" applies: this change alters how the only root
process starts, waits for, signals and reads every program it runs. No human second reviewer has
read this note or the diff. **The branch `fix/90-agent-bounded-spawn` MUST NOT merge to `main`
until one has.** An agent session cannot discharge this requirement, and nothing in this note
discharges it. The note records the debt, as that rule requires.

The reviewer records the verdict here:

- Reviewer:
- Date:
- Commit reviewed:
- Checks above that were run, and what they showed:
- Verdict:
