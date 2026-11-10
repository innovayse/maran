# SUSE family support (issue #59): a third package manager and a root job the distribution does not ship — threat note

Status: **Second review: OUTSTANDING (#331)** (rules/security.md "Sensitive change escalation"). This
change gives the installer and the root agent a third family. It touches two surfaces the rules escalate:
the installer's privileged steps (a new arm in each per-family function, running as root on a family
nobody on the team has operated, plus one root-owned timer the installer writes itself) and the root
agent's process surface (a third package manager, `zypper`, whose output the agent parses). The branch
**may land on a branch and MUST NOT merge to `main`** until a second reviewer has read this note and
checked every item under "What a reviewer must check". Author: an agent session. The plan it implements is
`docs/superpowers/plans/2026-11-09-maran-suse.md`; the supported-systems statement is in
`docs/superpowers/specs/2026-08-29-maran-design.md` section 4.

**Nothing about SUSE was measured by us.** Not one container of openSUSE Leap was run while this was
written. Every platform fact below (a path, a package, a unit, an exit status, an output grammar) was read
from the distribution's documentation and is marked UNVERIFIED in the code that states it. Read this note
as a list of claims to be turned into facts or into named failures by the first `leap16` polygon run.

## What is being added, in one paragraph

A `suse` family: a `suse/` adapter folder in the `distro` crate (`suse_adapter.rs` plus concern files
`suse_paths.rs`, `suse_packages.rs`, `suse_package_candidates.rs`, `suse_services.rs`,
`suse_account_programs.rs`, `suse_mail_services.rs`, `suse_dns_services.rs`, `suse_php_extensions.rs`),
one arm in `adapter_for`, the os-release ids `opensuse-leap` and `sles` in the agent's detection, a `suse`
arm in each per-family installer function (steps 20, 30, 68, 70, 80, 85, 86, 87, 88 and the skips in 89),
the uninstaller's counterparts, and a polygon image (`docker/polygon/images/leap16`,
`install-suse.Dockerfile`) that runs the whole installer and the agent suite on openSUSE Leap 16. The
supported pairs are `opensuse-leap:16.0 opensuse-leap:16.1 sles:16.0`; SLES 16 is claimed through its
community sibling Leap 16 (same base), exactly as RHEL is claimed through its rebuilds. No daemon, port or
rpc is added. The ONE wire change is forced: `DISTRO_FAMILY_SUSE = 3` (additive) in
`proto/agent/v1/common.proto`, `AgentDistroFamily.Suse` in the C# client, contract revision 25, because
`AgentInfo.family` is mandatory and the C# client refuses an unknown value at the handshake (owner
decision 221; keeping the family fully inside the agent is impossible).

What the family deliberately does NOT offer (spec section 4):

- **One PHP minor**, the distribution's own (Leap 16: 8.4, package `php8-fpm`). No third-party repository
  is added by the installer (decision 219), so there is no repository key to pin and no new root trust.
- **No FTPS.** vsftpd is not packaged for Leap 16 (decision 222). Step 89-ftps prints a plain sentence and
  exits 0 without touching anything; SFTP is unaffected.
- **No mail.** `suse_packages::dovecot_major` and `key_value_server` answer `None` for every release, so
  the mail stack refuses what it cannot lay out; step 89-mail prints a plain sentence and exits 0.
- DNS (BIND: `bind`, `named.service`, user and group `named`, state `/var/lib/named`) IS provided, from
  best knowledge, unmeasured.

## What an attacker could do with this surface, and why it is safe now

### 1. No new repository, so no new root trust

The first draft of this note (written before the inventory) feared a third-party PHP repository whose
signing key the installer would import. That risk does not exist in the design as built: the only package
source on this family is whatever the host already has configured (the distribution's own repositories).
`installer/lib/20-dependencies.sh` and `30-postgresql.sh` add no repository file and import no key on the
SUSE arm. A compromised third-party repository is therefore not a path to root here.
Safe only if: the review confirms that no `zypper addrepo` / `zypper ar`, no `rpm --import` and no file
under `/etc/zypp/repos.d/` is written by any `suse)` arm (a grep, listed under the reviewer checks).
UNVERIFIED: that the distribution's own repositories on a Leap 16 host carry nginx, MariaDB, PostgreSQL,
PHP 8.4 and the tools the steps install; the first run is what proves each package name.

### 2. The agent parses `zypper` output

Pending updates (issue #324), the security category (issue #163), the reboot probe, the package-candidate
listing (issue #466) and the install-failure classification all read `zypper`. The contract every other
family honours is kept: no byte the tool printed reaches an error variant, a log field a customer sees or
an audit line; a failure carries an exit status or a line number only.

- `suse_packages::read_pending_updates` reads `zypper --non-interactive --no-refresh list-updates`. It
  accepts exactly: two progress sentences, then either `No updates found.` or the header
  `S | Repository | Name | Current Version | Available Version | Arch`, a rule of `-` and `+`, and rows of
  exactly six cells. Name, versions and architecture must pass `is_printable_token`; the repository is
  printable ASCII (spaces allowed, because zypper prints the repository NAME); the status cell is at most
  two printable characters. Any other line fails the whole read with its line number. Only exit statuses 0
  and 100 are successes. "Up to date" is proved by status 0 plus the `No updates found.` sentence plus no
  header; an empty stdout, a header with no row, or status 100 with no row is `IncompleteOutput`, never an
  empty list. A skipped repository (106) or a failed refresh (4) is a failure.
- `suse_package_candidates::read_package_candidates` reads `zypper ... search --details --match-exact -t
  package -- <names>`. Header `S | Name | Type | Version | Arch | Repository`, rows of exactly six cells,
  `Type` equal to `package`. Status 0 must come with at least one row and status 104 with none (an output
  that contradicts its status is a failure, never "nothing is offered"). The operands follow `--`, so a
  name that looks like an option stays an operand. Standard error is not read at all.
- `classify_package_failure` reads a failed install only when the status is 4, 104 or 106 (zypper's own
  error reporting); any other status (a held lock 7, a failed transaction 8, a failed scriptlet 107) is
  `Local`. A refusal needs the full `Server returned an error: 'HTTP/<ver> <3-digit status>` shape; a
  resolve, connect or timeout complaint is `SourceUnreachable`; `Package '<name>' not found.` or
  `No provider of '<name>' found.` at the start of a line, with no source fault beside it, is
  `NotAvailable`. A refusal outweighs an unreachable source in the same run.
- `mark_security_updates` reads no stdout at all. `list-patches` lists advisories, which name no package
  one to one, so every row stays `Unmeasured`; nothing is ever `Ordinary` from a table that was not matched
  (rules: an unread row is never `Ordinary`). Only the exit status (0, 100, 101) is checked.
- The reboot probe is `zypper needs-rebooting` (constant argument): 0 is `NotRequired`, 102 is `Required`,
  any other status is an error, never `NotRequired`.

Safe only if the readers fail closed on everything outside these grammars, which the tests below assert,
and if no test pretends to rest on bytes we never recorded. **The recordings are owed**: the tests use
inline sample text constructed from `man zypper`, named as such; `tests/fixtures/package_manager/` holds no
zypper recording yet.

### 3. Binary paths and the process allow-list

`/usr/bin/zypper`, `/usr/bin/rpm` and the rest are spawned by argv from absolute paths the adapter answers
through `AdapterBinary` (rules/rust.md "Process execution"). A wrong path is not a hole but a silent
failure; `agent/crates/agent/tests/binary_paths_on_a_real_host.rs` must run on the Leap image and its
SUSE arm must FAIL when a declared path is absent (inverse control: a path that exists).

### 4. One firewall owner; SELinux, not AppArmor

- Leap 16 and SLES 16 enable `firewalld` by default (nftables backend). A firewalld running beside the
  agent's `table inet maran` would silently duplicate or override it: a firewall the panel believes is
  applied while another decides. Decision 220: ONE owner, the agent's nftables.
- Step 87 on this family (a) applies the agent's table through the existing path, (b) calls
  `require_ssh_ports_in_loaded_ruleset`, which reads `nft list table inet maran` from the KERNEL (not the
  file it rendered) and stops the install, leaving firewalld running, unless every port of
  `Firewall__SshPorts` appears as a whole word (an empty `Firewall__SshPorts` is also a stop), (c) disables
  firewalld, and only then (d) `mask_firewalld_unit`s it so a package update or a dependency cannot bring it
  back, and (e) re-checks that the Maran table is still loaded afterwards, because firewalld rewrites the
  ruleset as it shuts down. The order is the property: nothing is masked before SSH is confirmed in force.
- The first draft assumed AppArmor. That is true only for SLES 15 and Leap 15, which are not in the
  matrix. Leap 16 and SLES 16 enforce **SELinux** by default (AppArmor is no longer available in SLES 16).
  The installer's existing SELinux handling is keyed on the presence of `restorecon`/`semanage`, not on the
  family, so it runs here too, and step 20 installs `policycoreutils-python-utils` on this family. NOT
  measured: whether `nginx`, `sshd`, `mariadb`, `named` or the PHP pools are denied reading the
  Maran-owned directories under the distribution's policy. That is the same per-daemon problem the RHEL
  family had, and it is measured per daemon here too.

### 5. Detection must not widen by accident

- The installer names the family from the exact ids `opensuse-leap` and `sles`, and falls back to
  `ID_LIKE` `*suse*` for DETECTION only; `10-preflight.sh` then judges the exact `ID:VERSION_ID` pair
  against `MARAN_SUPPORTED_MATRIX` (`opensuse-leap:16.0 opensuse-leap:16.1 sles:16.0`). The agent's
  `os_release.rs` accepts only `opensuse-leap` and `sles`.
- Several ids carry "suse" in `ID_LIKE` and are NOT admitted: `opensuse-tumbleweed` (a rolling release
  has no version to pin), `opensuse-microos` (a read-only transactional root: installs do not persist),
  `sled` (a desktop), `sles_sap` (an SAP product), and `sles` 15.x / Leap 15.x (out of general support, and
  AppArmor-based, which no step was written for). Fedora and Alpine are declined (decision 218) and are
  refused by both.
- `maran structure` check 25 keeps the installer's id list and the agent's detection equal as sets; a new
  structure check compares the families the installer names with the `DistroFamily` variants and the arms
  of `adapter_for` (vacuity-guarded, with an inverse control).

### 6. Unattended OS security updates: a Maran-owned root timer (step 68)

Debian and RHEL use the distribution's own unattended updater (unattended-upgrades; dnf-automatic) and
Maran writes only a configuration file. **SUSE ships no unattended updater**, so on this family step 68
writes a oneshot service and a daily timer of its own. This is new root-executed surface and is the part
of the change that most needs a second pair of eyes.

What it writes (all four through `os_updates_write`: a staged file beside the target, `chown root:root`,
`chmod 0644`, fsync, atomic rename; an existing foreign file is kept once as `<file>.maran-previous` and
never overwritten):

- `/etc/systemd/system/maran-os-security-updates.service`: `Type=oneshot`,
  `Environment=ZYPP_CONF=/etc/zypp/maran-unattended-zypp.conf`,
  `ExecStart=/usr/bin/zypper --non-interactive patch --category security`,
  `SuccessExitStatus=100 101 102 103`. Every byte is a constant in the installer.
- `/etc/systemd/system/maran-os-security-updates.timer`: `OnCalendar=daily`, `RandomizedDelaySec=1h`,
  `Persistent=true`, `WantedBy=timers.target`. Constants.
- `/etc/zypp/maran-unattended.locks`: the hold list (the single exclusion list in `00-common.sh`) as one
  glob lock per validated pattern (`type: package`, `match_type: glob`, `solvable_name: <pattern>`).
  The patterns come from `os_updates_hold_patterns`, which validates every entry.
- `/etc/zypp/maran-unattended-zypp.conf`: a copy of the host's own `zypp.conf` with every active
  `locksfile.path` line dropped and one `locksfile.path = /etc/zypp/maran-unattended.locks` added under
  `[main]`. The copy keeps the distribution's other settings; the kernel multiversion rule matters, since
  without it a patch run would REPLACE the running kernel.

Why a persistent `zypper addlock` was refused: a lock is global, so it would also stop the operator's
manual `zypper update` and would make zypper refuse the installs the product performs itself (the agent
installs a PHP version on request, step 85 the database server), because a locked name cannot be
installed. The hold is a policy about the UNATTENDED run only, so the list lives in a file only that
service reads, selected by the `ZYPP_CONF` environment of that one unit.

What an attacker could do with it, and why it is safe:

- *Make the root job run something else.* The unit's `ExecStart`, environment and timer are constants
  rendered by the installer, written root-owned. Changing them needs root already. The job runs
  `zypper patch --category security` and nothing else: it installs no named package, takes no input from
  the panel or the agent, and the panel has no rpc that writes these files.
- *Plant a lock or a config line through the hold list.* The hold list is the validated list from
  `00-common.sh`, not operator free text; only a constant path is written into the private `zypp.conf`.
  The one thing in that file that is not a constant is a copy of the host's own stock `zypp.conf`, which is
  root-owned input already trusted by every zypper run. If neither `/etc/zypp/zypp.conf` nor
  `/usr/etc/zypp/zypp.conf` exists the step refuses (exit 1), it never guesses.
- *Modify the files as an unprivileged user.* All four are `root:root 0644` (world-readable, root-writable;
  none holds a secret), written by atomic rename, so a reader never sees a half-written file. A file that
  is not Maran's is kept and never overwritten silently.
- *Make unattended updates a regression of the hold list.* An update run that ignores the list would update
  the database engine, the web server or PHP at an hour nobody chose. The step does not assume the lock
  works: `observe_os_updates_zypp_config` runs `zypper locks` under the private configuration and FAILS the
  step unless every held pattern is listed, and reads the unit's `Environment` from systemd and fails
  unless it carries `ZYPP_CONF=...`.
- *Reboot the server.* None of the exit statuses in `SuccessExitStatus` reboots anything; 102 and 103 are
  zypper's own "a reboot / a zypper restart is advised". The step prints that the server is never rebooted
  unattended.
- *The timer outlives Maran.* `uninstall.sh` (`remove_suse_os_security_updates`) stops the timer, then
  removes the four files, each ONLY when it carries the Maran marker; a file without the marker is left in
  place with a note. The distribution's own timers on the other families are deliberately left (they are
  the customer's system after the install); these four are Maran's, so they are taken back.
- *The unit carries no hardening directive* (a package manager must write all of `/usr`), so it is
  deliberately outside the sandbox-ceiling policy for Maran-shipped units; it is a distribution tool run
  with constant arguments, not Maran code.

The step is OFF unless `--os-security-updates` was given (the default `MARAN_OS_SECURITY_UPDATES=no`
changes nothing).

UNVERIFIED (all of it): that libzypp honours `ZYPP_CONF`; that the `locksfile.path` key exists; the locks
file grammar; that `zypper patch` skips a patch whose package is locked; the exit statuses 100-103.

### 7. The installer edits the web server's own `nginx.conf` (step 80)

openSUSE's stock `nginx.conf` is believed to include `vhosts.d/*.conf`, not `conf.d/*.conf`, so a Maran
vhost under `conf.d` would pass `nginx -t` and never be served. Step 80 on this family therefore adds one
line (`include /etc/nginx/conf.d/*.conf;`, preceded by a Maran comment) as the last directive of the `http`
block of `/etc/nginx/nginx.conf`.

- It does nothing when `nginx -T` already shows an active include of `conf.d/*.conf`.
- It renders in memory with `awk` (brace-depth tracked, comments stripped) and refuses, exit 1, if no `http`
  block ends in a lone brace: a file it cannot place the line in is a refusal, never a guess.
- The staged file is created BESIDE `nginx.conf` with the original's owner and mode, renamed into place,
  and `nginx -t` is then run against the real tree; on a refusal, or if `nginx -T` does not list the
  include, the original bytes are copied back and the install stops. The original is kept once as
  `nginx.conf.maran-previous` and never overwritten, so a re-run cannot replace the true original with an
  edited copy.
- After the vhost is installed, `assert_vhost_in_loaded_tree` requires `nginx -T` to list the vhost's own
  `# configuration file <path>:` header: a vhost in a directory nothing includes fails the install by name.
- `uninstall.sh` (`remove_conf_d_include_from_nginx_conf`) drops exactly the comment-and-include pair, keeps
  the result only if `nginx -t` accepts it (otherwise it puts the file back and says so), then removes the
  `.maran-previous` copy.

What an attacker could do: nothing new from the content (a constant line); the risk is a mis-edit that
takes the customer's web server down. Safe because every step is validated by nginx itself, reversible, and
the original is preserved. UNVERIFIED: the real shape of the Leap 16 `nginx.conf`.

### 8. The wire value

`DISTRO_FAMILY_SUSE = 3` is additive; a client that does not know it sees an unknown enum value, which the
C# client refuses at the handshake (an agent of this family never talks to an older panel by accident).
`PROTO_VERSION` is raised to 25 and the compatible window moved, with `contract-baseline.txt` updated.
Nothing else on the wire changes. This deviates from "the family never leaves the agent" and is stated in
the PR and the plan.

## What a reviewer must check

1. No `suse)` arm adds a package repository or imports a key (`grep -n 'addrepo\|zypper ar\|rpm --import\|repos.d' installer/`).
2. The `zypper` readers: every typed failure variant is hit by a test with an inverse; no output byte
   reaches an error variant or a log field; the sample text in the tests is labelled constructed, not
   recorded, and the recording is still owed.
3. Exactly one firewall owner after install: `nft list ruleset` contains `table inet maran`; firewalld is
   masked (`systemctl is-enabled firewalld` says `masked`); the SSH gate stops the install before the mask
   when the port is absent from the loaded table.
4. Step 68's four files: owners and modes (`root:root 0644`), constants only in the unit pair, the hold list
   validated, `zypper locks` under the private config lists every held pattern, uninstall removes all four
   and only marker-bearing files.
5. Step 80's nginx edit: restored on a refused `nginx -t`, original kept once, removed by uninstall, a vhost
   in an unincluded directory fails the install.
6. `maran structure` check 25 and the installer matrix agree; a Tumbleweed `os-release` is refused by BOTH
   preflight and the agent (inverse: Leap is accepted by both); `ID=alpine` and `ID=fedora` are
   `Unsupported`.
7. SELinux result per daemon on the Leap image, with the audit log read (`ausearch -m avc` or
   `journalctl _AUDIT_TYPE=1400`, named in the output).
8. Mutation set (rules/testing.md harness rules; each must kill a NAMED test or a named polygon
   assertion, and a mutation that no test kills is a finding):
   - make the SUSE detection match `opensuse-tumbleweed` (kills
     `opensuse_tumbleweed_is_refused_although_id_like_says_suse`);
   - make `read_pending_updates` return `Ok(vec![])` for a header with no row (kills
     `a_header_with_no_row_is_incomplete_and_never_an_up_to_date_host`);
   - make an unknown zypper exit status classify as a source fault (kills
     `output_is_not_read_as_zyppers_when_it_did_not_exit_with_an_error_status`);
   - make `read_reboot_probe_run` answer `NotRequired` for an unknown status (kills
     `any_other_needs_rebooting_status_is_an_error_and_never_not_required`);
   - make `mark_security_updates` return `Ordinary` rows (kills
     `a_patch_reading_with_nothing_to_name_never_clears_a_row_to_ordinary`);
   - make `dovecot_major` answer `Some` for a Leap release (kills
     `the_mail_stack_is_offered_on_no_release_of_this_family`);
   - remove `mask_firewalld_unit` from step 87, remove `require_ssh_ports_in_loaded_ruleset`, drop the hold
     list from the locks file, drop `ZYPP_CONF` from the unit, and remove the `nginx -t` restore in
     `ensure_conf_d_include_in_nginx_conf` (each kills the polygon assertions in
     `docker/polygon/asserts/parts/suse-family.sh` and `os-security-updates-step.sh`, which first have to
     run on a real image).

## What the author could not verify

EVERYTHING that is platform-specific. At the time of writing no openSUSE container was run, no SLES system
exists to us, and no step below was exercised on a real Leap host:

- That the Leap 16 base image can be pulled and which digest to pin (`leap16/base.txt` names a tag only).
- Every package name (`nginx`, `mariadb`, `postgresql17-server`/`postgresql18-server`, `php8-fpm`,
  `openssh-server`, `cronie`, `at`, `quota`, `nftables`, `bind`, `policycoreutils-python-utils`, the mail
  packages) and every unit name (`nginx.service`, `php-fpm.service`, `mariadb.service`, `sshd.service`,
  `cron.service`, `atd.service`, `named.service`, `postgresql.service`).
- Whether Leap 16's `nginx` package creates the `nginx` user and group, its `nginx.conf` shape (the
  `conf.d` versus `vhosts.d` include), and the `nginx -T` header spelling.
- The PHP layout (`/etc/php8/fpm/php-fpm.d`, `/etc/php8/conf.d`, `/usr/sbin/php-fpm`), that the only
  installable minor is 8.4, and that `php8.3-fpm` is "not found" with exit 104.
- All `zypper` exit statuses (4, 7, 8, 100-107), the table layouts of `list-updates` and `search --details`,
  the `list-patches` shape, that `--` is honoured, and the sentences `No updates found.`,
  `No matching items found.`, `Package '<name>' not found.`, `No provider of '<name>' found.`.
- `zypper needs-rebooting` and its status 102.
- The firewall: where `nftables.service` reads its include file (`/etc/nftables.conf` is answered, matching
  the upstream default), how `nft list table` spells ports, and how firewalld shuts down.
- SELinux enforcing by default on Leap 16 and the per-daemon denials.
- The whole of step 68's SUSE mechanism (`ZYPP_CONF`, `locksfile.path`, the locks grammar, lock honouring).
- That vsftpd is absent from Leap 16 repositories (`zypper search vsftpd`), and the Leap 16 mail packages.
- glibc, systemd and kernel versions (read as 2.40, 257, 6.12; none measured), and the Leap/SLES end-of-
  support dates (secondary sources; some conflict).
- aarch64: nothing ran on Arm hardware; the real-hardware run is owed through issue #53.
- The `leap16` nightly legs (`nightly.yml`, the agent workflow) have never run; their `timeout-minutes` are
  estimates.

## Owner decisions applied

| # | Decision |
|---|---|
| 217 | Rows: openSUSE Leap 16.x only (`opensuse-leap:16.0 opensuse-leap:16.1 sles:16.0`); SLES 16 claimed through Leap; Leap 15.x and SLES 15 are out of support or untestable. |
| 218 | Fedora and Alpine are declined and refused by preflight and by the agent; a refusal test, no support code. |
| 219 | One PHP minor from the distribution's own repositories; no third-party repository. |
| 220 | One firewall owner (the agent's nftables); firewalld stopped, disabled and masked after SSH is confirmed in the loaded ruleset. |
| 221 | The family is internal to the agent except the one handshake field: additive `DISTRO_FAMILY_SUSE = 3`, contract revision 25. |
| 222 | FTPS is skipped on this family with a plain message and exit 0; SFTP is unaffected. |
| 223 | The SUSE polygon runs in the nightly matrix only; PR smoke stays `[ubuntu24, alma9]`. |
| 224 | Scope: v1.0.0. |

Mail is not offered on the family until measured (follows from the decisions; `None` for every release).

Second review: OUTSTANDING (#331)
