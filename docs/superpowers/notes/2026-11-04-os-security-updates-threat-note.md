# Threat note: opt-in unattended OS security updates, the hold list and the reboot-required read (issue #163)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to the agent's surface. This note covers what #163 adds. It is
written BEFORE the code: every statement below is a requirement the code must meet, not a description
of code that exists. Where the code lands and differs, the code is wrong or this note is amended in
the same change.

**Second review: OUTSTANDING — must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch `fix/163-os-security-updates` MUST NOT merge to
`main` until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner
is told at hand-off that this surface carries an outstanding review. Nothing here discharges it.

## What is added

1. An opt-in installer step, `installer/lib/68-os-updates.sh`, switched on by the flag
   `--os-security-updates` (exported as `MARAN_OS_SECURITY_UPDATES=yes|no`, default `no`). When on, it
   installs the distribution's own unattended-update package, writes ONE root-owned configuration
   file, and enables the timers the distribution already ships.
2. A hold list, `MARAN_OS_UPDATE_HOLD` in `installer/lib/00-common.sh`: the database engines, the web
   server, the PHP runtimes and the FTP daemon, excluded from the unattended run only.
3. A read-only agent rpc, `GetRebootRequirement`, and a closed classification (`security` or
   `ordinary`) on each entry `ListPendingUpdates` already returns (issue #324).
4. Panel surfaces that read the two: a classification column and filter, a security count, and a
   reboot-required notice. Reads only; they write nothing and add no audit entry, like the existing
   updates endpoints.

Not added: no Maran daemon, no Maran-owned timer or unit, no listening port, no outbound call of
Maran's own (the package manager contacts the mirrors it already uses for every manual run), no
change to the panel's self-update (issues #128 and #383), no rpc that installs, upgrades, holds or
reboots anything. The agent does not apply an update; the distribution's timer does, and only when
the operator opted in at install time.

## 1. The installer step: an opt-in privileged root step

The step installs a package and writes a configuration file as root, so it is an installer privileged
step in the sense of rules/security.md.

- Requirement: with the flag off the step prints `skipped (opt-in: --os-security-updates)` and
  changes nothing: no package, no file, no timer. The default install therefore runs no unattended
  root job it did not run before.
- Requirement: the ONLY inputs are the flag and constants. The flag is a fixed word parsed by the
  installer's own argument loop (an unknown argument stays refused); the hold list is a constant in
  `00-common.sh`; the distribution id and codename on the Debian family are read from the host's own
  `/etc/os-release`, never from an argument, an environment variable the operator controls, or the
  network. No value from the command line, a file the customer can write, or a response ever reaches
  the rendered file. Security.md item 4 is met by there being no caller-supplied value to reject, not
  by escaping one. A codename read from `os-release` is still checked against a positive allow-list
  shape (lowercase letters and digits) before it is written, so a damaged file cannot inject a line.
- Requirement: the file is written the way rules/rust.md "Config writes" says every system config is
  written, in shell: render into memory, write a temporary file BESIDE the target (same directory,
  so the rename is atomic), `fsync`, atomic rename, then validate. Debian family target:
  `/etc/apt/apt.conf.d/52maran-os-security-updates`. RHEL family target: `/etc/dnf/automatic.conf`,
  which `dnf-automatic` reads whole and which has no drop-in directory, so the step writes that file
  whole. The step never appends to it and never edits a line in place.
- Requirement: mode `0644`, owner `root:root`, set on the temporary file BEFORE the rename, so no
  moment exists in which the target is more permissive or owned by someone else. The directory is a
  root-owned system directory; the step does not create it.
- Requirement: replacing `/etc/dnf/automatic.conf` is the one place the step overwrites a file the
  operator may have edited. The step refuses to replace a file that differs from the previous
  Maran-rendered content AND from the package's shipped default without saying so: it prints that
  the file was replaced and where the previous bytes were kept (`<file>.maran-previous`, root-owned
  `0600`). The decision to overwrite is stated, not silent.
- Requirement: validation reads the artefact the system uses, by the name it reads it
  (rules/testing.md "A check must be able to observe what it reports on"). Debian: `apt-config dump`
  must list each key the step wrote with the value the step wrote; a missing key fails the step.
  RHEL: there is no offline validator for `dnf-automatic`; the step re-reads the file it renamed,
  compares it byte for byte with the render, and prints `UNOBSERVED HERE: a real security run` rather
  than implying a check it did not make. `unattended-upgrade --dry-run` is NOT run (it contacts the
  mirrors); `dnf --assumeno -C check-update` is NOT claimed as a validator.
- Requirement: a failure at any point leaves the previous file in place (the rename is the last
  write) and ends the step non-zero with one fixed sentence; the install does not report success for
  a step that did not complete.
- Requirement: the package install uses the installer's existing `pkg_install` with a constant name
  (`unattended-upgrades` on the Debian family; `dnf-automatic` plus the package that provides
  `needs-restarting` on the RHEL family). The name is a constant, so the allow-list of what the step
  can install is the step's own source. No new repository is added and no signature check is relaxed.
- Requirement: the step enables the distribution's own timers (`apt-daily.timer` and
  `apt-daily-upgrade.timer`; `dnf-automatic-install.timer`) and writes no unit file of its own.
  Enabling an existing timer is `systemctl enable --now` with an argv of constants.
- Requirement: `uninstall.sh` removes the Maran-written configuration file and nothing else. It does
  NOT remove `unattended-upgrades` or `dnf-automatic` and does NOT disable the distribution's timers:
  the package and the timers are the customer's system after the install, and a Maran uninstall that
  silently stopped security updates would weaken a server it no longer manages. It prints that.
  Removing the file on the RHEL family restores the package default through the package manager's
  own mechanism, which the uninstaller states it does not do itself (see "could not verify" 6).
- Why a flag and not a default: an unattended root job that changes software on a customer's host is
  a decision the operator makes, not one the installer makes for them. The default is the previous
  behaviour.

## 2. Unattended upgrades: a root job run by the distribution's own timer

After the step runs, `apt-daily-upgrade.timer` or `dnf-automatic-install.timer` starts a root process
that downloads and installs packages on a schedule. This is not a Maran process: it is the
distribution's job, configured by one file Maran wrote.

- **What an attacker with a poisoned or compromised mirror could do.** The same as against any
  `apt-get upgrade` or `dnf upgrade` the operator runs by hand, with one difference of timing: the run
  is unattended, so a malicious package that a mirror served would be installed without a person
  reading the list first. Package signatures are verified by the package manager against the keys
  the distribution installed; Maran does not weaken, bypass or replace that check (`--allow-unauthenticated`,
  `gpgcheck=0`, `localpkg_gpgcheck`, `repo_gpgcheck` are not touched), and nothing in the file turns a
  check off. A mirror that cannot produce a validly signed package cannot get it installed by this
  job. What it can do is replay an old, validly signed package within the metadata's validity window;
  that is the package manager's own residual risk and this note does not claim to remove it.
- **Security-only origins narrow the window, they do not close it.** The Debian file clears the
  distribution's default origin lists (`#clear Unattended-Upgrade::Allowed-Origins;` and
  `#clear Unattended-Upgrade::Origins-Pattern;`) and then allows exactly one origin pattern: `origin=Ubuntu,archive=<codename>-security`
  on Ubuntu (its Release files carry the security archive in the Suite field) and
  `origin=Debian,codename=<codename>-security` on Debian (Codename field), the shapes each
  distribution's own default uses; the origin word is a constant per `ID`. The RHEL file sets
  `upgrade_type = security`. An update that the distribution does not mark as security is not applied
  unattended, so the set of packages an unattended run can change is a fraction of the set a full
  upgrade changes. A compromised security channel is still a compromised channel, and a security
  update is still code run as root.
- **No network facts of Maran's own.** The job contacts the mirrors the package manager is already
  configured for. The installer adds no repository, no proxy, no endpoint and no credential.
  `random_sleep = 3600` on the RHEL family spreads the load; it is a politeness, not a control.
- **Why `Automatic-Reboot` stays off.** `Unattended-Upgrade::Automatic-Reboot "false";` is written
  explicitly rather than left to the package default, so a later change of default cannot turn a
  reboot on. An unattended reboot drops every customer's connections, interrupts a backup or a
  restore in flight, and on a host with an encrypted root volume may leave the server down until a
  human is present. The panel shows reboot-required (surface 4) and the decision stays the
  operator's. The RHEL file never sets `reboot` in `[commands]`, and the step prints that the
  default there is no reboot rather than assuming it (see "could not verify" 7).
- **The interaction with Maran's own package operations.** Two things now install packages as root
  on the same host: the distribution's job, and the agent (`InstallPhpVersion`, via the package
  manager). Both take the package manager's own lock (the dpkg frontend lock and the apt lists lock
  on the Debian family; the rpm and dnf lock on the RHEL family).
  - Requirement: contention is a typed, retryable failure and never a corrupted state. The package
    manager's lock is what prevents two writers; Maran does not remove, steal or ignore it
    (no `rm` of a lock file, no `--force-lock`, no lock-timeout override that waits unboundedly).
  - Requirement: an `InstallPhpVersion` that fails because the unattended job holds the lock is
    classified by the existing `classify_package_failure` (issue #251) as a LOCAL failure, not as an
    unreachable or refused source, so the operator is told to try again rather than that a mirror is
    down. The rpc is idempotent, so the panel's remedy is to ask again (rules/rust.md, "Async and
    blocking"). If the existing classifier cannot tell a held lock from another local failure, the
    change adds that distinction inside the `distro` crate, not in `ops`.
  - Requirement: the converse is bounded too. The unattended job never installs a PHP, database,
    web-server or FTP package (the hold list), so it cannot change the runtime a site is bound to
    while Maran is mid-operation. What it can change are libraries those depend on (for example a TLS
    or compression library); a service that links them keeps the old copy loaded until it restarts,
    which is exactly why reboot-required and "services needing restart" are shown, not hidden.
  - Honest limit: the hold list is by package-name pattern. A security update to a shared library
    that a held daemon uses is NOT held, by design (it is the point of security updates), and the
    daemon keeps running the old library until restarted.
- **No race with the panel's own update.** The panel self-update (#128, #383) is a different
  mechanism; this feature does not touch it. If both ran together the package manager's lock and the
  panel update's own locking serialise them; neither was changed here, and this note does not argue
  the self-update's safety.

## 3. The hold list: a policy, not a mitigation

`MARAN_OS_UPDATE_HOLD` is a single space-separated constant of distribution-native patterns per
family: database engines (`mariadb*`, `mysql*`, `postgresql*`), the web servers (`nginx*`, `apache2*`, `httpd*`), the app
store runtimes (`php*`, `nodejs*`; issue #517) and the FTP daemon (`vsftpd*`). It is written into `Unattended-Upgrade::Package-Blacklist`
on the Debian family and `exclude=` under `[base]` on the RHEL family.

- **What it is for.** An unattended restart of these drops every customer's connections or can
  change a version Maran manages. A database engine upgrade that restarts the server mid-transaction,
  a web-server restart that resets every open connection, a PHP minor bump that changes a version a
  site is pinned to, and a vsftpd restart that ends every active transfer are each an event the
  operator should schedule, not one a timer should choose at an arbitrary hour.
- **What it is not.** It is not a security control and it does not reduce what an attacker can do.
  It is a policy about WHEN a change is made and WHO decides. A held package with a known
  vulnerability stays vulnerable until the operator applies it; the hold list therefore shortens one
  risk (an uninvited restart) by lengthening another (a delay in patching these specific packages).
  That trade is the operator's to accept, and the documentation (`docs/installer/os-security-updates.md`)
  says it in those words rather than presenting the list as protection.
- **Held packages stay pending in the panel.** The hold affects ONLY the unattended run. It is
  written into the unattended-upgrade configuration, not into `apt-mark hold`, `dnf versionlock` or
  an `exclude=` in the main package-manager configuration, so a manual `apt-get upgrade` or
  `dnf upgrade` still sees and applies them, and `ListPendingUpdates` still lists them, classified
  `security` where the distribution says so. Requirement: the list is never turned into a package-manager-wide
  hold, because that would hide a security fix from the one view whose job is to show it. The panel
  showing a held security update as pending is the correct state, not a defect.
- **Pattern width is a cost, stated.** `php*` and `mysql*` match more than the runtime packages
  (names that merely begin with the string). That errs toward holding too much, which delays a patch
  for an unrelated package whose name happens to match; it never errs toward applying a package that
  should have been held. The list is deliberately narrow in categories and generous in patterns.
- **Changing it.** The list is a constant in the installer; changing it is editing the one file and
  re-running the step. The documentation states how, and says that editing the rendered file by hand
  is overwritten by the next run of the step.

## 4. The read-only agent surface: `GetRebootRequirement` and the classification

New rpc `GetRebootRequirement(GetRebootRequirementRequest{})` returns `GetRebootRequirementOk {
requirement }` or `AgentError`; `PendingUpdate` gains `classification`. Additive in the proto
(`PROTO_VERSION` 18 to 19), with an old agent answering `UNSPECIFIED`, which the panel reads once at
its client boundary as "not measured".

Rules/security.md item 12 (a new rpc is a closed typed command, idempotent, re-validated, audited)
applies in this shape:

- Requirement: **closed and typed, no caller input.** The request message has no fields. The
  response is an enum with three values (`UNSPECIFIED`, `NOT_REQUIRED`, `REQUIRED`); the
  classification is an enum with three values (`UNSPECIFIED`, `SECURITY`, `ORDINARY`). Nothing the
  caller sends reaches a path, an argument, a template or a command. Nothing resembling "run this"
  exists on it.
- Requirement: **read-only and idempotent.** It writes nothing, takes none of the agent's mutation
  locks (rules/rust.md "What this agent serialises": it contributes no row and no wait-for edge),
  and asking twice returns the same answer for the same host state. Its cancellation class is
  **stop** (read-only: a dropped stream abandons nothing), stated in its own doc comment, with the
  reason.
- Requirement: **cache-only.** The classification is read from what the package manager already holds
  locally. On the Debian family the simulated-upgrade reading already carries each package's origin
  list, and a suite ending `-security` is `security`, any other `ordinary`. On the RHEL family a
  second constant reading (`check-update --security -C -q`) names the packages the metadata marks as
  security, and the ones it does not name are `ordinary`. `-C` is the package manager's cache-only
  flag; neither reading refreshes metadata and neither contacts a mirror. The agent makes no outbound
  call, so security.md item 10 is unaffected.
- Requirement: **bounded spawn, the only way to spawn.** Every process the rpc starts goes through
  the core's bounded spawn (`agent-core::utils`), with a `CallClass` entry for each in
  `call_class.rs` (a deadline that means "wedged", never "slow"), the child in its own process
  group killed whole, a cap per captured stream, a cleared environment, and an argv array of
  constants. A result is whole or a typed `Bound` error, never a truncated list. The program is an
  `AdapterBinary` only the `distro` crate can construct. The reboot probe has no operand: either a
  file's presence (a descriptor-safe stat of one constant path) or `needs-restarting -r`, with no
  argument that a caller or a package name can influence.
- Requirement: **a failed probe is an error, never "not required".** This is the control that matters
  most on this surface, because the failure that costs something is silent: a panel that shows no
  notice because a probe did not run tells the operator the host is safe to leave. So:
  `needs-restarting -r` exit 0 is `NOT_REQUIRED`, exit 1 is `REQUIRED`, and ANY other status, a spawn
  failure, a deadline or a byte cap is an `AgentError` (`SYSTEM_FAILURE`); the marker-file probe
  treats "file absent" as `NOT_REQUIRED` only when the stat itself succeeded and reported not-found,
  and any other stat error is an error. The missing-program case (the package that provides
  `needs-restarting` not installed) is an error, not `NOT_REQUIRED`. The panel then shows nothing
  rather than a false assurance (it errs quiet), and the operator-facing text for the failure says
  the figure could not be measured.
- Requirement: a failed SECOND reading of the classification does not fail the list. The list
  `ListPendingUpdates` returns is still returned, with every entry `UNSPECIFIED` (not measured), the
  failure logged once at the boundary that handles it, and no entry guessed `ordinary`. "Ordinary"
  is a measured answer and an unmeasured entry never gets it. This is one field with one meaning:
  `SecurityCount` is `null` when it was not measured and a number only when it was.
- Requirement: **no platform literal outside the `distro` crate.** The marker path, `needs-restarting`,
  `--security`, `check-update`, `-C`, suite spellings and the exit-code meanings are all `distro`
  facts behind new methods on `PackageFacts` (a `reboot_probe` that answers a closed type, a reader
  for its status, a second-reading argument list that is `None` where the first reading already
  classifies). `ops` asks the adapter and never writes `/var/run/reboot-required`, `dnf` or `-security`
  itself (rules/architecture.md "Supported systems"). `maran structure` rejects a literal under `ops/`.
- Requirement: **nothing in a customer-facing message.** `GetRebootRequirement` and the updates
  list are administrator-only on the C# side (`AdminOnly`), but the rule is the same one rust.md
  states for every variant: the agent never formats a message for a hosting customer and no path,
  package name, version or tool output goes into an error variant that can reach one. The reboot
  answer carries one enum; the errors carry a fixed kind. Error text from a failed probe is for the
  operator's log and holds an exit status or a line number, never a byte the tool printed.
- Requirement: **the classification parser sees untrusted text.** Package names, versions and
  origin strings come from a mirror's metadata and are not trusted. Each field accepted into a
  `PendingUpdate` passes the existing printable-token test (`is_printable_token`); a line the reader
  does not know is a typed error (`PendingUpdatesFailure`, a line number), not a skip. A package
  name containing a control character cannot reach the wire, the panel's table or the log.
- Audit: reads write no audit entry, matching the existing updates endpoints, which is itself a
  position a reviewer may reasonably challenge: the rpc discloses the host's patch state to whoever
  holds the administrator role. It is administrator-only for that reason, and a customer or a
  reseller never reaches it.
- Rules/security.md item 13: the endpoint is served by the Monitoring module, which already declares
  the agent monitor capability; the change adds no `AgentCapability` value because it adds no service,
  only a method on the monitor service. A reviewer confirms the declared list did not grow.

## 5. The rules/security.md checklist, items 1 to 13

1. **Input.** The step takes a flag and constants; the rpc takes no field; the new list filter
   (`classification`) is a closed value validated server-side against three values and again by the
   typed enum on the agent side if it is forwarded. No value reaches a template, path or SQL.
2. **Paths.** The step writes one fixed path by temp-beside-and-rename; the reboot probe stats one
   constant path through the descriptor-safe helpers, not a string check. No customer-controlled path
   is read or written.
3. **Shell.** No shell string. Installer commands are argv of constants; the agent spawns through the
   bounded core with an `AdapterBinary`; the API spawns nothing.
4. **Config-file injection.** The only value written into a config file that is not a literal is the
   distribution id and codename on the Debian family, read from the host's own `os-release` and
   checked against a positive shape before use; no caller-supplied value is written.
5. **SQL.** None added. No migration, no table.
6. **AuthZ.** The new endpoint is `AdminOnly` and declares its permission like its neighbours; no
   tenant data is involved, so there is no IDOR surface and the tenant census has nothing to add.
7. **Configuration.** One new installer variable, `MARAN_OS_SECURITY_UPDATES`; it gets an entry in
   `installer/panel.env.example` or the installer's documented flag list, with a safe default of
   `no`. No secret.
8. **Secrets.** None are handled. Nothing in a log, an error or a URL carries tool output; failures
   carry an exit status or a fixed word.
9. **Crypto.** None added or changed. Package signature checking is the package manager's and is not
   touched.
10. **Surface.** No new port, no new Maran daemon, no new Maran-owned timer or unit, no new outbound
    call of Maran's own. The distribution's timers talk to the distribution's mirrors, as every
    package-manager run on the host already does; that is stated here rather than assumed, because a
    reviewer reading "unattended updates" may reasonably ask whether item 10 is met. It is met by the
    absence of anything Maran-owned, and the owner may reasonably still want to decide it.
11. **Dependencies.** No Rust or NuGet or npm dependency added. Two distribution packages are
    installed, only when the flag is on: `unattended-upgrades` or `dnf-automatic`, and the package that
    provides `needs-restarting` (justification: they ARE the feature; the alternative is a Maran-written
    updater, which this design avoids).
12. **Agent.** One new rpc, read-only, closed, idempotent, revalidated, with no caller input; audit
    entry deliberately none, argued in surface 4.
13. **Module reach.** No new `AgentCapability`; the Monitoring module's declared list is unchanged.

## What an attacker could do with this surface

- **Control a mirror or its metadata.** They can offer what the package manager's signature check
  accepts and nothing else. The unattended job installs it without a person reading the list, which
  is the cost of the feature. Security-only origins reduce the volume, the hold list removes the
  highest-impact daemons, signature checking is untouched.
- **Write the configuration file.** Needs root; with root they can run the package manager directly.
- **Make the panel say "no reboot needed" when one is.** They would need to make the probe return
  NOT_REQUIRED: on Debian, remove the marker file (root), on RHEL, a `needs-restarting` that exits
  0 (root, or a poisoned package that replaces it, which is already a root compromise). A failure of
  the probe is an error and shows nothing; the false-assurance path requires the probe to run and lie.
- **Learn the patch state through the endpoint.** Requires the administrator role. The data is the
  list of pending updates and a boolean, which is what the existing updates endpoint already shows.
- **A process running as `maran` gains nothing.** No file, socket, sudoers entry or group membership is
  added; the rpc is read-only and takes no input.

## What a reviewer must check

1. The step's file write: temporary file beside the target, mode and owner set before the rename,
   the rename last, and an interrupted run (kill between write and rename) leaves the old file intact.
   Run it twice and confirm the second run converges and rewrites identical bytes.
2. That nothing the step writes comes from a caller: print every variable that reaches the rendered
   text and trace each to a constant or to `os-release` behind its shape check. Feed it a codename
   with a newline and a quote; it must refuse and write nothing, and must still accept a real one
   (inverse control).
3. That the rendered Debian file clears BOTH origin lists and then allows one pattern, by reading
   `apt-config dump` on a real host, not by reading the file: a host where a later file in
   `apt.conf.d` adds an origin back is a host where the `52` file did not win, and the step's
   validation should catch it (the file sorts after `50unattended-upgrades` and before nothing that
   Maran writes; a reviewer checks the ordering claim against the distribution's own file names).
4. That the hold list is not a package-manager-wide hold: after the step, a manual `apt-get upgrade
   --dry-run` or `dnf upgrade --assumeno` on a host with a pending `nginx` update still lists it, and
   `ListPendingUpdates` still returns it.
5. That `Automatic-Reboot` is `false` in the effective configuration and that no `reboot` key is set
   on the RHEL family; and that `uninstall.sh` leaves the package and the timers.
6. The probe: kill the `needs-restarting` binary (rename it), make the marker path unreadable, make the
   spawn time out; every case must produce an `AgentError`, never `NOT_REQUIRED`. The inverse
   control: a real reboot-required host returns `REQUIRED` and a clean host returns `NOT_REQUIRED`.
7. That no platform literal landed under `agent/crates/ops/` (`maran structure` is the gate; the
   reviewer also greps for the marker path, `needs-restarting` and `-security` there), and that every
   new spawn has a `CallClass` row in `call_class.rs`.
8. That an `InstallPhpVersion` run while the unattended job holds the package-manager lock reports a
   retryable local failure and not a refused or unreachable source, on both families.
9. That the SPA, when the probe errors or the classification is unmeasured, shows nothing and not
   "not required" and not a zero.
10. Item 10 of rules/security.md: the owner's decision that an opt-in distribution timer meets the
    "no new outbound calls" rule. This note argues the case but cannot grant it.

## What the author could not verify

1. Nothing in this note has run: the code does not exist yet, and this session cannot run the
   installer, the polygon or a package manager against a mirror. Every statement above is a
   requirement until a gate pass and a reviewer say otherwise.
2. **A real security run, on either family.** The polygon containers have no reachable security
   channel with pending updates. The step's output says `UNOBSERVED HERE: a real security run`; what
   is observed is that the file is present, owned correctly, parsed by `apt-config dump` and that the
   timer is enabled. That the job then applies only security updates and skips the held ones is read
   from the package manager's documentation, not seen.
3. **`dnf check-update --security -C` from the cache alone.** Whether it answers from the local
   metadata without a refresh, on AlmaLinux 9 and 10 and Rocky 9 and 10, must be read from the
   distribution's documentation and recorded from a real container into the fixture; if it cannot,
   the fallback `updateinfo list --security -C` is used and the change says so. Nothing is assumed.
4. **`needs-restarting` availability and exit codes on AlmaLinux 10 and Rocky 10.** On the ninth
   releases the program ships in `dnf-utils` (the `yum-utils` family); whether the same package and
   the same `-r` exit-code contract (0 not required, 1 required) hold on the tenth releases is not
   verified in this repository and must be read from the release's documentation before the probe is
   believed there. A host where it is absent is an error by construction.
5. **Debian 12 and Debian 13 suite spellings.** The Debian reading treats a suite ending `-security`
   as security. The security suite is named `<codename>-security` on the Ubuntu releases and on
   current Debian releases (older Debian used `<codename>/updates`); Ubuntu 22.04 and 24.04 and
   Debian 12 and 13 were not each read from their own documentation here. The `origin` and
   `archive=`/`codename=` keys of `Origins-Pattern` (Ubuntu `origin=Ubuntu,archive=<codename>-security`, Debian `origin=Debian,codename=<codename>-security`)
   have the same dependency. A host whose spelling differs would have an unattended run that applies
   NOTHING (the filter matches no origin) and a panel that classifies everything `ordinary`; that is
   a safe failure (no unexpected upgrade) but a silent one, and the reviewer should check each
   supported release.
6. **The effect of removing `/etc/dnf/automatic.conf` on uninstall.** Whether the package's own
   default is restored, left absent (in which case the timer's job has no configuration and
   behaves as the package says), or recreated by the next package update, was not observed. The
   uninstaller states what it did and does not claim the previous state returned.
7. **The `dnf-automatic` default for rebooting.** Read from the documentation, not observed; the step
   prints that it did not set it.
8. **Lock-contention behaviour.** How `apt-get` and `dnf` behave when another process holds their
   lock (fail fast versus wait) differs by tool and version; what `classify_package_failure` makes
   of each, and whether an `InstallPhpVersion` that meets the unattended job is reported as a local,
   retryable failure, was derived by reading, not by running both at once.
9. **The unit the distribution ships.** That `apt-daily-upgrade.timer` and `dnf-automatic-install.timer`
   exist under those names on every supported release, and that enabling them is the entirety of
   "turn it on" (some releases ship them enabled or masked), is read from the packages, not observed
   on all ten supported systems.
10. **SELinux enforcing on the RHEL family.** The polygon containers do not run it, so the effect of
    a root-written `/etc/dnf/automatic.conf` under the default context, and `restorecon` on it, are
    unexercised.
11. **A kernel, glibc or systemd update landing while customers are connected.** The notice
    shows reboot-required; what services restart themselves on a library update (`needrestart`-style
    behaviour on the Debian family) was not examined, and a host with that package installed may
    restart a held daemon the hold list was meant to protect. The reviewer should check what the
    hosts' defaults do.
