# Pending package updates reading (issue #324) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change adds an rpc through which the panel makes the only root process on the server start a
package manager, a program the agent has never executed itself: since issue #93 the package
manager runs only inside `maran-php-install@.service`, outside the agent's sandbox. Written
before the code, per that rule. The branch may land on its issue branch but MUST NOT merge to
`main` until a second reviewer has read this note and the diff and checked the items under "What
a reviewer must check". An agent session wrote this note and cannot supply that reviewer; this
note records the debt and discharges none of it.

## The surface

1. A new rpc, `MonitorService.ListPendingUpdates()` (agent contract revision 17, additive), served
   by the agent as root. It takes **no input**: the request message is empty. It runs one program
   per family, chosen by the `distro` crate, with an argv array and no shell:

   ```
   Debian family:  apt-get -s -q -o Debug::NoLocking=true dist-upgrade
   RHEL family:    dnf check-update -C -q
   ```

   The path of each program is an `AdapterBinary` from a new adapter method
   (`package_query_binary`), constructible only inside the `distro` crate, with a new row in the
   polygon `binary_paths_on_a_real_host` suite. `apt-get -s` is a simulation: nothing is installed,
   removed or configured. `dnf check-update -C` is cache-only: `-C` forbids a metadata refresh.
   The exact flag spelling above is the plan's and is re-confirmed against the recorded fixtures
   and each family's manual page when the code is written; this note is edited if it moves.
2. The run goes through the bounded spawn core in `CallClass::Query` (60 s deadline, whole process
   group killed on expiry, 4 MiB per stream). Reaching a bound is a typed error, never a
   shortened answer handed to the parser. The child's environment is `apply_child_environment`
   and nothing else: cleared, then `PATH=/usr/sbin:/usr/bin:/sbin:/bin` and `LC_ALL=C`. Stdin is
   null.
3. The output is parsed by the family's adapter into rows (`package_name`, `available_version`,
   `installed_version`, `source`). A catalogue the agent could not read is
   `MonitorError::UpdateCatalogueUnavailable`, answered as `ERROR_CODE_SYSTEM_FAILURE`. It is
   **never** an empty list: "0 updates" is only ever the answer to a read that succeeded.
4. The panel exposes the answer through two administrator-only reads
   (`AuthorizationPolicies.AdminOnly`): `GET /api/v1/monitoring/updates` (paged in the panel's
   memory) and `GET /api/v1/monitoring/updates/count`. The SPA polls the count for the
   navigation badge on a ten-minute cadence, and only when the signed-in user is an
   administrator. Both reads ask the agent live; the panel stores nothing.
5. No new listening port, daemon, rpc that takes input, or outbound call (rules/security.md items
   10 and 12). The cache is read, never refreshed, by design decision D1 in the plan. If the
   owner changes D1 so that the agent refreshes metadata, this note is void: that is an outbound
   call and needs a spec change first.

## What an attacker could try, and why it does not work

Who can reach the rpc: the agent serves one peer-credentialed unix socket, so every request comes
from the panel's uid. The actors are a compromised panel process (can call any rpc) and a
panel administrator (the only role the HTTP routes admit). A hosting customer has no route to it.
Someone who can already write root-owned files is out of scope: that person already has root.

1. **Make root run something else.** The request message has no field. The program is an
   `AdapterBinary` and every argument is a constant in the `distro` crate. No operand exists, so
   there is nothing for an option to be smuggled through and no `--` is needed (stated here so a
   reviewer does not read the absence as an oversight). `-s` and `-C` are the whole safety of the
   operation, and they sit in a constant array that no caller value reaches
   (rules/security.md items 3 and 12; rules/rust.md "Process execution").
2. **Make the agent install, remove or refresh something.** `apt-get -s` simulates;
   `Debug::NoLocking=true` is what lets the simulation run beside a real package operation
   without taking the dpkg lock, and the price is a possibly inconsistent read, which fails
   closed (below). `dnf check-update -C` never contacts a repository. If the flag were dropped by
   a future edit, the call would become an outbound refresh from root. A test asserts the exact
   argv of both families, so that edit turns a test red; it would still need a reviewer to notice
   that the test was changed with it.
3. **Plant a hostile package name or version.** The strings come from the package
   metadata the host's own configured repositories published, not from a customer. They are
   still treated as untrusted text: each row is parsed by a fixed grammar, a line that does not
   match fails the whole read rather than being skipped, the 4 MiB cap bounds the volume, no
   string is placed in a path, a shell or a query on the agent side, and the SPA renders it as
   text. A `Remv`, `Conf` or new-dependency line is ignored by rule, not by luck.
4. **Reach the child through its environment.** The inherited environment is cleared, so
   `LD_PRELOAD`, `BASH_ENV`, `APT_CONFIG`, `http_proxy` and the like in the daemon's own
   environment or in `/etc/maran/agent.env` do not reach `apt-get` or `dnf`. That is the same
   guarantee every other spawn already has; this change adds no spawn that bypasses it, because
   `launch_child` remains the single `Command::new`.
5. **Hang the agent with a stuck package manager.** The run is under the `Query` deadline with
   the whole group killed, so a wedged `dnf` cannot hold a blocking-pool thread past the
   deadline plus the reap ceiling. It takes no lock the agent holds (not `config_tree_lock`, not
   an account lock), so it cannot stall other operations by waiting on or holding one.
6. **Use the rpc to burn root CPU.** A compromised panel or an administrator refreshing the page
   in a loop starts one package-manager process per call. Each is bounded in time and output,
   and the child is a read, but nothing in this change limits how many run at once beyond the
   blocking pool itself. See "What the author could not verify" and "What a reviewer must check".
   Neither actor gains anything but cost, and a compromised panel can already call far more
   expensive rpcs.
7. **Learn something through the error text.** The failure is answered as
   `SystemFailure` with the area's operator-facing message; the child's output is not forwarded
   to the caller (rules/rust.md "Process execution": captured output goes to a typed variant for
   the operator log, never verbatim to the client). A `Bound` carries the program and the bound,
   never a byte the program printed.

## What could go wrong, and what was chosen

- **A cache that was never built, or is half-written.** `dnf check-update -C` with no cache exits
  1; `apt-get -s` with an unreadable lists directory fails. Both are `UpdateCatalogueUnavailable`.
  An unreadable catalogue is never reported as "nothing to update": a badge of zero or an empty
  "up to date" page on a host whose security updates are pending is the failure that matters
  here, so the SPA draws no badge and shows the reason instead.
- **A stale cache is a correct answer to a different question.** The panel reports what the
  host's package manager already knows; it does not make the host check. The page says so. A host
  that never refreshes its metadata reports a frozen list, and that is by design while D1 stands.
- **Exit status is the contract, per family.** apt: 0. dnf: 0 = none, 100 = updates, anything
  else is a failure, 1 included. The parser maps nothing else to a success.
- **Parsing is recorded, not imagined.** The fixtures under
  `agent/crates/distro/tests/fixtures/package_manager/` are output recorded from real
  `ubuntu:24.04` and `almalinux:9` containers. If those containers cannot be run, the work stops;
  fixtures are not hand-written. A grammar the fixtures do not cover (a package with several
  origins, a `[arch]` suffix, a kept-back package) is a failure to read, not a skipped line.
- **The unit is not widened.** No `ReadWritePaths=`, capability or syscall-filter change is made
  for this rpc. `ProtectSystem=true` keeps `/usr` and `/boot` read-only and leaves `/var` and
  `/etc` writable, so a read of `/var/lib/apt/lists` or `/var/cache/dnf` is expected to need no
  entry. If the polygon shows otherwise, the work **stops**: changing the unit is an installer
  privileged step and belongs to the owner, not to this change. Weakening the sandbox to make a
  test pass is a reject.
- **A second privileged use of the package manager, with the opposite sandbox.** The install
  runs in an unsandboxed unit on purpose (issue #93). This read runs INSIDE the agent's sandbox:
  `NoNewPrivileges`, the enumerated capability set, `SystemCallFilter=@system-service quotactl
  quotactl_fd`, `PrivateTmp`. That is the narrower environment, and the reason the read is safe
  to run there is that it mutates nothing, not that the sandbox is known to be sufficient for it.

## What a reviewer must check

- `agent/crates/distro/src/{debian,rhel}/*`: the argv of both families is a `&'static` constant
  array; it contains `-s` (Debian) and `-C` (RHEL) and nothing built from a variable; the binary
  is an `AdapterBinary`; the exit-status meaning per family matches the manual page, not the
  author's memory of it. Read both families' documentation, not only the one on the machine at
  hand (rules/architecture.md "Supported systems").
- `agent/crates/ops/src/monitor/list_pending_updates.rs`: the run is `CallClass::Query`, is
  not overridden at the call site, takes no lock, and a non-zero status, an unparsable line, or a
  `Bound` is an error and **cannot** become an empty list. Check the three paths separately; they
  are the ones a quiet regression would merge.
- The parsers and their tests: a line the grammar does not recognise fails the read; `Remv` and
  `Conf` lines are ignored; a package with a new dependency and no installed version is not a
  row; kept-back and `+`/`.` names are handled. Confirm the fixtures are real recorded output
  (compare against a fresh `docker run` yourself).
- `agent/crates/agent/src/services/monitor/`: the service method is the three steps and no more;
  `monitor_status.rs` maps `UpdateCatalogueUnavailable` and `Bound` by named arm to
  `SystemFailure`, ahead of any wildcard; the wire message has no field a caller can fill.
- `proto/agent/v1/monitor.proto`: the change is additive only and the doc comment states
  read-only, no input, cache-only, and that an unreadable catalogue is an error. The revision
  moves to 15 in all four places (agent constant, `REVISIONS` digest, `handshake.rs`,
  `AgentProtoVersionWindow`) together.
- `installer/systemd/maran-agent.service` is **unchanged** by this diff. If it is not, this note
  covers neither the edit nor its reason.
- That nothing outside `distro` names `apt-get`, `dnf` or a path to them (`maran structure`
  rejects platform literals under `ops/`), and that no other rpc gained a way to reach
  `package_query_binary`.
- Whether the absence of a concurrency limit on this rpc (attack 6) is acceptable. The author
  judged it so because the call is read-only, bounded and administrator-only, and because
  rules/rust.md already records that a tonic-level limit is a panel-wide limit. That is a
  judgement, not a measurement; the reviewer may reasonably ask for a cache in the panel or a
  single-flight guard in the agent.
- The panel side: both routes are `AdminOnly` and have a customer-refused test; an agent failure
  fails the request with the typed error and is not turned into a zero or an empty page;
  `ResilientAgentMonitorClient` retries a read only the way the other monitoring reads do.

## What the author could not verify

None of the below was run on a real host when this note was written; the note is revised with the
polygon results, and until then each item is an open assumption, not a finding.

- That `apt-get -s dist-upgrade` and `dnf check-update -C` run to a correct answer **inside the
  agent's sandbox on every supported family**: Ubuntu 22.04 and 24.04, Debian 12 and 13,
  AlmaLinux 9 and 10, Rocky Linux 9 and 10, `x86_64` and `aarch64`. The specific unknowns are the
  agent's own empty `PATH` and the child's fixed one (`apply_child_environment`), the cleared
  environment (no `HOME`, which a package manager's configuration or plugins may expect), the
  syscall filter, `PrivateTmp`, and read access to the package databases under `ProtectSystem=true`.
  If any of these fails, the result is a typed error and the Updates page shows its reason; it is
  not a wrong answer. That is the safe direction, but it makes the feature absent on that
  family, not secure on it.
- That `apt-get -s` runs **no hook, maintainer script or helper** as root. The manual page says a
  simulation performs no action, but that has not been shown with a deliberately configured
  `APT::` hook or `DPkg::` hook on a host. The same question for `dnf check-update -C`: it loads
  the host's installed plugins as root, and the plugin set differs per family and per machine.
  These are programs only root could have installed, so this adds no customer-reachable path,
  but a reviewer should confirm they do not run anything on a read.
- That `Debug::NoLocking=true` is honoured as spelled by the apt shipped on each Debian-family
  release, and that reading the lists directory beside a concurrent `maran-php-install` unit
  fails closed rather than yielding a wrong list. The same concurrency question applies to `dnf`
  against its own cache lock.
- Behaviour at scale: the largest realistic pending set against the 4 MiB cap and the 60 s class
  deadline. A host with very many pending packages, or a slow disk, may exhaust either. That is an
  error, not a truncated list. Whether 60 s is wide enough is a measurement the polygon cannot
  give on its own; raising a class is done once, in `call_class.rs`, with its reason, never at the
  call site.
- dnf5. The plan assumes `dnf` 4 on the RHEL family. If a supported release ships a `dnf` whose
  `check-update` exit statuses or `-C` semantics differ, the adapter for that family reads the
  new documentation and the note is edited.
- Timing and load: no measurement of how long an administrator's page refresh plus the badge poll
  keeps a root package manager busy on a small host.
