# Pending package updates reading (issue #324) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change adds an rpc through which the panel makes the only root process on the server start a
package manager, a program the agent has never executed itself: since issue #93 the package
manager runs only inside `maran-php-install@.service`, outside the agent's sandbox. Written
before the code, per that rule, and revised after the code landed so that it describes what was
built (a document describing code that does not exist is a defect, rules/architecture.md); where the
code differs from what this note first required, "Deviations found when the code landed" says so
plainly. The branch may land on its issue branch but MUST NOT merge to
`main` until a second reviewer has read this note and the diff and checked the items under "What
a reviewer must check". An agent session wrote this note and cannot supply that reviewer; this
note records the debt and discharges none of it.

## The surface

1. A new rpc, `MonitorService.ListPendingUpdates()` (agent contract revision 17, additive), served
   by the agent as root. It takes **no input**: the request message is empty. It runs one program
   per family, chosen by the `distro` crate, with an argv array and no shell:

   ```
   Debian family:  apt-get indextargets --format '$(FILENAME)'          (the catalogue probe, run first)
                   apt-get -s -q -o Debug::NoLocking=true dist-upgrade
   RHEL family:    dnf check-update -C -q
   ```

   The Debian family runs TWO fixed argv arrays, the probe first. Both are `&'static [&'static str]`
   constants in `distro/src/debian/debian_packages.rs` (`CATALOGUE_PROBE_ARGUMENTS`,
   `PENDING_UPDATES_ARGUMENTS`); the RHEL family's is `rhel_packages::PENDING_UPDATES_ARGUMENTS`. The
   `$(FILENAME)` in the probe is apt's own format placeholder, passed as one literal argument; there
   is no shell, so nothing expands it. The path of each program is an `AdapterBinary` from a new
   adapter method (`package_query_binary`, `/usr/bin/apt-get` and `/usr/bin/dnf`), constructible only
   inside the `distro` crate, with a new row in the polygon `binary_paths_on_a_real_host` suite.
   `apt-get -s` is a simulation: nothing is installed, removed or configured. `dnf check-update -C`
   is cache-only: `-C` forbids a metadata refresh. `apt-get indextargets` only lists index files
   that exist on disk. The flag spellings above are the ones the code and its recorded fixtures use.
2. The run goes through the bounded spawn core in `CallClass::Query` (60 s deadline, whole process
   group killed on expiry, 4 MiB per stream). Reaching a bound is a typed error, never a
   shortened answer handed to the parser. Both runs of the Debian family are in that class, and
   `list_pending_updates` passes `CallClass::Query` itself without overriding it (a test asserts the
   class of every run). The child's environment is `apply_child_environment`
   and nothing else: cleared, then `PATH=/usr/sbin:/usr/bin:/sbin:/bin` and `LC_ALL=C`. Stdin is
   null.
3. The output is parsed by the family's adapter into rows (`package_name`, `available_version`,
   `installed_version`, `source`). A catalogue the agent could not read is
   `MonitorError::UpdateCatalogueUnavailable { reason }`, answered as `ERROR_CODE_SYSTEM_FAILURE`
   (named arm in `monitor_status.rs`, ahead of the wildcard). The `reason` is a
   `PendingUpdatesFailure`: `NotStarted`, `Unsuccessful { status }`, `CatalogueNotBuilt`,
   `UnrecognisedLine { line }` or `IncompleteOutput`, so it carries an exit status or a line number
   and never a byte the package manager printed. A run killed at its deadline or cap stays
   `MonitorError::Bound`, a different variant. None of them is an empty list: "0 updates" is only
   ever the answer to a read that succeeded. `installed_version` is empty on the wire on the RHEL
   family (dnf prints only the new version) and the panel reads it back as `null`.
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
   operation (the probe only lists files), and they sit in constant arrays that no caller value reaches
   (rules/security.md items 3 and 12; rules/rust.md "Process execution").
2. **Make the agent install, remove or refresh something.** `apt-get -s` simulates;
   `Debug::NoLocking=true` is what lets the simulation run beside a real package operation
   without taking the dpkg lock, and the price is a possibly inconsistent read, which fails
   closed (below). `dnf check-update -C` never contacts a repository. If the flag were dropped by
   a future edit, the call would become an outbound refresh from root. A test asserts the exact
   argv of both families, probe included, so that edit turns a test red; it would still need a reviewer to notice
   that the test was changed with it.
3. **Plant a hostile package name or version.** The strings come from the package
   metadata the host's own configured repositories published, not from a customer. They are
   still treated as untrusted text: each row is parsed by a fixed grammar, a line that does not
   match fails the whole read rather than being skipped, the 4 MiB cap bounds the volume, no
   string is placed in a path, a shell or a query on the agent side, and the SPA renders it as
   text. Every name, version and repository id must pass one shared test
   (`is_printable_token`: non-empty printable non-space ASCII), so a control character or a space in
   a field fails the read. A `Remv`, `Conf` or `Purg` line is ignored by rule, not by luck, and an
   `Inst` line with no installed version (a new dependency) is well formed and is not a row.
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
  1 (recorded), which the RHEL reader maps to `Unsuccessful`, so that family needs no probe. On the
  Debian family `apt-get -s dist-upgrade` against an EMPTY lists directory does NOT fail: it exits 0
  and prints "0 upgraded" (recorded), which read alone is indistinguishable from an up-to-date host.
  So the Debian family runs the probe first, and empty probe output is
  `CatalogueNotBuilt`; a test asserts the simulation is not even run then. Both families end as
  `UpdateCatalogueUnavailable`. An unreadable catalogue is never reported as "nothing to update": a badge of zero or an empty
  "up to date" page on a host whose security updates are pending is the failure that matters
  here, so the SPA draws no badge and shows the reason instead.
- **A stale cache is a correct answer to a different question.** The panel reports what the
  host's package manager already knows; it does not make the host check. The page says so. A host
  that never refreshes its metadata reports a frozen list, and that is by design while D1 stands.
- **Exit status is the contract, per family.** apt (probe and simulation): 0. dnf: 0 = none, 100 =
  updates, anything else is a failure, 1 included. The parsers map nothing else to a success. The
  output must also agree with the status: dnf status 0 with any output, dnf status 100 with no row,
  and an apt simulation without its closing "N upgraded, ..." summary line are all failures
  (`UnrecognisedLine`, `IncompleteOutput`), never an empty or shortened list.
- **Parsing is recorded, not imagined.** The fixtures under
  `agent/crates/distro/tests/fixtures/package_manager/` are output recorded from containers (see
  "Deviations found when the code landed" for exactly which, and which shapes needed a constructed
  source). None is hand-written. A line outside the grammar fails the whole read, not a skipped line;
  the grammar is stated in the doc comments of `read_pending_updates` in each family.
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

- `agent/crates/distro/src/{debian,rhel}/*` and `catalogue_probe.rs`: the argv of both families
  (and the Debian probe) is a `&'static` constant array; the simulation contains `-s` (Debian) and
  the RHEL run `-C`, and nothing is built from a variable; the binary
  is an `AdapterBinary`; the exit-status meaning per family matches the manual page, not the
  author's memory of it. Read both families' documentation, not only the one on the machine at
  hand (rules/architecture.md "Supported systems").
- `agent/crates/ops/src/monitor/list_pending_updates.rs`: the run is `CallClass::Query`, is
  not overridden at the call site, takes no lock, and a non-zero status, an unparsable line, or a
  `Bound` is an error and **cannot** become an empty list. Check the three paths separately; they
  are the ones a quiet regression would merge.
- The parsers and their tests: a line the grammar does not recognise fails the read; `Remv` and
  `Conf`/`Purg` lines are ignored; a package with a new dependency and no installed version is not
  a row; a kept-back package is NOT reported (see Deviations); `+` and `.` names, dnf `Obsoleting`
  rows and the `[arch]` suffix are handled. Confirm the fixtures are real recorded output (compare
  against a fresh `docker run` yourself), and read the two dnf fixtures and the `Remv` fixture
  with the provenance stated under Deviations.
- `agent/crates/agent/src/services/monitor/`: the service method is the three steps and no more
  (`run_blocking`, `list_pending_updates`, `reported_pending_updates`); `monitor_status.rs` maps
  `UpdateCatalogueUnavailable` and `Bound` by named arm to `SystemFailure`, ahead of any wildcard;
  the wire message has no field a caller can fill.
- `proto/agent/v1/monitor.proto`: the change is additive only and the doc comment states
  read-only, no input, cache-only, and that an unreadable catalogue is an error. The revision is
  **17** (the plan said 15; the contract was already at 16 when this was written) and must move
  in all four places together: agent constant (`PROTO_VERSION`), `REVISIONS` digest,
  `handshake.rs`, `AgentProtoVersionWindow`. On this branch three are at 17; the `REVISIONS`
  row 17 holds a placeholder digest of zero until `maran proto --accept` has recorded
  `contract-baseline.txt`, so the test that compares them is expected to fail by name until the
  integrator fills it. A reviewer must confirm the placeholder is gone.
- `installer/systemd/maran-agent.service` is **unchanged** by this diff (measured: no path under
  `installer/` appears in `git diff origin/dev..HEAD --stat`). If it is not, this note covers
  neither the edit nor its reason.
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

## Deviations found when the code landed

Written after the code, against it. Each item is a place where the code differs from what this note
first said, stated rather than smoothed over.

1. **Contract revision 17, not 15.** The contract was already at 16.
2. **A second fixed argv on the Debian family.** This note first described one program run per family.
   `apt-get -s dist-upgrade` with an empty lists directory exits 0 and prints "0 upgraded" (recorded in
   `apt_dist_upgrade_empty_lists.txt`), so the first-written design would have reported a host with
   no package catalogue as up to date, which is the failure this note exists to prevent. The Debian
   family therefore runs `apt-get indextargets --format '$(FILENAME)'` first; empty output is the
   typed `CatalogueNotBuilt`. This is a new `distro` unit (`CatalogueProbe`, answered by
   `PackageFacts::catalogue_probe`) and a second spawn per call, still `CallClass::Query`, still a
   constant argv. The RHEL family needs no probe: `dnf check-update -C` exits 1 with no cache
   (recorded in `dnf_check_update_no_cache.txt`) and 1 is a failure.
3. **The error variant carries a reason.** `MonitorError::UpdateCatalogueUnavailable` was described
   without a payload; it holds a `PendingUpdatesFailure`, whose variants hold only an exit status or a
   line number, never printed bytes.
4. **Kept-back packages are not a failure to read.** This note said a kept-back package is a failure to
   read, not a skipped line. The code does the opposite on purpose: apt lists a kept-back package
   under a heading with no `Inst` line, the heading and the indented names are known apt output, and
   the package is simply not reported (its count shows only in the summary's "not upgraded"). The
   proto comment says "Packages the package manager would keep back or remove are not listed". A
   reviewer who disagrees that this is safe should say so: an administrator is not told about a
   held package.
5. **Grammar the note called a failure is now parsed.** A package with several origins and the
   `[arch]` suffix on an apt `Inst` line are read (origins kept as printed, the arch bracket
   dropped). dnf `Obsoleting Packages` rows ARE reported as updates, and the indented line that names
   the package being replaced is read and dropped. Anything else still fails the whole read.
6. **RHEL names lose the architecture suffix.** `name.noarch` is reported as `name`, so a multilib host
   can report one name twice, once per architecture. Debian keeps a `:arch` qualifier where apt
   prints one.
7. **Fixture provenance is not uniform.** All were recorded by running the commands, none written by
   hand, but not all from an unmodified image: the Debian fixtures come from `ubuntu:24.04`
   (`noble-20240429`) only, and the RHEL ones from `almalinux:9.3` only. Two dnf fixtures,
   `dnf_check_update_long_and_plus_names.txt` and `dnf_check_update_obsoleting.txt`, were recorded
   from the same image with two throw-away local repositories, because no real repository on the
   image offers a `+` name, a wrapping-length name or an `Obsoleting Packages` section; that run
   restricted dnf to the local repository, which changes which rows are printed and not their
   layout. `apt_remove_simulation.txt` is `apt-get -s remove curl`, because none of the upgrade
   simulations printed a `Remv` line. No recording exists from Debian 12 or 13, Ubuntu 22.04, or the
   RHEL-family 10 releases, and none from `aarch64`.
8. **The `REVISIONS` digest is a placeholder** at the time of writing (see "What a reviewer must
   check"); the integrator fills it from the recorded baseline.

## What the author could not verify

None of the below was run on a real host. The unit file is untouched, and **the behaviour of both
runs under the unit's real sandbox (the agent's empty `PATH`, `ProtectSystem=true`, the cleared
child environment) is UNVERIFIED until the polygon runs**; the polygon row for
`package_query_binary` exists but has not been observed. This note is revised with the polygon
results, and until then each item is an open assumption, not a finding. The fixtures prove the
parsers read what a package manager printed in a container; they prove nothing about running it
under the unit.

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
- That `apt-get indextargets` exists and prints one file name per index on every supported Debian
  family release (it was recorded on Ubuntu 24.04 only), runs inside the sandbox, and reads
  "no index files" the same way on a host whose lists directory exists but holds only partial
  downloads.
- That `Debug::NoLocking=true` is honoured as spelled by the apt shipped on each Debian-family
  release, and that reading the lists directory beside a concurrent `maran-php-install` unit
  fails closed rather than yielding a wrong list. The same concurrency question applies to `dnf`
  against its own cache lock.
- Behaviour at scale: the largest realistic pending set against the 4 MiB cap and the 60 s class
  deadline. A host with very many pending packages, or a slow disk, may exhaust either. That is an
  error, not a truncated list. Whether 60 s is wide enough is a measurement the polygon cannot
  give on its own; raising a class is done once, in `call_class.rs`, with its reason, never at the
  call site.
- dnf5. The plan assumes `dnf` 4 on the RHEL family; the fixtures are from AlmaLinux 9.3 only. If a supported release ships a `dnf` whose
  `check-update` exit statuses or `-C` semantics differ, the adapter for that family reads the
  new documentation and the note is edited.
- Timing and load: no measurement of how long an administrator's page refresh plus the badge poll
  keeps a root package manager busy on a small host.
