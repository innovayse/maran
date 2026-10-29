# Threat note: installing a PHP version through a root oneshot unit (issue #93)

`rules/security.md` ("Sensitive change escalation") requires a threat note and a second reviewer for
a change to the installer's privileged steps or to the agent's privileged paths. This change does
both. The installer gains a new root unit whose only job is to run the family's package manager, and
the agent gains the code path that starts that unit. This note was written **before** any code
changed, from the issue and the tree as they stand on `fix/93-php-package-install-unit` (based on
`dev` at `7e75b67`). A first run of this stage was interrupted. A second run of the same stage then
checked this note against the rules and the code and completed it, also before any code changed:
at that point `git status --porcelain` in the worktree listed this file as the only change.

A third run of the same stage checked it again, still before any code changed (`git status
--porcelain` again listed this file alone). `dev` has since moved to `697cad1`, and
`git diff --stat 7e75b67 dev` touches only `README.md` and `docs/screenshots/`, so nothing this note
cites has moved. That run made three corrections, each marked where it lands:

- **The instance name.** The earlier plan derived it through a *new* `DistroAdapter` method. That
  needs a change to the `distro` crate, which neither part of this branch owns. It also kept a second
  spelling of the package names in the unit files, which can drift from the adapter's. The instance
  is now the package name the adapter **already** answers (`php_package`), and the unit passes `%i`
  to the package manager as the whole package argument (sections 2.2, 2.3 and 3).
- **The contract between the parts** was missing. Section 2.7 now states it, so the two parts can
  be written in parallel.
- **Smaller corrections.** `PrivateNetwork=no` had been listed as a hardening candidate (it is the
  default and hardens nothing). The start rate limit is now a planned directive rather than only a
  review item. The wrong comment's line range is `254-256`. The spec's own wording joins the
  rule-reading question in section 2.6.

A fourth run of the same stage checked it once more, still before any code changed: `git status
--porcelain` listed this file alone, the branch head is still `7e75b67`, and `git diff --stat HEAD
dev` still touches only `README.md` and `docs/screenshots/`. Every line number this note cites was
re-read against the tree. No separate rules-fit write-up for #93 exists in `docs/superpowers/notes/`, so
section 2.6 *is* the rules-fit reading, and the parts implement the option this note chooses. That
run made these additions, each marked where it lands:

- **Comments the change makes untrue outside both parts.** Moving the package manager out of the
  agent leaves `DistroAdapter::package_manager()` with no production caller and a doc comment that
  says it is "for the process-execution allow-list", and it makes the `PATH` justification in
  `agent-core`'s `apply_child_environment.rs` name a spawn that no longer happens. Neither file is
  owned by a part of this branch, and `rules/architecture.md` says a change that leaves such a
  comment behind is not done. Section 2.8 names them and the decision they need.
- **Conffile prompts.** `DEBIAN_FRONTEND=noninteractive` silences debconf, not `dpkg`'s own
  conffile question. Section 2.2 now says what the unit may add for that, and section 5 records it as
  unmeasured.
- **Smaller additions.** The uninstall lines the unit needs are named (section 2.7). The unit must
  carry no mandatory `ReadWritePaths=` under a Maran-owned prefix, because structure check 24 reads
  every `installer/systemd/maran-*.service` (section 2.2). Section 4 item 10 now carries the evidence
  that nothing in the tree builds the installer package. Section 2.6 states plainly that this note
  does **not** conclude a spec change is needed, so the parts know they may proceed.

A fifth run of the same stage re-read the rules (README, git, security, testing, architecture, rust)
and spot-checked the note against the tree, still before any code changed (`git status --porcelain`
lists this file alone; the branch head is still `7e75b67`). `dev` has moved again (`git diff --stat
HEAD dev` lists 46 files; the cited files were read in the worktree itself, which is at `7e75b67`,
so the citations below are true of this branch). Re-confirmed: the false clause is on line 256 of
`maran-agent.service`; `ProtectSystem=true` is line 162 and `ReadWritePaths=` line 311 without
`/usr`; `package_manager()` has exactly two callers (`install_php_version.rs:93` and its test);
`uninstall.sh` has `remove_systemd_units` (line 56) with its `daemon-reload` (line 66);
`70-services.sh` has `refuse_unsubstituted_placeholder` and `install_units`. No correction to the
design was needed. One instruction for the parts, nothing more: re-read every cited line number
before editing, because other sessions move the tree.

**The second-reviewer requirement is OUTSTANDING.** The work is being done by agent sessions, and an
agent session cannot discharge it. This branch MUST NOT merge to `main` until a human second reviewer
has read this note and the diff and signed off. The owner is to be told at hand-off that this
privileged surface is carrying an outstanding review.

## As built (correction)

The sections below describe a `systemctl show --property=Result --property=ExecMainStatus` call on a
failed start. **It was not implemented.** As built, the agent reports only `systemctl start`'s
stderr plus the unit name, a `journalctl -u <unit>` pointer and a re-run-the-installer hint for a
missing unit; `Result` and `ExecMainStatus` are never read. The decision is still the exit status of
`systemctl start` alone. Every passage below that mentions `show`, `Result` or `ExecMainStatus`
(the "Exit status is still reported" paragraph, the pkg-unit contract, the test-plan rows and review
row D) is superseded by this paragraph and describes a design that does not exist.

## 1. The surface today

### What runs, and where

`InstallPhpVersion` is a server-streaming rpc (`agent/crates/agent/src/services/php/php_service.rs`).
The service revalidates the version with `PhpVersion::parse`, then calls
`ops::php::install_php_version` inside `spawn_blocking`. That operation does these things:

1. `ensure_supported`: the version must be one of the closed set `7.4 8.0 8.1 8.2 8.3 8.4`
   (`ops/src/php/supported_versions.rs`).
2. `is_installed`: if the version's pool directory exists, it reports 100% and returns.
3. It derives the package name in the distro crate (`php8.3-fpm` on Debian, `php83-php-fpm` on RHEL)
   and runs `apt-get install -y <pkg>` or `dnf install -y <pkg>` through `spawn_argv`
   (`ops/src/php/install_php_version.rs:91-103`).
4. It runs `systemctl enable --now <php-fpm unit>` (`install_php_version.rs:110-113`).

The package manager is an ordinary `fork`+`execve` child of the agent. It inherits the agent's
mount namespace and everything in `installer/systemd/maran-agent.service`: `ProtectSystem=true`
(line 162, so `/usr` is read-only), `PrivateTmp=`, `PrivateDevices=`, `NoNewPrivileges=`,
`SystemCallFilter=@system-service`, `UMask=0027` (line 401) and the bounding set. Its environment is
the one `apply_child_environment` builds: cleared, then `PATH=/usr/sbin:/usr/bin:/sbin:/bin` and
`LC_ALL=C`, with no `DEBIAN_FRONTEND`.

Who can reach it: `POST /api/v1/sites/php-versions/install` is `AdminOnly`
(`SitesController.cs:211-212`). Anyone who controls the panel process (uid `maran`) can also send the
rpc directly over the agent socket, which accepts that one uid. Neither can name a package. The only
input is a version, and only six values get past `ensure_supported`.

### What an attacker can do with it today

- **Install software of their choosing as root: no.** The package name comes from a closed set of six
  and is passed as a single argv element. No shell is involved and no request field reaches the
  package manager except through `PhpVersion` (digits, one dot, at most two digits per side).
- **But the six strings are not exact names to `apt-get`. Measured.** When no package has exactly
  the name given, `apt-get install` retries the argument as a glob and then as an **unanchored**
  POSIX regular expression, and the `.` in `php8.3-fpm` is a regex metacharacter. Measured in a
  throwaway container of `maran-install-polygon:ubuntu24-x86_64` (apt 2.8.3, run with
  `docker run --rm --entrypoint /bin/bash <image> -c '<command>'`):
  `apt-get install -s -y 'ase-file.'` selects `base-files`, so the match is unanchored at both
  ends and the `.` matched `s`. Inverse control: `apt-get install -s -y 'zzq.-nope'` refuses with
  `Couldn't find any package by regex 'zzq.-nope'`, so the selection above was not a default.
  So on a host whose archive has no `php8.1-fpm`, the request installs any package whose name
  contains `php8<any character>1-fpm`, from repositories the host already trusts. The caller still
  chooses nothing, so this is not a privilege route. But it does weaken two comments.
  `install_php_version.rs:86-90` ("one of six known strings") and the closed-set argument in
  `supported_versions.rs:7-11` both suggest that the argv names exactly one package. To apt it
  names a pattern whenever the exact name is missing. It is also why section 2.3 re-checks
  `is_installed` after the unit succeeds. On the RHEL family `83` carries no metacharacter, and
  `dnf`'s globs need `*`, `?` or `[`, none of which a `PhpVersion` can hold.
- **Remove other packages, possibly.** `-y` lets `apt-get` remove packages that conflict with the
  one being installed. Only essential packages need a further flag. Nothing has been measured about
  which packages a PHP install could displace on a host that also runs the panel's own nginx,
  MySQL and PHP. **Unverified.** Section 2.2 closes it with `--no-remove`. On the RHEL family
  `dnf install` does not erase packages unless it is given `--allowerasing`.
- **Get the install to work: also no, for anyone.** Issue #93 measured this on the Ubuntu 24.04
  install polygon. `dpkg` unpacks into `/usr`, gets `EROFS` on the first file, and the install fails
  every time on both families. So the feature is broken. It is not a privilege leak.
- **Leave the host's package database damaged, possibly.** By the time `dpkg` hits `EROFS`, `apt-get`
  has already updated its lists and taken the `/var/lib/dpkg` lock. An administrator, or a
  compromised panel, can trigger this as often as they like. Nothing has been measured about what it
  leaves behind. If it leaves `dpkg` "interrupted" or a package half-installed, later `apt-get` runs
  refuse to proceed, and that includes the host's unattended **security** updates. That would make a
  broken feature into a security regression on the host. **Unverified.** The "before" measurement
  must answer it (section 4, item 6).
- **Get a false "installed".** `is_installed` checks only whether the pool directory exists. If a
  failed unpack has already created `/etc/php/<v>/fpm/pool.d` (a conffile directory) before it fails
  in `/usr`, the panel's retry reports 100% and success on a host with no php-fpm binary.
  **Unverified.** The same measurement answers it.
- **Persist past a panel reinstall, if the agent is compromised.** Today this does not depend on this
  rpc at all (section 2.4).

### Further claims in the tree that are wrong today

- `maran-agent.service` says the package manager "runs as its own unprivileged-of-this-sandbox
  process, not inside ours". It runs inside ours. The issue cites lines 233-234, but the unit has
  grown since. The clause now runs from "anything" at the end of **line 254** to "not inside ours)"
  on **line 256** (`/usr/bin/grep -n "unprivileged-of-this-sandbox" installer/systemd/maran-agent.service`
  prints `256:`). Lines 233-234 now hold the correct scratch-directory paragraph and must not be
  touched.
- `install_php_version.rs:78-79` says "The repository is provisioned once by the installer". Nothing
  in `installer/` sets up a PHP repository or installs any PHP package. Command (run with
  `/usr/bin/grep`, because the interactive `grep` here is a wrapper that honours ignore files):
  `/usr/bin/grep -rniE 'sury|remi|ondrej|php[0-9.]*-(php-)?fpm' installer/`. It returns two lines,
  both comments (`86-sftp.sh:16`, `10-preflight.sh:198`), and both match only on the generic word
  `php-fpm`. Positive control: `/usr/bin/grep -niE 'remi' docker/polygon/images/alma9/suite.Dockerfile`
  finds the Remi set-up lines (`:40`, `:41`, `:53`). The consequence is
  larger than the comment. On a real install, only versions in the distribution's own archive can be
  installed, and on the RHEL family Remi's `php<NN>-php-fpm` names resolve to nothing at all. That is
  a separate defect from #93 and is **not** fixed by this change (section 5).

## 2. The planned change

### 2.1 What is not done

`/usr` is **not** added to the agent's `ReadWritePaths=`, and `ProtectSystem=` is not loosened. The
agent's sandbox is not widened in any direction. The only change to `maran-agent.service` is the
corrected comment at lines 254-256.

### 2.2 The unit

`installer/systemd/maran-php-install@.service` is a templated `Type=oneshot` unit, shipped by the
installer as `root:root 0644` in `/etc/systemd/system/`. It has no `[Install]` section and is never
enabled, so it runs only when the agent starts it. Planned properties:

- **A fixed `ExecStart=`**: the family's package manager by absolute path (`/usr/bin/apt-get` or
  `/usr/bin/dnf`, the same paths `DistroAdapter::package_manager` answers), `install`, `-y`, the
  no-removal guard below, and then `%i` as the **whole and only** package argument. The installer
  renders the family form, as it already does for the other units (`MARAN_OS_FAMILY` from
  `install.sh`). There is no shell, no `sh -c`, and no `ExecStart=` that takes a program from a
  variable. *Corrected in the third run:* the earlier plan built the package name inside the unit
  from a fixed prefix, `%i` and a fixed suffix. The instance is now the package name itself, as
  `DistroAdapter::php_package` spells it (section 2.3). So the unit holds no copy of the package
  naming, and the agent does not need a new adapter method. What the unit gives up is a constraint on
  the *shape* of what it installs. Section 2.5 explains why that shape constraint never protected
  against anyone who can start the unit, and section 3 keeps the other design for the reviewer.
- **No removals.** On the Debian family the `ExecStart=` also carries `--no-remove`, so an install
  that could only proceed by removing another package aborts instead (section 1). On the RHEL family
  it never carries `--allowerasing`. Both must be measured on the polygon, not assumed from the
  manuals.
- **No `apt-get update` unless it is argued.** The unit runs one fixed command. If the implementer
  finds that installs fail on stale package lists and adds an `ExecStartPre=` for `update` (or
  `dnf makecache`), that is another fixed argv with no instance specifier in it, and the unit's
  comment must say why it is there.
- **`%i`, never `%I`.** `%I` un-escapes `-` into `/`. Every package name the adapter answers
  contains a `-` (`php8.3-fpm`, `php83-php-fpm`), so `%I` would hand the package manager
  `php8.3/fpm`. That is a release selector to `apt-get`, not a package. It would also let an instance
  name spell a path, and `apt-get install` accepts a local `.deb` path. With `%i` the instance arrives
  byte for byte as the agent wrote it.
- **The environment is stated, not inherited.** This unit's process is a child of PID 1, not of the
  agent, so `apply_child_environment` does not apply to it. That function's protections have to be
  restated in the unit:
  - `Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin`. systemd's built-in default starts with
    `/usr/local/sbin:/usr/local/bin`, which are exactly the unmanaged directories
    `apply_child_environment.rs` keeps out of every child's `PATH`. Here the children are maintainer
    scripts running as root, unsandboxed.
  - `Environment=DEBIAN_FRONTEND=noninteractive`, set on purpose on the Debian family as the issue
    asks, rather than relying on debconf's fallback when stdin is closed.
  - `UMask=0022`. Under the agent's `0027`, files a maintainer script creates would be unreadable to
    the web server and php-fpm workers.
  - Standard input is `null`, which is the systemd default.

  None of this comes from `/etc/maran/agent.env`. The unit has no `EnvironmentFile=`.
- **`TimeoutStartSec=` is set explicitly.** For `Type=oneshot` the default is no timeout at all, so a
  stuck mirror would hold the agent's blocking thread forever. The value must be generous. A timeout
  kills `dpkg` part-way, and that is the very state the "before" measurement is checking for. The
  unit's comment must give the number and the reason for it.
- **`RemainAfterExit=no`**, which is the default and is stated. With `yes`, a second `start` of an
  instance that had already succeeded would do nothing, even if the package had since been removed.
- **`StartLimitIntervalSec=0` in `[Unit]`** *(made a plan item in the third run; it was only a
  review question before)*. The default rate limit refuses a sixth start within ten seconds, and the
  refusal (`start-limit-hit`) would reach the operator as a package-manager failure that never ran
  the package manager. Failing fast is exactly when retries cluster: a held `dpkg` lock or an
  unreachable mirror. The agent is the only starter, and the panel's retries are paced by a human or
  a job timeout, so the limit protects nothing here. Disabling it also means the agent needs no
  `reset-failed` step, so it has no second code path to test.
- **No `[Install]` section, no `Alias=`, no `Also=`.** Nothing can enable it or pull it in.
- **Conffile prompts** *(added in the fourth run)*. `DEBIAN_FRONTEND=noninteractive` answers
  debconf's questions. It does not answer `dpkg`'s own question about a configuration file that
  differs from the packaged one. That question can arise when a version is installed again over
  files left behind by an earlier remove that did not purge. With standard input at `null`, `dpkg`
  then fails the configure step and leaves the package half-configured, which is the damaged-database
  state section 1 worries about. If the implementer measures this on the polygon, the unit may add
  the fixed options `-o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold` to the
  Debian `ExecStart=` (keep the local file, take the default otherwise). They are fixed argv with no
  specifier in them, so they stay inside section 2.7's contract. The unit's comment must give the
  reason. They must not be added unmeasured (section 5).
- **No `ReadWritePaths=`** *(added in the fourth run)*. With `ProtectSystem=` off the unit has
  nothing to list, and structure check 24 (`scripts/lib/check-structure.sh:1040-1100`) reads every
  `installer/systemd/maran-*.service`. A mandatory entry under `/var/lib/maran`, `/var/log/maran`,
  `/var/backups/maran` or `/run/maran` would need a step below 70 to create it. No such entry is
  planned, so the check reads the new file and finds nothing to object to.
- **Its own sandbox, and what that sandbox is worth.** The unit has to write `/usr`, `/etc` and
  `/var`, and has to run maintainer scripts that start services. So `ProtectSystem=` is off by
  necessity. Directives that cost the job nothing may be added, but **each one only after it has been
  measured** on both families, never on the strength of "it should be fine" (the agent unit's own
  `MemoryDenyWriteExecute` paragraph explains why). The candidates are `ProtectHome=` (no PHP package
  touches `/home`) and `PrivateTmp=`. *Corrected in the third run:* `PrivateNetwork=no` was listed
  here. It is the default and hardens nothing, and `PrivateNetwork=yes` would cut off the download.
  Network access is therefore a stated requirement of this unit, not a candidate. The services a
  maintainer script starts or restarts (`deb-systemd-invoke`, `systemctl try-restart` in an RPM
  scriptlet) are started by PID 1 in **their own** units, so nothing set here leaks into a running
  php-fpm. Against a malicious package these directives are **not** a boundary: a maintainer script
  running as root can undo any of them. They only guard against accidents.

### 2.3 The agent path

- `install_php_version` no longer runs the package manager. After `ensure_supported` and the
  `is_installed` short-circuit, it runs `systemctl start maran-php-install@<instance>.service` as an
  argv array through the existing seam (`ConfigHost::run`, the supertrait method every `PhpHost`
  has, with the program from `distro.service_manager()`). `systemctl start` on a oneshot unit blocks
  until the unit finishes and exits non-zero if it failed. `--no-block` is not used. The issue
  suggests `systemctl start --wait`. For a `Type=oneshot` unit with `RemainAfterExit=no` a plain
  `start` already returns only when the run is over, so `--wait` adds nothing here. Either form is
  acceptable, but the implementer must measure the exit status of the chosen form for a failed run
  on the polygon. The documentation alone is not enough.
- **The instance name comes from validated types only.** *(Corrected in the third run.)* It is
  `distro.php_package(version.as_str())`, which is the existing adapter method and the same string
  the agent hands `apt-get`/`dnf` today. It is computed from a `PhpVersion` that has passed
  `ensure_supported`. The six possible values per family are `php7.4-fpm` … `php8.4-fpm` on Debian
  and `php74-php-fpm` … `php84-php-fpm` on RHEL (`debian_packages.rs:9-11`,
  `rhel_packages.rs:10-12`). Every one of them uses lowercase letters, digits, `-` and at most one
  `.`, which is a subset of the unit-name alphabet, so the name needs no escaping. None of them can
  carry `/`, `=`, `*`, whitespace or a control character, start with `-` (the option form) or end
  with `-` (apt's removal form). There is no environment file and no per-request file, so nothing
  written at request time can be injected (rules/security.md item 4).

  The unit-name prefix `maran-php-install@` and the suffix `.service` are a Maran fact that is the
  same on every family, not a platform fact. They are therefore a private constant in
  `ops/src/php/`, beside their one caller, and not an adapter method. Structure check 17
  (`scripts/lib/check-structure.sh:733`) rejects only `"/usr/…"`, `"/etc/…"` and package-manager
  names, so it does not fire on them. The precedent `AgentPaths::FTPS_UNIT`
  (`agent-core/src/agent_paths.rs:202`) lives in `agent-core`, which this branch's agent part does
  not own. Moving the constant there is a later tidy-up for whoever owns that file, not a
  requirement.
- **Why `php_package` and not a new adapter method.** The package name already crosses into the
  package manager today, and the adapter is the one place it is spelled. Putting the same name in the
  unit's instance keeps it spelled once. The earlier plan derived `8.3` or `83` and rebuilt the name
  inside the unit, which put a second spelling of Remi's and Sury's naming into shell-rendered unit
  files. That second spelling could drift from the adapter with nothing but the post-install check
  to notice. The earlier plan also needed a `distro` crate change, and neither part owns that crate
  (section 2.7).
- **Exit status is still reported.** If the start fails, the agent reads `Result` and
  `ExecMainStatus` (`systemctl show --property=Result --property=ExecMainStatus <unit>`) and returns
  them in `PhpOpError::PackageManager`, together with the `systemctl start` stderr and the unit name
  an operator can pass to `journalctl -u`. Success and failure are decided **only** by the exit
  status of `systemctl start`, never by what `show` prints. `show` on a unit that never ran (for
  example a template the installer has not shipped yet) prints default values such as
  `Result=success`, so it describes a failure and does not detect one. If `show` itself cannot be
  run, the failure is still reported, without those two fields.
- **No fallback.** If the unit is missing (a host whose installer has not been re-run since this
  change, where `systemctl start` refuses with "Unit … not found"), the agent reports
  `PhpOpError::PackageManager` naming the unit and saying that the installer must be re-run. It does
  **not** fall back to running the package manager in-process. That path is the `EROFS` failure this
  change exists to remove, and it can leave the host's package database interrupted (section 1).
- **Where the tool output goes.** The package manager's own stderr now goes to that unit's journal
  instead of to the agent. This is a
  real loss of in-panel detail, and it is accepted on purpose (section 3). The text is for operators
  only and never reaches a customer (rules/rust.md "Errors"), and the rpc is administrator-only in any
  case.
- **Success is checked, not assumed.** After the unit succeeds, the agent asks `is_installed` again.
  If the pool directory is still missing, that is a typed failure, not success. Since the third run
  the unit no longer spells the package name itself, so there is no unit-to-adapter drift left for
  this check to catch. What it catches now is two other cases. One is apt's regex fallback (section
  1) installing some *other* package whose name matched. The other is a package that installed
  without creating the directory the adapter expects. Both would otherwise be reported as success
  for a version the panel then cannot list.
- **Progress is still reported.** The stages (`preparing`, `download`, `install`, `enable`) and their
  percentages stay as they are. They were already markers around one blocking call, not measurements
  of `apt-get`. The doc comment must keep saying so.
- `systemctl enable --now <php-fpm unit>` stays in the agent. PID 1 carries it out over its private
  socket, and it was never affected by `/usr` being read-only.
- **Cancellation class is unchanged: run to completion.** It now holds more strongly than before.
  The package manager runs in its own unit's cgroup, so a restart of the agent (whose `KillMode` is
  the default, `control-group`), or a dropped client, no longer kills `dpkg` part-way. Today it does.

### 2.4 Why this does not widen what a compromised agent can do

The agent can already get code run outside its sandbox, and it does not need this unit to do so.
`ProtectSystem=true` leaves `/etc` writable. The bounding set has `CAP_DAC_OVERRIDE`. The agent
already drives PID 1 (`systemctl enable --now`, `restart`, reloads). So a compromised agent can write
any unit file into `/etc/systemd/system/`, `daemon-reload`, and start it, and that unit can write to
`/usr`. It could also write `/etc/ld.so.preload`. This conclusion comes from reading the unit's
directives. **It has not been measured.** A reviewer should measure it (section 4, item 8).

So the template unit gives a compromised agent nothing it lacked. What it gives the *correct* agent
is one named, fixed route that can be reviewed: six possible instance values, one argv form, an
environment stated in the unit. The agent's sandbox keeps the value it really has, which is
containing the agent's *own* mistakes, such as a path bug that writes into `/usr`.

For the same reason, the claim at `maran-agent.service:155-158`, that "a compromised agent cannot
overwrite the system's binaries or libraries, cannot replace its own /usr/local/maran/agent binary,
and cannot install a boot-time payload", already overstates what the sandbox does, before this change
and independently of it. This unit's scope covers only the comment at lines 254-256. The 155-158
paragraph is **flagged for the reviewer and the owner**: it describes a protection the unit does not
give, and that is the kind of comment `rules/architecture.md` treats as a defect.

### 2.5 Who can start the unit besides the agent

Only uid 0, or a caller that polkit authorises for `org.freedesktop.systemd1.manage-units`, which by
default means authenticating as an administrator. The panel uid cannot: `maran-api.service` is
unprivileged and has no polkit grant, and this change ships no polkit rule. A root caller can start
an instance outside the closed set (`maran-php-install@7.3.service`), because the unit does not
re-check anything. *(Corrected in the third run.)* Because the instance is now the whole package
argument, such a caller can install **any** package by name (`maran-php-install@htop.service`) from
repositories the host already trusts, with the unit's no-removal guard still in force. Under the
earlier prefix and suffix design the same caller could get only a `php<instance>-fpm`-shaped name.
That difference protects against no one. The only principals who can start the unit are uid 0 and
polkit-authorised administrators. Both can run `apt-get`/`dnf` directly, and a compromised agent can
write and start a unit of its own (section 2.4). So this gives root nothing new. It is stated here
so that nobody mistakes the unit for the place where the closed set is enforced. The closed set is
enforced in the agent, by `ensure_supported`, and the unit is an "install one named package, remove
nothing" route that only the agent is expected to use.

Searches behind this paragraph: `/usr/bin/grep -rniE 'polkit|pkla' installer/` finds nothing (exit
1). Positive control: the same pattern over `docs/superpowers/notes/` finds this note.

### 2.6 Is this a new daemon? A rule-reading question for the owner

`rules/architecture.md` says the system is "exactly three processes" and "MUST NOT add daemons", and
that "only `maran-agent` mutates the system". `rules/security.md` item 10 says "no new daemons". The
author reads the template unit as compliant with all three, for these reasons:

- It listens on nothing.
- It is never enabled, so nothing starts it at boot.
- It exits when the package manager exits.
- It runs only when the agent asks PID 1 to start it.
- The process it runs is the same package manager the agent already spawns today. The only change
  is which parent starts it.

`installer/systemd/maran-ftps.service` (shipped disabled by `89-ftps.sh`) is the existing precedent
for a root unit outside the three processes, and that one *is* a daemon. Even so, the author is not
the person to settle whether "process" in the architecture rule means "long-running process". This
reading is **flagged for the owner and the reviewer**. If they read the rule differently, the
alternative in section 3 that runs the agent binary as the `ExecStart=` does not escape the
question, because it is also a second root process.

The spec says the same thing in its own words, and the reviewer weighs those too
(`docs/superpowers/specs/2026-08-29-maran-design.md`). §2 item 4 limits the *daemons* on a customer's
server to PostgreSQL, the API and the agent. §5 calls the agent "the only root process". §11 says
PHP versions are installed "by the agent on demand". The first is about daemons, and the unit is not
one. The second has never been literally true: today's `apt-get` and its maintainer scripts are root
processes too, as are `useradd`, `nft` and every other tool the agent spawns. The third still holds,
because the agent decides and orders the install, and nothing else can order it through the panel.
If the owner reads any of the three otherwise, this is a spec question (rules/security.md item 10:
"requires a spec change first, not a PR"). The code parts must then stop until it is answered.

**What this note concludes for the parts** *(stated in the fourth run)*: it does **not** conclude
that the option needs a spec change. The author's reading is that a never-enabled oneshot unit that
listens on nothing is not a daemon. The parts may therefore implement it. The reading is still
flagged for the owner and the reviewer, and it is section 4 item 14. If the owner rules the other
way, the unit and its installer lines come out again, and the branch carries the corrected comments
only.

### 2.7 The contract between the parts *(added in the third run)*

Two parts write this branch in parallel. **php-agent** owns `agent/crates/ops/src/php/**` and
`agent/crates/agent/src/services/php/**`. **pkg-unit** owns the new unit under `installer/systemd/`,
the installer lines that install and remove it, and the comment at `maran-agent.service:254-256`.
Neither part edits the other's files, and neither edits the `distro` crate or `agent-core`. Each part
depends on exactly the following, and nothing else.

**What php-agent relies on pkg-unit to provide:**

- **Template file in the repository:** `installer/systemd/maran-php-install@.service`.
- **Installed as** `/etc/systemd/system/maran-php-install@.service`, `root:root 0644`, by step 70
  (`install_units`, then the existing `systemctl daemon-reload` in `step_services`, both in
  `installer/lib/70-services.sh`). It is never enabled. *(Added in the fourth run.)* The template is
  rendered at install time, as the other units are, by a function in `70-services.sh` that fills in
  the family's `ExecStart=` and `Environment=` lines, chosen on `MARAN_OS_FAMILY` (set by
  `install.sh:268-284`). Like its neighbours it ends in `refuse_unsubstituted_placeholder`. A family
  other than `debian` or `rhel` is a refusal, not a default. `installer/uninstall.sh` removes it in
  `remove_systemd_units` (line 56), before that function's `systemctl daemon-reload` (line 66). The
  unit is never enabled, so `stop_and_disable_services` (line 50) needs no `disable` line for it. A
  stop of any running instance before the file is removed is pkg-unit's choice.
- **Instance accepted:** the exact string `DistroAdapter::php_package(version)` returns. That is
  `php<M>.<m>-fpm` on Debian and `php<M><m>-php-fpm` on RHEL.
- **How the instance is used:** `%i` (never `%I`), as the last and only package argument of
  `ExecStart=`.
- **`ExecStart=`, Debian render:** `/usr/bin/apt-get install -y --no-remove %i`.
- **`ExecStart=`, RHEL render:** `/usr/bin/dnf install -y %i`, with no `--allowerasing`.
- **Unit type and lifetime:** `Type=oneshot`, `RemainAfterExit=no`, no `[Install]`,
  `StartLimitIntervalSec=0` in `[Unit]`, and `TimeoutStartSec=` set explicitly, with its value
  argued in the unit's comment.
- **Environment:** `Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin`, plus
  `Environment=DEBIAN_FRONTEND=noninteractive` in the Debian render, and `UMask=0022`. There is no
  `EnvironmentFile=`, and standard input stays at the default, `null`.
- **Exit semantics:** `ExecStart=` exits 0 only when the package manager installed the package, so
  `systemctl start` exits 0 only then.

The order of the options inside `ExecStart=` is pkg-unit's choice, provided `%i` stays the last
argument. Any `ExecStartPre=` pkg-unit adds must be a fixed argv that contains no specifier, with
its reason written in the unit (section 2.2). If pkg-unit has to change anything in this table, it
says so in its result before php-agent's code depends on it.

**What pkg-unit relies on php-agent to do:**

- Start the unit only as the argv `[distro.service_manager(), "start", "maran-php-install@" +
  distro.php_package(version.as_str()) + ".service"]`, through `PhpHost`'s `ConfigHost::run`, and
  only after `ensure_supported` and the `is_installed` short-circuit. Never use `--no-block`, never
  use `systemd-run`, and never write any file the unit reads.
- On a non-zero start, run `[service_manager, "show", "--property=Result", "--property=ExecMainStatus",
  <unit>]` for the report only, and return `PhpOpError::PackageManager` naming the unit. Never fall
  back to running the package manager in-process.
- After a zero start, ask `is_installed` again, and treat "still not installed" as a failure.
- Keep `systemctl enable --now <php_fpm_service>` (`PhpOpError::ServiceEnable`), the four progress
  stages, the existing `PhpOpError` variants (no new variant is needed; if php-agent finds that one
  is, it says so, because the C# side maps variants through the wire `ErrorCode`), and the "run to
  completion" class in the service's doc comment.
- Correct the two doc comments section 1 found wrong in its own area: `install_php_version.rs:76-80`
  (the repository "provisioned once by the installer") and `:86-90` / `supported_versions.rs:5-11`
  (the exact-package-name claim under apt's regex fallback).

**Shared by both, written in both and checked by section 4 item 15:** the unit name
`maran-php-install@<instance>.service`. It is spelled once in Rust (a private constant in
`ops/src/php/`) and once in shell (the template file name, the install line and the uninstall line).

**Outside both parts, and owed by the test pass that follows the code (rules/testing.md, "tests
after"):** `agent/crates/ops/src/tests/php/install_php_version_tests.rs` asserts today's argv
(`installing_spawns_the_package_manager_from_the_adapter_as_an_argv_array`, line 55), so it will go
red. The same goes for anything in `ops/src/tests/php/fake_php_host.rs` that cannot answer "not
installed" before the install and "installed" after it. Neither file matches either part's glob.
The proto contract is untouched: no rpc, message or field changes, so `maran proto` has nothing to
accept.

### 2.8 Comments the change makes untrue, outside both parts *(added in the fourth run)*

`rules/architecture.md` says a doc comment that describes what the code does not do is a defect as
serious as the behaviour it gets wrong, "fixed in the same change or the change is not done". Moving
the package manager out of the agent makes three comments untrue, and no part of this branch owns
the files they are in:

- **`agent/crates/distro/src/adapter.rs:133-139`**, `DistroAdapter::package_manager()`, says it
  is the binary "for the process-execution allow-list". After the change the agent spawns no package
  manager, so the method has no production caller left. Command:
  `/usr/bin/grep -rn "\.package_manager()" agent/crates --include=*.rs` prints
  `install_php_version.rs:93`, which this change removes, and `install_php_version_tests.rs:66`.
  Positive control: the same search for `\.service_manager()` over `agent/crates/ops/src/php` finds
  three callers. The binary path is now also spelled in the unit's `ExecStart=`. So the adapter's
  copy becomes a second spelling that nothing reads and nothing ties to the unit except review item
  15. The method can be retired, which is a `distro` crate change. Or its doc can say that it names
  the binary the installer's unit runs and is kept identical to that unit by review. The family
  files `debian_packages.rs:13-16` and `rhel_packages.rs:14-17` stay true: Sury and Remi are still
  driven through those binaries, now by the unit.
- **`agent/crates/agent-core/src/utils/apply_child_environment.rs:30-37`**, the doc of
  `PATH_VALUE`, justifies the child `PATH` by saying that "`apt-get` and `dnf` run packaged helpers
  and maintainer scripts by bare name". After the change no package manager is a child of the agent.
  The `PATH` itself is still needed by other children that start programs by bare name. The same
  file says `tar`'s `--use-compress-program` forks `/bin/sh -c`. So only the example becomes
  untrue, not the setting. The unit now sets the same `PATH` for the package manager itself
  (section 2.2).
- **`agent/crates/ops/src/tests/php/fake_php_host.rs:5`** says "the real host runs `apt-get`".
  This is test code, so it belongs to the test pass along with the assertions above.

**The orchestrator decides** whether a doc-only touch of the first two files joins this branch, or
whether they are recorded as open items blocking "done". Neither part may edit them on its own. The
first one is the choice between retiring a trait method and re-documenting it, and the owner should
make it.

## 3. Alternatives considered

- **Add `/usr` to `ReadWritePaths=`.** Refused, as the issue proposes. It would remove the one
  protection `ProtectSystem=true` gives the agent against its own mistakes, for every rpc and not
  just this one.
- **Use the agent binary as the `ExecStart=`, in an "install a PHP version" mode that re-validates
  `%i`.** This would keep package naming in one place (the distro crate) and would enforce the closed
  set inside the unit. It was not chosen. It adds a second command-line entry point to the only root
  binary and runs the whole agent unsandboxed, which is a much larger thing to review than a
  three-word `ExecStart=`. It would also route `apt-get` back through `apply_child_environment`, which
  clears `DEBIAN_FRONTEND` unless that function's list grows. The drift it would prevent is caught
  instead by the `is_installed` check after the install (section 2.3). If a reviewer prefers this
  option, this note must be amended **before** the code changes.
- **A new `DistroAdapter` method answering a version-only instance (`8.3` or `83`), with the unit
  rebuilding the package name from a fixed prefix, `%i` and a fixed suffix.** This was the plan until
  the third run. It is set aside for three reasons. It needs a change to the `distro` crate (trait,
  both adapters and their tests), and neither part of this branch owns that crate. It spells the
  Sury and Remi package names a second time, in shell-rendered unit files that nothing compiles
  against the adapter. And what it buys over the chosen design, a unit that will install only
  php-fpm-shaped names, protects against no principal who can start the unit (section 2.5). Its one
  real merit is that the unit text names what it installs, so a reader of the unit file sees "PHP".
  If the reviewer values that, the orchestrator must add a `distro` part to this branch before any
  code is written, and section 2.7 changes with it.
- **Write a per-request environment file that the unit reads.** Refused. It adds a root-written file
  as a channel for a value that fits in the instance name, and brings the config-file-injection rule
  into play for nothing.
- **`systemd-run` with a transient unit.** Refused. The unit's properties would then live in the
  agent's argv instead of a file shipped and reviewed with the installer. The issue asks for a unit
  with "its own reviewed sandbox", and a transient one has none that anyone reviews.

## 4. What a second reviewer must check

1. **The agent's sandbox did not move.** `git diff dev -- installer/systemd/maran-agent.service`
   changes comment lines only. `ReadWritePaths=` (line 311) has no `/usr` entry, and
   `ProtectSystem=true` is unchanged. The issue also asks for a polygon assertion that the *installed*
   unit lists no `/usr` in `ReadWritePaths=`. It must read the installed file or
   `systemctl show -p ReadWritePaths maran-agent`, not the source text.
2. **The `ExecStart=` of the template unit, as installed on each family.** Check for an absolute
   path, no shell, and `%i` (not `%I`) as the last and only package argument. Check that the agent
   can produce no instance other than the twelve `php_package` values in section 2.3. Those values
   cannot become an option (a leading `-`), a removal (apt's trailing `-`), a local package path (a
   `/`) or a version pin (an `=`). For every other instance, decide whether the "only root can start
   it" argument of section 2.5 holds on this host. Confirm `--no-remove` is present on the Debian
   family and `--allowerasing` is absent on the RHEL family. Then show on the polygon that an
   install which would need a removal is refused. Finally, check that a root-started
   `maran-php-install@-…` instance cannot make the package manager read options it would not
   otherwise accept, such as `-o`/`--option` on apt or `--setopt` on dnf. If it can, decide whether
   that matters given section 2.5.
3. **The environment the package manager really ran with.** Read it from `/proc/<pid>/environ` of
   the running `apt-get`/`dnf` (or a maintainer script) on the polygon, not from the unit text
   (rules/testing.md, "a gate observes the artefact the system actually uses"). Expected:
   `DEBIAN_FRONTEND=noninteractive` on Debian, a `PATH` with no `/usr/local`, and nothing from
   `agent.env`. Also check the process's umask (`/proc/<pid>/status`, `Umask:`).
4. **The oneshot semantics.** Check that `RemainAfterExit` is not `yes` and that `TimeoutStartSec=`
   is set and argued. Check that `StartLimitIntervalSec=0` is in `[Unit]` and works on the oldest
   systemd in the matrix: fail an instance six times inside ten seconds on the polygon, then show
   that the seventh start runs the package manager rather than reporting `start-limit-hit`.
5. **The agent path.** Check that the unit name is `maran-php-install@` + `distro.php_package(…)` +
   `.service`, computed only from a `PhpVersion` after `ensure_supported`, with no platform literal in
   `ops`. Check that the argv is `[service_manager, "start", <unit>]`, with no `--no-block`. Check
   that a failure reports `Result` and `ExecMainStatus`, and that the success decision never reads
   `show`'s output. Check that a missing unit is a typed failure that names the installer and does
   not fall back to an in-process package manager. Check that success is re-checked with
   `is_installed`. Check that every new error path has a unit test (rules/testing.md: every typed
   error variant appears in a test).
6. **Before and after on a throwaway systemd polygon, both families.** Before: today's agent unit
   reproduces the `EROFS` failure. After that failure, record `dpkg --audit`, `apt-get check` and
   whether `/etc/php/<v>/fpm/pool.d` exists. This answers the two "unverified" items in section 1.
   After: the same request installs the version, `php-fpm<v>` is active, and the pool directory
   exists. Inverse control: the after-run must also be shown failing when the template unit is
   removed. Otherwise the success may have come from somewhere else.
7. **The process boundary is real.** While an install is running, `systemctl restart maran-agent`
   must leave the package manager running to completion. Find it in the template unit's cgroup
   (`systemctl status maran-php-install@<i>`), not the agent's.
8. **Section 2.4's claim, measured.** Show that a process with the agent's unit properties can create
   and start a unit that writes into `/usr`. If it cannot, section 2.4 is wrong and this note must be
   corrected before merge, because the "does not widen" argument would then need a different basis.
9. **Concurrency.** Two installs of the same version should be one job. Two different versions
   should hit the `dpkg`/`rpm` lock, and the second should fail in a typed way, not hang past
   `TimeoutStartSec=`.
10. **Installer and packaging.** The unit is installed `root:root 0644` with a `daemon-reload`, is
    never enabled (no `.wants` link), is removed by `installer/uninstall.sh`, is included in
    whatever ships `installer/` (the release bundle carries api, agent and frontend, not the
    installer itself, per `scripts/lib/release-bundle.sh:20-27`), and passes `maran structure`
    check 24, which globs `installer/systemd/maran-*.service`. The installer package that
    `installer/get.sh:32` downloads (`maran-installer.tar.gz`) is built by nothing in this
    repository: `/usr/bin/grep -rn "installer.tar.gz" scripts .github` finds nothing (exit 1).
    Positive control: the same pattern against `installer/get.sh` matches lines 32 and 52. So
    "shipped" means "present under `installer/systemd/`", and whoever builds that package must
    take the whole directory. The reviewer confirms that no list of files somewhere outside this
    repository needs the new unit added to it. *(Fourth run.)* `installer/get.sh:33` says its
    checksum is "Replaced at publish time by `maran release publish-get`", and no such command
    exists in this tree: `/usr/bin/grep -rn "publish-get" scripts .github` finds nothing (exit 1).
    Positive control: the same pattern against `installer/get.sh` matches line 33. So the publish
    step lives outside this repository too, and that is where the "whole directory" assumption has
    to be confirmed. The install polygon copies the whole of `installer/` into the container
    (`scripts/lib/install-polygon.sh:176` and `:184`), so the polygon proofs in items 6 and 15 do
    see the new unit with no packaging change.
11. **The comment corrections.** The false sentence at `maran-agent.service:254-256` is corrected.
    The correct paragraph at 233-234 is untouched. The decision on the 155-158 paragraph (section
    2.4) is recorded either way.
12. **The repository gap is recorded, not hidden.** The false sentence at
    `install_php_version.rs:78-79` is corrected, and the missing repository provisioning
    (section 1) is tracked separately, not reported as fixed by this branch.
13. **The regex fallback (section 1).** The comments at `install_php_version.rs:86-90` and
    `supported_versions.rs:7-11` no longer claim that the argv holds an exact package name, or
    they say when it does. The reviewer re-runs the `ase-file.` probe and its `zzq.-nope` inverse
    control on each supported Debian-family release. Its apt version may differ from 2.8.3.
14. **The reading of the three-process rule (section 2.6).** The owner or the reviewer records
    whether a oneshot unit is a "daemon" under `rules/architecture.md` and `rules/security.md`
    item 10. If it is, this design is out of rules, whatever its security merits.
15. **The two parts agree (section 2.7).** Compare the unit name in the Rust constant, the file name
    under `installer/systemd/`, the name `70-services.sh` installs and the name `uninstall.sh`
    removes, as four strings. Then check the same name on the polygon:
    `systemctl cat maran-php-install@php8.3-fpm.service` must print the installed template.
16. **The comments outside both parts (section 2.8)** *(fourth run)*. Check that the doc of
    `DistroAdapter::package_manager()` and the `PATH_VALUE` doc in `apply_child_environment.rs` were
    corrected, or the method retired, in this branch. If they were not, the open item must be
    recorded with the owner, because the branch is not done while they describe a spawn that no
    longer happens. If the Debian `ExecStart=` gained the `Dpkg::Options` pair (section 2.2,
    "Conffile prompts"), check that the unit's comment gives the measurement behind it.

## 5. What the author cannot verify

- **The second review itself.** An agent session cannot obtain one. That is why it is OUTSTANDING
  above.
- **The RHEL family on a product-provisioned host.** The installer provisions no Remi repository, so
  on a real RHEL-family install `dnf install -y php83-php-fpm` finds no package, whatever sandbox it
  runs in. A RHEL "after" proof in this branch is only possible by adding EPEL and Remi to the
  throwaway container by hand. That proves the unit, not the product. The same applies on Debian to
  any version outside the distribution's own archive. Which versions each supported release ships
  natively has **not** been measured for this note.
- **SELinux.** Polygon containers do not run SELinux in enforcing mode. Whether a confined RHEL host
  lets `rpm` scriptlets run from a unit started this way is unobserved.
- **Every supported OS and both architectures.** Proofs run on the polygons that exist
  (`docker images | grep polygon`), on x86_64. They are containers with real systemd as PID 1, not
  virtual machines. When this note was written, that listing showed `maran-install-polygon` images
  for ubuntu22/24/26, debian12/13, alma8/9/10, rocky8/9 and oracle8/9/10. The RHEL family can
  therefore be exercised, subject to the Remi caveat above. Only `alma9` also has an `aarch64` tag.
- **The regex fallback beyond one release.** It was measured on apt 2.8.3 (Ubuntu 24.04) only.
  Whether Debian 12/13 and Ubuntu 22.04 behave the same way is not measured.
- **What the packages do.** After this change, a version's maintainer scripts run as root,
  unsandboxed. A compromised package repository therefore means root on every host that installs from
  it. That trust is inherent in installing packages, and the hosts already extend it to their
  distribution's archive. Today it is masked only because the install never gets far enough to run a
  script. This change makes it real, and no unit directive can contain it.
- **Operator-added polkit rules** that let some group manage units. Such a rule would let that group
  start instances of this unit, and the author cannot see it from the repository. Since the third
  run, an instance like that can name any package (section 2.5).
- **The systemd behaviour section 2.3 relies on, per release.** Section 2.3 relies on four things.
  `systemctl start` must block until a `Type=oneshot` unit has finished, and must exit non-zero when
  it failed. `show` must print defaults (`Result=success`) for a template instance that never ran.
  `StartLimitIntervalSec=0` must disable the limit. And "Unit … not found" must be the refusal for a
  missing template. All four come from the documentation. None has been measured on the systemd
  versions the matrix ships (from Ubuntu 22.04's to AlmaLinux 10's), and section 4 items 4 to 6
  are where they get measured.
- **Whether the agent's own `systemctl` waits are bounded.** `spawn_argv` puts no deadline on its
  child, so the agent's blocking thread waits for as long as `TimeoutStartSec=` lets the unit run.
  That is the reason section 2.2 requires the timeout. Nothing has measured what an agent thread
  pool held by several long installs does to the other rpcs.
- **Conffile prompts under a unit** *(fourth run)*. Section 2.2 relies on how `dpkg` behaves
  when a conffile question arises and standard input is `null`. The author expects it to fail the
  configure step, from how `dpkg` is documented to ask. Nobody has measured it, on any release,
  and nobody has measured whether `--force-confdef`/`--force-confold` change that answer. Until
  that is measured, the unit should not carry those options.
- **The panel side of a long install** *(fourth run)*. `GrpcPhpServiceInvoker.cs:34` opens the
  stream with the caller's cancellation token and sets no `deadline:` argument. The resilience
  wrapper, `backend/src/Maran.Host/Resilience/ResilientAgentPhpClient.cs:14` and `:49-53`, passes
  the install straight through and does not apply `AgentOperationPipeline`'s operation timeout
  (`AgentOptions.OperationTimeoutSeconds`, 30 by default). Command:
  `/usr/bin/grep -rn "deadline:" backend/src --include=*.cs` prints nothing, so no call in the
  backend passes a gRPC deadline. Positive control: `/usr/bin/grep -n "cancellationToken:"
  backend/src/Maran.Agent.Client/Services/PhpService/GrpcPhpServiceInvoker.cs` matches line 34, the
  install call itself, so the search can see that call's named arguments. The panel's wait is
  therefore bounded by the HTTP request that started it, not by `TimeoutStartSec=`. Because of the
  run-to-completion class, a request that gives up does not stop the install, and the idempotent
  retry reports it once it has landed. Nobody has measured what the administrator sees while an
  install outlasts their request.

## 6. Independent agent review of the implementation *(added at the review stage)*

Five agent reviewers (correctness, security, rules, compatibility, design) read the Phase A
implementation. They are agents, so this section **does not discharge the second-reviewer
requirement**. It records what they found and the decision taken on each finding, so the human
reviewer reads a decision list rather than five separate reports. Fixers apply the decisions below;
this section records the decision, not the diff. Phase A ran no build, test or measurement, so every
item the note calls unmeasured is still unmeasured.

**The second-reviewer requirement remains OUTSTANDING.** This branch MUST NOT merge to `main` until
a human second reviewer has read this note and the diff and signed off, and the owner has confirmed
the reading of section 2.6 (a never-enabled oneshot unit is not a "daemon" under
`rules/architecture.md` and `rules/security.md` item 10, section 4 item 14). If the owner reads it
otherwise, a spec change is needed first and the unit comes out. The owner is told at hand-off that
the installer's privileged step and the agent's root path (the PHP install unit) carry this
outstanding review.

### 6.1 Verdicts

| Lens | Verdict | Blocking or major findings |
|---|---|---|
| correctness | approve | none |
| security | approve | none |
| compatibility | approve | none |
| rules | changes required | three major (6.3, A to C) |
| design | changes required | one major (6.3, A) |

### 6.2 Security-lens checklist (rules/security.md, run verbatim)

| Item | Result | Note |
|---|---|---|
| 1 Input validated and re-validated | yes | The version passes `PhpVersion` and `ensure_supported`; the instance is one of six closed-set package names. |
| 2 Paths | n/a | No customer path is written. Only a fixed pool-path existence check; the unit file is written by the installer as root, 0644. |
| 3 Shell | yes | Argv only. `ExecStart=` is a fixed argv with an absolute program, and `%i` is substituted after splitting. Residual: no `--` before `%i` (6.4, E). |
| 4 Config-file injection | yes | No caller value is written into any file. |
| 5 SQL | n/a | |
| 6 AuthZ | n/a | No endpoint or tenant query touched. |
| 7 Configuration documented | yes | No new variable; no `EnvironmentFile`; nothing from `agent.env` reaches the unit. |
| 8 Secrets and customer-facing errors | yes, with an open check | The failure text carries the unit name and `systemctl` stderr and reaches the wire as `tool_output`, as apt stderr did before. That the API strips it for non-admin roles was not verified (6.4, G). |
| 9 Crypto | n/a | |
| 10 Surface | yes, pending the owner | No port, no resident process, no outbound call of the product's own. The no-daemon reading is the author's, not the owner's (see above). |
| 11 Dependencies | n/a | None added. |
| 12 Agent | yes | The rpc is unchanged and typed; the `is_installed` short-circuit keeps it idempotent. The closed set is enforced only in the agent (6.4, F). |
| 13 Module reach into the agent | n/a | No new service or client. |
| Sensitive change escalation | yes, as a debt | Note written first; review stated OUTSTANDING; the code matches section 2.7; the branch must not merge. |

### 6.3 Blocking and major findings, and the decision on each

**A. `DistroAdapter::package_manager()` has a false doc and no production caller** (rules and design
lenses, major; also raised as minor by correctness, security and compatibility). The agent no longer
spawns the package manager, so the doc "for the process-execution allow-list" is untrue, and the
installer now hard-codes the same two paths. *Decision: fix in this branch, as section 2.8 already
required.* The method is kept and its doc rewritten to say what it now is: the program that the
installer renders into `maran-php-install@.service`, not spawned by the agent. Retiring it is
rejected for now because it removes a tested distro fact and widens the diff into the `distro`
crate. The two literals can still drift, so Phase B adds a polygon assertion that the installed
unit's program equals the adapter's path on both families. The owner may still choose to retire the
method instead.

**B. The `PATH_VALUE` doc in `agent-core/src/utils/apply_child_environment.rs` names a spawn that no
longer happens** (rules lens, major). *Decision: fix in this branch.* The package-manager rationale
is removed and the doc names the children the agent still spawns.

**C. The sandbox paragraph at `maran-agent.service:155-158` and README line 160 overstate the
protection** (rules lens, major). pkg-unit corrected only the lines at 254-256. *Decision: fix in
this branch, doc-only.* The paragraph is reworded to this note's section 2.4 wording, and the README
states that the install unit is deliberately not sandboxed. The agent unit's directives do not
change (section 4 item 1).

No lens raised a blocking finding. Correctness, security and compatibility each closed with
approve; correctness alone said the issue could close, and that is **not** accepted until Phase B
measures it and the human review is done.

### 6.4 Minor findings, and the decision on each

| # | Finding | Decision |
|---|---|---|
| D | Stray `; ;` in the failure text when `show` cannot run (correctness, rules, design). | Resolved by dropping the `show` call: the failure text is systemctl's stderr plus the unit name and a `journalctl -u` hint, and the exit status is the failed start itself. |
| E | `%i` passed bare; an instance starting with `-` would read as an option (security). Only root or a polkit-authorised user can start an instance, and the agent sends only the six names. | Defer to Phase B: measure `--` on apt and on dnf first, then add it only where accepted. Recorded here as defence in depth, not a closed hole. |
| F | The closed set lives only in the agent; a compromised agent could start an instance naming any package, and section 2.4's "it can already reach root by writing its own unit" is by reading directives, unmeasured (security). | Keep section 2.4 as written but treat it as **unproven**. Section 4 item 8 is a merge precondition. If the measurement shows the agent cannot write and start a unit of its own, 2.4 is wrong and the instance must be restricted in the unit (six fixed units, or a condition or allow-list check) before merge. |
| G | The API's role-aware stripping of `tool_output` was not verified (security). | Verify in the test pass; add a test if none exists. |
| H | The unit name is spelled in Rust constants, `MARAN_PHP_INSTALL_UNIT_NAME` and `uninstall.sh`, with nothing tying them together (security, design). | Add a structure or polygon assertion that the names agree (section 4 item 15). |
| I | Existing tests and `fake_php_host.rs` still assert the old apt-get argv (correctness, rules, design). | Test pass, as `rules/testing.md` allows. Required cases: the start argv for both families; a non-zero start; a spawn error; a zero start with the pool directory still missing; a `show` that cannot run; the unsupported version, which must not start the unit. The fake answers "not installed" before the start and "installed" after. |
| J | The polygon uninstaller census says two units (already stale, three with ftps) and `check-structure.sh` check 24's comment says "for both units" (correctness, rules, compatibility). | Update both in the test pass. Add a polygon assertion that the unit exists after install and is absent after uninstall, on both families. |
| K | No CHANGELOG entry: hosts that update the binary without re-running the installer fail PHP installs, with no in-process fallback (correctness, rules, compatibility). | Add an Unreleased entry before merge. Not a regression, since the old in-process install failed with `EROFS` on every host. |
| L | No apt lock wait and no stale-index handling on the Debian family; a held dpkg lock fails the unit at once (compatibility). | Measure on Ubuntu 22.04 and 24.04 and Debian 12 and 13. Add `-o DPkg::Lock::Timeout=N` only with that measurement; otherwise record the limitation. |
| M | The agent's blocking thread waits up to 1800 s because `spawn_argv` has no deadline (security). | Measure in Phase B whether several waiting installs starve other rpcs; bound the wait or move it off the shared pool if they do. |
| N | Unmeasured: `--no-remove` on Debian, `dnf` on RHEL, the 1800 s timeout, `systemd-analyze verify` on a rendered unit, the exit status of `systemctl start` after a failed oneshot, and the "Unit not found" text (all lenses). | Phase B on both families, including a real install through the agent as root under `ProtectSystem=true`, plus the inverse control of section 4 item 6. Record results in this note. |
| O | The RHEL family has no Remi provisioning, so a real RHEL install finds no package (correctness, and section 1). | A separate pre-existing defect, tracked on its own and not reported as fixed here. |
| P | Compatibility checklist: "Polygon measurement done" and "Human second reviewer" are both **no**. | Both stay open. They are the merge gates above. |

### 6.5 Review coverage gaps

The reviewers' own rule reading was thin. Correctness, security and design did not read
`rules/git.md`, `rules/testing.md` or `rules/architecture.md` in full, and compatibility did not
read this note. The human reviewer must not treat these approvals as full rule coverage.

## 7. Verification evidence *(added at the verification stage, Phase B)*

Phase B ran builds, tests, measurements and mutations. This section records what was observed and,
as important, what was not. Agents produced it, so it **does not discharge the second-reviewer
requirement**.

**The second-reviewer requirement remains OUTSTANDING.** This branch MUST NOT merge to `main` until a
human second reviewer has read this note and the diff and signed off, and the owner has confirmed the
reading of section 2.6 (a never-enabled oneshot unit is not a "daemon" under `rules/architecture.md`
and `rules/security.md` item 10). Green gates, measurements and mutation kills below do not settle
either point. The post-implementation lenses tests-vs-plan, rules-final, security-final and
doc-truth-final returned "changes required" before this stage; their findings must be read by the
human reviewer alongside this section (they are not re-litigated here).

### 7.1 Gates (against base 7e75b67; dev has moved on)

| Gate | Verdict | Evidence |
|---|---|---|
| `maran check` | OK | All 8 tools present. |
| `maran structure` | STRUCTURE-OK | Includes new check 27 (unit name agrees across the agent constant, the template file, `MARAN_PHP_INSTALL_UNIT_NAME` and the `uninstall.sh` path). |
| `cargo fmt --check`, `clippy --all-targets --all-features -D warnings`, `cargo doc` with `-D warnings` | OK | No warnings. |
| `maran test rust` | OK | 28 targets, 2008 passed, 0 failed, 117 ignored; after the mutation-stage test additions 2009 passed. Rows: `maran_agent` lib 324, new `php_install_rpc` 6, `maran_agent_core` 356, `maran_distro` 42, `maran_ops` 1252 (+1 ignored). The baseline in `scripts/test-baseline.txt` already carried these totals; nothing was accepted by this stage. |
| `maran proto` | PROTO-OK | No proto diff. |
| `maran handshake` | **NOT VERIFIED** | Run twice, never reached a verdict: the API could not start because 127.0.0.1:5432 is held by another project's container, so the script did not start the `maran-postgres` dev container (SqlState 28P01). Machine cause, not a branch regression. The Rust handshake tests inside `maran test rust` passed (6/0/0). |
| backend, api, migrate, licenses, frontend lanes | not run | No backend, proto, frontend or dependency diff against the base. |
| `maran polygon verify` (full suite images) | **NOT RUN** | Installer files changed, so the suite images must be rebuilt first. Only the five installer assertions were run in a throwaway container (7.3). |

### 7.2 Measurements on real systemd containers

Throwaway containers on the host kernel, the real `InstallPhpVersion` rpc driven over the agent
socket by a scratch client (never committed). The built agent equals the worktree's production code.

- **Before (7e75b67), Ubuntu 24.04:** the install fails as the issue says (`Read-only file system`
  writing under `/usr`). It also leaves dpkg broken: `dpkg --audit` listed 15 packages in state iHR
  with missing control files. This is stronger than the issue's claim and means the old behaviour also
  blocked unattended security updates. `/etc/php` did not exist, so the feared false "installed" did
  not happen; a second request failed again.
- **After, Ubuntu 24.04:** progress 10, 30, 80, 100 then Ok(8.3). The package manager ran as PID 1's
  child in cgroup `maran-php-install@php8.3-fpm.service`, not the agent's. Its command line was
  `apt-get install -y --no-remove -- php8.3-fpm` (so a `--` is present in the measured unit, which
  closes finding E for apt), environment only `PATH` and `DEBIAN_FRONTEND=noninteractive`, umask
  0022, no `MARAN_*` or `agent.env` variables.
- **After, Rocky Linux 9.8 (Remi and EPEL hand-added to the image):** Ok(8.3);
  `php83-php-fpm-8.3.36-1.el9.remi` installed, pool directory present, service active. Before, dnf
  failed with a misleading "needs 880KB more space" caused by the read-only `/usr`. The unit's
  `ExecStart` was `dnf install -y -- %i`. Only Rocky 9 was run; alma9 and the 10-series were not.
- **Agent sandbox not widened (E11, 3.x):** `systemd-analyze security maran-agent.service` is 4.4
  OK before and after; `ProtectSystem=yes`; `ReadWritePaths=` has no `/usr` before and after. A probe
  inside a `ProtectSystem=true` sandbox cannot write `/usr/sbin`, while a unit started by PID 1 can.
  The install unit itself scores 9.6 UNSAFE; informational, a package manager cannot be sandboxed.
- **No in-process fallback (E4):** with the template unit deleted the request fails typed, naming the
  unit and the "installer must be re-run" hint, and no `apt-get` process appears. Plain `systemctl
  start` exits 5 for a missing unit and 1 for a failed run (`Result=exit-code`, `ExecMainStatus=100`).
- **Start limit (E8):** eight consecutive failing starts all exit 1, never `start-limit-hit`
  (Ubuntu 24.04 only).
- **`--no-remove` (E9):** with postfix installed, starting the unit for `exim4-daemon-light` fails
  and postfix stays. The inverse control `apt-get install -s` shows `Remv postfix`, so the flag is what
  protects it.
- **Concurrency (E10):** a second concurrent start fails fast on the dpkg lock, the first succeeds,
  no hang.
- **Regex fallback (E12):** `apt-get install -s -y 'ase-file.'` selects `base-files`; the control
  `zzq.-nope` fails. Confirms the section 2 concern on Ubuntu 24.04 only.
- **Agent restart mid-install (E5):** the unit ran to completion (dpkg clean, pool present) and a
  retry returned 100/Ok, so run-to-completion holds. **New finding:** the interrupted client stream
  ended with a typed error whose stderr was empty ("the PHP install unit failed:  (unit ..."),
  because `systemctl start` was killed with the agent. Misleading, minor; not fixed here. The first
  run was a harness artifact (probe connected during the restart).

### 7.3 Mutation proof

- **Rust:** 22 mutations on a copy of the tree, each scored by `maran mutate`; all ended KILLED by a
  named test and every file was restored byte-identical. Two needed test work first: M14a survived
  (no test made the enable command fail to spawn) and M15 was not a kill (the test only checked the
  pool path). Both were fixed with a new test and a `created_directories()` assertion, then killed.
  Plus the in-service mutation "stop on closed stream", killed by the stream-drop test.
- **Installer and polygon assertions:** 17 mutations (`%i` to `%I` per arm, dropped `--no-remove`,
  added `--allowerasing`, shell wrapper, `[Install]`, timeout removed, limit key moved, `RemainAfterExit`,
  PATH, UMask, `DEBIAN_FRONTEND`, mode 0640, `.wants` link, deleted uninstall line, `/usr` in the agent's
  `ReadWritePaths=`, `ProtectSystem=false`) all refused by the named assertion; the unmutated subset
  passed first. This was a five-function subset on the Debian image, **not** the full image-build
  gate. `systemd-analyze verify` accepted the installed unit.
- **Structure check 27:** four mutations, each gave violations; control was STRUCTURE-OK.

### 7.4 What remains unverified

1. **The human second review and the owner's reading of section 2.6.** Outstanding; merge blocked.
2. `maran handshake` and full `maran polygon verify` (including rebuilt suite images and the build-time
   assertions as a whole, and the Dockerfile `COPY` lines, which were modelled, not built).
3. The full installer path (`install.sh`, release bundle) and `uninstall.sh` removing the unit (E13):
   the containers were prepared by running `install_units`, not installed end to end.
4. The full API chain: setup, login, POST `/api/v1/sites/php-versions/install`, the audit row, and the
   issue's closing condition (create an account and a site, switch it to the new version). The probe
   called the agent rpc directly. The API's stripping of `tool_output` for non-admin roles (6.4 G) was
   not verified.
5. Coverage: Debian 12 and 13 and Ubuntu 22.04 (start limit on the oldest systemd, apt regex per
   release, dpkg lock wait, finding L); alma9 and RHEL 10; aarch64; SELinux enforcement; the dpkg
   conffile prompt case (no `--force-conf*` options are passed); PHP versions other than 8.3.
6. Section 2.4's claim that a compromised agent can already reach root by writing its own unit remains
   an argument from directives, unmeasured.
7. How the panel behaves while an install outlasts the administrator's HTTP request, and whether
   several waiting installs starve the agent's blocking pool (6.4 M).
8. The RHEL Remi/Sury provisioning gap: the RHEL result proves the unit, not the product, because the
   repositories were added by hand. A separate defect, not fixed here.
9. Findings carried forward: the empty-stderr message on an agent restart (7.2); the open doc-only
   items of 6.3 A to C and the CHANGELOG entry (6.4 K) were assigned to fixers and this stage did not
   re-check them.
