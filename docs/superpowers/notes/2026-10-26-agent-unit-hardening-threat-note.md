# Threat note: the root agent's sandbox gains four directives and refuses a fifth (issue #91)

**Surface:** `installer/systemd/maran-agent.service`. The installer installs this unit in step 70
(`installer/lib/70-services.sh`, `render_agent_unit`), and it is the only sandbox around the one
process in this product that runs as root. rules/security.md "Sensitive change escalation" names
the installer's privileged steps, so this change needs a second reviewer and a note written before
the change. This is that note. It was first written by an earlier run before any line of the unit
was edited, and has been checked again since (see "Corrections made after the unit was edited").

**Verification phase (2026-10-28):** the last section of this note, "Verification evidence", records
the gates, the real-systemd measurements and the mutation proof, and corrects two claims made
earlier in this note (the reason `ProtectProc=invisible` is refused, and the `userdel` live-process
claim). Where an earlier section disagrees with that section, that section wins. The six
`UNCONFIRMED (#91)` markers are still in the unit; the measurements below are not yet written into it.

**Which version of the unit the line numbers describe.** `dev` has moved since the first draft:
commit `92fc9d6` (#93) landed the `maran-php-install@.service` unit and rewrote the package-install
sentence in the agent unit. `git show dev:installer/systemd/maran-agent.service | md5sum` now prints
`805b6a6c6ee2f653a73ff58ceb4f6439` (the first draft pinned `aac6a50bac6d9b1d9f37e897a559137b`, the
pre-#93 file). **Every line number in this note now refers to that current `dev` version.** The working
file in this worktree carries the code phase's edits (about 120 inserted lines after line 348 of
`dev`), so its numbers above that point are larger; the six directive lines of this change sit at 379
to 479 there. Re-derive any number with the command beside it.

**Second reviewer: OUTSTANDING.** The work is being done by an agent session, which cannot get a
reviewer and cannot be one. Per rules/security.md this change may land on the branch
`fix/91-installer-agent-unit-hardening`. It **MUST NOT merge to `main`** until a human second
reviewer has read this note and the measurements it asks for and has signed off. The owner is to be
told at hand-off that the agent unit has a review outstanding.

**Not in this change, and no longer in anyone else's way:** the package-install sentence in the
"Paths deliberately NOT listed" paragraph (line 260 on `dev`). #93 has already corrected it on `dev`
("the agent does not run the package manager: it asks PID 1 to start
maran-php-install@<package>.service"). This branch leaves it alone and must not reintroduce the old
wording when it merges `dev`.

**Observed and also not in this change:** `ReadWritePaths=` (line 316) lists
`/var/lib/maran-ftps`, but the paragraph above it, which says "Writable set, one entry per reason",
has no entry for that path. That is a gap in the unit's own documentation, and it is not part of
#91. It is recorded here so the owner can decide whether this branch or a separate issue
closes it.

**Also observed, also not in this change:** there are two files named `file_mode.rs` in
`agent-core`, and only one is compiled. `agent-core/src/validation/fs/mod.rs:4` declares
`pub mod file_mode;`, and `agent-core/src/validation/mod.rs` declares no `file_mode` at all, so
`agent-core/src/validation/file_mode.rs` is a tracked file that no build reads (`git ls-files`
lists both, and `cmp` says they differ from line 13). Item 4 below credits the setid refusal in
`files.*` to the compiled one, `validation/fs/file_mode.rs`. A reviewer who opens the other one is
reading code that does not run.

## What the unit holds today

The numbers below were measured on this tree on 2026-10-26, not copied from the issue, by an earlier
run of this same stage. The current run's code phase forbids running `systemd-analyze` or any other
check until all code is written, so the figure was **not** taken again in this run. The verification
phase records it again, before and after. Before it can be analysed, the unit has to be rendered the
way the installer renders it:

```
sed -e 's/__MARAN_GROUP__/maran/' -e '/__MARAN_QUOTA_DEVICE_BINDS__/d' \
  installer/systemd/maran-agent.service > /tmp/maran-agent.service
systemd-analyze security --offline=yes /tmp/maran-agent.service
```

On systemd 255 (255.4-1ubuntu8.18) this prints **overall exposure 4.4**, the same figure as the
issue. The figure was measured again when this note was checked, from a copy rendered into a
session scratch directory, and it had not changed. Five of the findings are the subject of this
change: `ProtectControlGroups=`,
`ProtectKernelModules=`, `SystemCallArchitectures=`, `RestrictSUIDSGID=` and `ProtectProc=`, each
weighted 0.2. `maran-api.service`, which runs unprivileged, already sets three of them at lines
122, 124 and 127.

The issue's line numbers for the agent unit are 27 lines behind `dev`, because the #81
paragraph about `User=root` and the #93 rewrite were added above them. On `dev`:

- **Line 365** sets `MemoryDenyWriteExecute=true`. Lines 354-364 explain why, with a measurement.
- **Lines 385-388** say "ProtectKernelModules/MemoryDenyWriteExecute are intentionally NOT set".
  This is false about MDWE. It also gives a reason for leaving `ProtectKernelModules` off ("would
  block the systemctl calls") that comes from reasoning, not measurement, and the comment admits
  as much. It also cites "per rules/rust.md" for the agent never running interpreted or JIT code,
  and rules/rust.md says no such thing:
  `/usr/bin/grep -n -i -E "jit|interpret" rules/rust.md` prints nothing (exit 1). Positive control:
  `/usr/bin/grep -c -E "unsafe" rules/rust.md` prints 11. The rewrite must not carry that citation
  over. The reason MDWE costs the agent nothing is the transient-unit measurement in the comment above the directive (lines 361-362 on `dev`), not a rule.
- **Lines 428-430** (item 6 of "Not yet confirmed") still say MDWE is "left off above". They list
  `ProtectControlGroups` as untested on the guess that "per-account systemd slices may need it".
- **Line 384** keeps `CAP_SYS_ADMIN` in the bounding set.
- **No gate observes any of these directives.** `scripts/lib/check-structure.sh:1009` checks
  `MemoryDenyWriteExecute` only to refuse it in `maran-api.service`. The polygon assert
  (`docker/polygon/asserts/parts/units-and-preflight.sh:29`) reads only the agent unit's
  `ReadWritePaths=` line and the order of `EnvironmentFile=` and `Environment=PATH=`. Command:
  `/usr/bin/grep -rn -E "MemoryDenyWriteExecute|ProtectControlGroups|RestrictSUIDSGID|SystemCallArchitectures|ProtectKernelModules|ProtectProc" docker scripts agent backend`
  finds only the api-unit check and three prose mentions of #40. Positive control: the same search
  for `ReadWritePaths` finds `units-and-preflight.sh` and `check-structure.sh`. So an edit that
  later deletes one of the lines this change adds turns nothing red. Whether a structure check
  should hold them is for the owner. That check would live in `scripts/lib/check-structure.sh`,
  which this branch's part does not own.

## What an attacker can do with this surface today

The attacker here has already got past the agent's typed command set: a parsing or logic defect
in one rpc, or a program the agent spawns that misbehaves. The sandbox decides what that foothold
is worth. Two things are true first, and this note does not hide them behind the directives.

- **This sandbox does not contain a fully compromised agent, and nothing in this change makes it
  do so.** `/etc` is writable (`ProtectSystem=true`, chosen at lines 140-162 so that shadow-utils
  can work). The agent also talks to PID 1 through `systemctl`. A process with arbitrary code
  execution inside the agent can therefore write a unit under `/etc/systemd/system` and have PID 1
  start it with no sandbox at all. The unit already says this at lines 158-161: protection for
  `/etc` is enforced upstream, by the closed command set. The new directives do not change it.
- What the directives do buy is narrower and real. They shrink the kernel interface a foothold can
  reach. They make the kernel refuse two specific mistakes on every path and in every child, not
  only where validation code remembered to refuse them. And they close routes that need no file
  under `/etc` to be written.

With that limit stated, here is the surface as it stands, directive by directive.

1. **Control groups are writable by a process holding `CAP_SYS_ADMIN`.** Today the agent can
   rewrite any service's limits (`memory.max` on PostgreSQL, `cgroup.freeze` on sshd or nginx),
   which is a denial of service against the whole host. It can also move a process it forked out of
   `maran-agent.service`'s cgroup. A process moved that way survives `systemctl stop` and the
   `KillMode=control-group` (comment at line 130 of the unit) that kills the setuid children with the daemon.
   That is persistence across a restart or an upgrade of the agent. On a host booted with cgroup v1
   or the hybrid hierarchy, the root cgroup's `release_agent` is writable as well, and the kernel
   runs that program as full root **outside every systemd restriction**. That is a sandbox escape,
   and one that needs no file under `/etc`. This last point comes from the kernel's documented
   behaviour and was **not measured**. Every supported release defaults to cgroup v2, where
   `release_agent` does not exist, but an operator can boot v1. The agent's own code does not
   write cgroups:
   `/usr/bin/grep -rn -E "cgroup" --include=*.rs agent/crates | /usr/bin/grep -v "/tests/"` prints
   a single line, a comment at `agent-core/src/privs/fork_as_account.rs:572`. That line is also the
   search's positive control. The issue cites it at :553.
2. **Kernel module interfaces.** `CAP_SYS_MODULE` is already missing from the bounding set, so an
   explicit `init_module`/`finit_module` already fails. What is still open is reading
   `/usr/lib/modules`, plus the module syscalls reaching the kernel at all if the bounding set is
   ever widened. This directive is cheap and its value is small. The note says so plainly so
   nobody oversells it.
3. **Every syscall ABI the kernel offers.** On an x86_64 kernel built with IA32 emulation, the agent
   can make i386-ABI system calls. systemd.exec(5) recommends `SystemCallArchitectures=native`
   because filtering is not equally effective on every ABI, and in particular the address-family
   restriction is weaker where sockets are multiplexed through `socketcall`. The extent of the
   bypass on each family's kernel was **not measured**. On `aarch64` the corresponding non-native
   ABI is 32-bit ARM, on kernels built with `COMPAT`. No program the agent spawns is a non-native
   binary.
4. **SUID/SGID files can be created.** `NoNewPrivileges=true` stops setuid binaries from working
   *inside* this sandbox. It does nothing for a customer's processes, which run outside it. So a
   setuid-root file that root writes anywhere writable outside `/usr` (`/home`, `/var`, `/etc`)
   gives local root to whichever customer runs it, and it outlives the agent. Two code paths
   already refuse this in Rust. `files.*` refuses a mode with the bits
   (`agent-core/src/validation/fs/file_mode.rs`). A restore extracts with `--no-same-permissions`
   (`ops/src/backup/model/extract_spec.rs:70-78`). Neither covers every other path or every child:
   `tar`, `useradd` copying the skeleton, `chmod`/`chgrp`, and the package manager for as long as
   it still runs as the agent's child (#93). The agent itself never asks for these bits. This
   search finds no special mode anywhere a mode is written:
   `/usr/bin/grep -rn -E "g\+s|u\+s|0o[0-7]?[2467][0-7]{3}" --include=*.rs agent/crates | /usr/bin/grep -v "/tests/"`.
   Every hit is a doc comment or a mask (`0o7777`, `0o7000`). Positive control: the same pattern
   without the `/tests/` filter matches `0o4755` in
   `agent/crates/agent/src/tests/services/files/write_collector_tests.rs:150`.
5. **The full process tree is visible.** The agent can read every process's `/proc` entries except
   those that need ptrace access (`environ`, `mem`). In practice that means other users' command
   lines, which sometimes carry a password typed on a command line. This is the exposure
   `ProtectProc=invisible` would close, and the issue expects it to be **rejected**. The reason is
   below.
6. **The MDWE comment contradicts the unit.** A reader who trusts lines 385-388 or item 6 removes
   a directive that was measured and works. That is a trap for whoever edits this unit next. A
   second gap sits under it: the measurement just above the MDWE line shows only that the daemon *starts*
   under MDWE. MDWE is inherited by every child (`nginx -t`, `php-fpm -t`, `nft`, `tar`, the dump
   tools, the shadow suite), and no rpc has been observed running under it.

## The change, and why it makes the surface safer

All behavioural edits are in `installer/systemd/maran-agent.service`. One comment-only edit is in
`installer/systemd/maran-api.service`: its note on `MemoryDenyWriteExecute` used to say the agent
unit does not set it, which was false (the agent unit sets it and documents why); it now says the
agent unit does set it. The header of the agent unit's "not yet confirmed" list was also reworded
so it no longer claims nothing below was observed while item 5 is marked confirmed. The unit's own
`RestrictSUIDSGID` comment assumes the supported releases (systemd 249 and newer); the EL8 question
below concerns the wider set the installer's preflight still accepts. The second-reviewer
requirement remains OUTSTANDING.

**The order of work, and the debt it creates.** An earlier draft of this note said each directive
would be measured *before* its line was written. The owner has since set a different order for this
work: all code is written first, with no build, test or check, and a separate verification phase
then measures. So the directive lines land in the working tree **before** anything has observed
them. The unit's own MDWE paragraph names the risk of that: "A hardening directive added on the
strength of 'it should be fine' is how the panel's own unit acquired one that stopped it starting."
This note does not argue that risk away. It turns it into a debt that can be seen and counted:

- Every line this change adds or re-justifies carries the marker `UNCONFIRMED (#91)` in the
  comment directly above it, with the measurement that line still owes. The exact form is in
  "What the parts depend on from each other" below.
- The verification phase measures **one directive at a time**, changing one property per run.
  When a directive's measurement holds, the marker is replaced by the result (family, rpcs driven,
  outcome), in the house style of the MDWE and `PrivateDevices=` comments. When it fails, the
  directive is **removed** and the comment records the rejection and the observation that caused it.
- The branch is not finished while any marker remains. A marker still present at hand-off is named
  to the owner as an unmeasured directive. The unit as written in the code phase is a set of claims,
  not a sandbox anyone has watched work.

"Measured" means a throwaway install polygon with real systemd as PID 1, built from the install
images in
`docker/polygon/images/` (`install-debian.Dockerfile` and `install-rhel.Dockerfile`, pinned by each
version's `base.txt`). The minimum is Ubuntu 24.04 and AlmaLinux 9, one per family. Each run
requires three things. The agent starts. The installer's own check passes (`wait_for_agent_socket`
in step 70, then the polygon verdict: both units active, the socket `660 root:maran`, `/health`
showing `"agent":"connected"`). And representative rpcs are driven through the panel's HTTP API:
account create and delete, quota (`setquota`, and the enforceability read that runs `quotaon -p`),
site create (with `nginx -t` and the reload), cron create, backup create and restore, a firewall
allow and a ban, an SFTP login create (a mount unit started through `systemctl`), and a suspension
(the `pkill` cull). Quota is on the list for a reason specific to this sandbox: the last time a
directive here broke it (#73), the failure did not look like a denial at all. `setquota` reported a
quota file and a mount point, and neither was the problem. A quota run is therefore read by the quota
it applied, not by the exit status alone.

Three constraints on how those runs are made. They were found while this note was being checked,
and the measuring stage must honour them:

- **`maran installer` cannot be used as it stands.** It always names its container
  `maran-install-polygon-<os>-<arch>` (`scripts/lib/install-polygon.sh:129`). It runs
  `docker rm -f` on that name before it starts (line 138). It also re-tags the shared
  `maran-install-polygon:<os>-<arch>` image from whatever tree it was run in. When this note was
  checked, a container with exactly that name for `ubuntu24` was running for another session. So
  running the command would destroy someone else's polygon. The runs for this branch use their own
  containers named `maran-wf-91-installer-agent-unit-hardening-<stage>-<os>`, started from the
  existing image with the same `docker run` flags (`--privileged --cgroupns=host`, the host cgroup
  tree mounted rw). They then do the same install steps by hand and the same verdict checks. The
  record must say that the harness was reproduced, not run.
- **Which agent binary was measured must be written down.** `--mode local` installs *this tree's*
  installer over the *released* artifacts (`install.sh --channel beta`). The unit under test is
  therefore this branch's, but the agent binary is the published beta. The record names that
  binary's version. If its spawn behaviour may differ from `dev`, the run uses an offline bundle
  built from this tree instead.
- **A cgroup positive control must never touch the host.** The polygon shares the workstation's
  cgroup tree (`--cgroupns=host`, `/sys/fs/cgroup` bind-mounted rw). Any control showing that the
  agent *could* write a cgroup before `ProtectControlGroups=` must write only into a leaf under the
  container's own subtree. It must never write into the root, and must never write `cgroup.freeze`
  or `memory.max` on anything outside that subtree. `release_agent` is not probed at all.

What the code phase writes, directive by directive, and what each one still owes:

- **`SystemCallArchitectures=native`: added, marked.** Takes the non-native ABIs out of the
  filter's reach. No binary the agent spawns is non-native. Owes: the agent starts and the
  representative rpcs pass with it on.
- **`ProtectControlGroups=true`: added, marked.** `/sys/fs/cgroup` becomes read-only inside the
  agent's mount namespace. That closes item 1 above, including the v1 `release_agent` escape.
  Nothing in the agent writes cgroups, and `systemctl` asks PID 1, which acts outside this
  namespace. Owes: the `ro` mount seen in the agent's namespace, and the SFTP mount unit and every
  reload still working.
- **`ProtectKernelModules=true`: added, marked.** Adds a seccomp denial of the module syscalls and
  hides `/usr/lib/modules`. Kernel *auto*-loading (for example the nf_tables expression modules an
  `nft` rpc pulls in) runs `modprobe` from the kernel in the init namespace, not in this sandbox,
  so it should keep working. No agent code spawns `modprobe`, reads a modules tree or runs
  `systemd-run`:
  `/usr/bin/grep -rn -E "modprobe|/lib/modules|systemd-run|lsmod" --include=*.rs agent/crates | /usr/bin/grep -v "/tests/"`
  prints nothing. Positive control: adding `|quotaon` to the same alternation finds nine files,
  among them `ops/src/accounts/quota_enforceability.rs`. Owes: the firewall rpcs and the quota pair passing
  with it on.
- **`RestrictSUIDSGID=true`: added, marked, and removed again if the restore measurement fails.**
  The kernel refuses S_ISUID/S_ISGID in the mode argument of `chmod`/`mkdir`/`open(O_CREAT)`/`mknod`
  for the agent and every child. Item 4 is then enforced on every path and no longer depends on each
  validator. That holds only where the filter also covers `fchmodat2`, which reviewer check 4 below
  asks to be measured. Owes: account create, restore of a home holding setid entries, and the
  `fchmodat`/`fchmodat2` controls.
- **`ProtectProc=invisible`: NOT added.** The code phase was told to add it only if the analysis
  showed that `userdel` and the kill of an account's processes keep working under it. The analysis
  shows the opposite (below). The unit carries a comment recording the rejection and why, marked
  as owing the observation in reviewer check 5. That observation is made with the directive applied
  to a run, never by shipping it.
- **`MemoryDenyWriteExecute=true`: kept, its contradiction removed, marked.** Lines 385-388 and
  item 6 of "Not yet confirmed" are rewritten to say what the unit actually sets, and the false
  rules/rust.md citation goes with them. The existing line is kept. Its marker owes the measurement
  the start-only check never made: the representative rpcs, whose children (`nginx -t`,
  `php-fpm -t`, `nft`, `tar`, the dump tools, the shadow suite) all run under MDWE. See reviewer
  check 9.

Item 6 of "Not yet confirmed" is rewritten to list the markers still open, and shrinks as the
verification phase removes them. `systemd-analyze security` is recorded before and after, both
`--offline=yes` on the rendered file and online inside the polygon against the unit PID 1 actually
loaded.

**Why `ProtectProc=invisible` is refused, and why refusing it is the safe side.**
The agent has no `CAP_SYS_PTRACE` (the bounding set, line 384), so with `hidepid=invisible` it stops seeing any
process of another uid. It also loses root processes whose capabilities exceed its own. Two
security functions depend on seeing those processes, and both would fail **silently, in the
insecure direction**:

- **Suspension.** `ops::logins::end_account_sessions` runs `pkill --uid <n>`.
  `pkill` reads `/proc`, so it would match nothing and exit 1. That module treats exit 1 as "no
  session open" and as success (`NOTHING_MATCHED`, `end_account_sessions.rs:45-50`). The panel
  would record the suspension as complete while the customer's SFTP session kept writing.
- **Deletion.** `AccountOperations` runs `userdel --remove`
  (`ops/src/accounts/account_operations.rs:682`). Shadow-utils' "user is currently used by
  process" refusal scans `/proc`, so it would let the deletion through with the account's
  processes still alive. Those processes would hold a uid that the next `useradd` hands to a
  different tenant. That refusal is the only guard on this path, because deletion does not cull
  the account's processes first. The only call to the cull is in the suspension path:
  `/usr/bin/grep -rn -E "end_account_sessions\(" --include=*.rs agent/crates | /usr/bin/grep -v "/tests/"`
  prints two lines. One is the definition (`ops/src/logins/end_account_sessions.rs:149`), and it
  is the search's positive control. The other is the only call,
  `ops/src/logins/set_account_logins_locked.rs:233`.

Each of those is worse than the information exposure the directive closes. The rejection must be
shown from **outside** the sandbox (`docker exec … ps -o uid,pid,cmd`), not read from an rpc's
answer, because the rpc's answer is exactly what goes blind. The method: start a process at the
account's uid from outside, then suspend and delete the account, with and without the directive.
`ProcSubset=pid` (0.1 in the analysis) is not proposed either. `monitor.GetHostMetrics` reads
`/proc/stat`, `/proc/meminfo`, `/proc/loadavg`, `/proc/net/dev` and `/proc/net/route`
(`ops/src/monitor/process_monitor_host.rs:33-48`), and that setting removes them. It also reads
`/proc/mounts` (`process_monitor_host.rs:199`), and so does account creation's home inspection
(`ops/src/accounts/process_system_host.rs:99`). `/proc/mounts` is a top-level symlink to
`self/mounts`, and `subset=pid` hides top-level entries other than the pid directories, `self` and
`thread-self`. That comes from the kernel's procfs code and was **not measured**. The `/proc/stat`
group is reason enough to refuse it either way.

**Interaction with #93 (resolved on `dev`).** The first draft of this note warned that a package
manager spawned as the agent's child would inherit `RestrictSUIDSGID` and refuse any package that
ships a setuid or setgid file. #93 (commit `92fc9d6`) has since moved the install out of the agent:
`InstallPhpVersion` asks PID 1 to start `maran-php-install@<package>.service`, a oneshot whose process
runs outside this sandbox
(`agent/crates/ops/src/php/install_php_version.rs`, `INSTALL_UNIT_PREFIX`). So none of the
directives here reach `apt-get`/`dnf` or the maintainer scripts, and the interaction is gone. What
remains for a reviewer is the converse: this change must not be read as hardening that install unit,
which has its own sandbox in `installer/systemd/maran-php-install@.service`, and no new rpc may
re-introduce a package-manager spawn as a direct child of the agent.

## What the parts depend on from each other

This branch has one part, `agent-unit`, and it owns one file. No code interface passes between
parts of this branch. The file is still read by other code and by another branch, and those readers
are its public interface. The part keeps every one of them exactly as it is, and the verification
phase relies on the marker form below.

**Read by the installer** (`installer/lib/70-services.sh:124-136`, `render_agent_unit`):

- `__MARAN_GROUP__` is replaced wherever it appears (`gsub`). It stays in `Group=__MARAN_GROUP__`
  and appears nowhere new.
- `__MARAN_QUOTA_DEVICE_BINDS__` is replaced only when it is **alone on its line**
  (`/^__MARAN_QUOTA_DEVICE_BINDS__$/`). It stays at column 0 with nothing else on the line. A
  directive may be added after it, never on it.
- `refuse_unsubstituted_placeholder` then fails the install if any `__MARAN_…__` token survives.
  So the part adds no new placeholder.

**Read by the checks:**

- `scripts/lib/check-structure.sh` check 20 (line 893 onward) and
  `docker/polygon/asserts/parts/units-and-preflight.sh:29` both take the **first** line that begins
  `ReadWritePaths=`. That line (316) is not edited, and no second `ReadWritePaths=` line is added
  above it.
- The polygon assert requires `EnvironmentFile=` to come before `Environment=PATH=`. Neither line
  moves.

**Already settled on `dev`:** the package-install sentence (line 260) was rewritten by #93. This
part does not edit it; a merge conflict there is resolved in favour of `dev`'s wording.

**Read by the unit itself:** the unit's comments may cite this note by its path,
`docs/superpowers/notes/2026-10-26-agent-unit-hardening-threat-note.md`. The path therefore does
not change, and a reviewer checks that any such citation resolves to this file.

**The marker the verification phase reads.** Every item that still owes a measurement carries the
literal text `UNCONFIRMED (#91)` in the comment block directly above its directive line. In the
`ProtectProc=` case, which has no directive line, it is in the comment recording the rejection.
The same comment names the measurement that item owes. Each marker is one line, and no marker
appears anywhere else in the unit. After the code phase there are exactly six:
`SystemCallArchitectures=`, `ProtectControlGroups=`, `ProtectKernelModules=`, `RestrictSUIDSGID=`,
`MemoryDenyWriteExecute=` and the `ProtectProc=` rejection. The verification phase counts them with
`/usr/bin/grep -n "UNCONFIRMED (#91)" installer/systemd/maran-agent.service`, which must print six
lines after the code phase and none at merge. Item 6 of "Not yet confirmed" refers to the marked
items by directive name and does not repeat the marker, so that the count stays one per item.

**The directive values the verification phase reads back** from the loaded unit, with
`systemctl show maran-agent -p <Name>`: `SystemCallArchitectures=native`,
`ProtectControlGroups=yes`, `ProtectKernelModules=yes`, `RestrictSUIDSGID=yes`,
`MemoryDenyWriteExecute=yes`, and `ProtectProc=default` (not `invisible`). Written in the unit as
`true`, as the rest of the file does, and `systemctl show` prints booleans back as `yes`. The printed
form on systemd 257, where `ProtectControlGroups=` gained the values `private` and `strict`, is
recorded as observed rather than assumed to be `yes`.

## What a second reviewer must check

1. **That every adopted directive was measured, not reasoned.** For each one, the run's evidence
   must show four things: the polygon family, the directive in effect on the *loaded* unit
   (`systemctl show maran-agent -p <Directive>`), the installer check passing, and the list of
   rpcs driven with their results. A directive whose line in the unit carries no measurement is a
   reject, which is how the panel's own unit once acquired MDWE. Because the code phase wrote the
   lines before any measurement (see "The order of work"), this check also asks whether each
   measurement was taken **one directive at a time**. A run with all four added at once and
   everything passing proves the set works together. It does not show which directive a later
   failure belongs to, and it is not the measurement the markers owe.
2. **That each measurement observed the real sandbox, and names what it could not observe.** Mount
   effects can be checked against the agent's own namespace
   (`nsenter -t <agent pid> -m findmnt /sys/fs/cgroup` must show `ro`, and `/usr/lib/modules` must
   be inaccessible). Seccomp effects cannot be read off the running process, beyond
   `Seccomp: 2` in `/proc/<pid>/status`. Their positive control (a `chmod u+s` refused with
   `EPERM`) and inverse control (the same call accepted without the directive) can only run in a
   transient unit carrying the same property set. Adding a "run this" rpc to probe the real agent
   is forbidden (rules/security.md item 12). The record must say the control was a stand-in.
3. **That the read-only mounts actually hold against `CAP_SYS_ADMIN`.** A process holding that
   capability can remount a read-only bind mount as writable unless the mount syscalls are denied.
   On systemd 255 here, `@system-service` contains none of `mount umount2 move_mount fsopen
   fsmount open_tree mount_setattr pivot_root init_module finit_module delete_module`, and it does
   contain `chown setuid kill` (positive control). Both were found by expanding the group
   recursively. When an earlier run of this stage checked the note, the expansion was re-run and
   gave the same answer. The current run did not repeat it, because its code phase runs no systemd
   tool:
   375 names, none of the mount or module calls, and all three controls present.

   ```
   x() {
     systemd-analyze syscall-filter "$1" | sed 's/\x1b\[[0-9;]*m//g' | tail -n +2 \
       | sed 's/^ *//' | /usr/bin/grep -v -E '^#|^$' \
       | while read -r s; do case $s in @*) x "$s";; *) echo "$s";; esac; done
   }
   x @system-service | sort -u
   ```

   `RestrictNamespaces=true` (line 351 in the working file) is what stops `unshare`/`setns`, both of which the group
   allows, from building a fresh namespace instead. The reviewer must confirm the same on the
   systemd each family ships: 239 on the 8-series EL family the installer still accepts, 249 on
   Ubuntu 22.04, 252 on Debian 12 and the 9-series EL family, 255 on Ubuntu 24.04, 257 on
   Debian 13 and the 10-series.
4. **That the restore runs under `RestrictSUIDSGID`** with a backup whose home holds a setgid
   directory and a setuid file. Whether GNU tar under `--no-same-permissions` still *asks* for the
   bits decides whether restores keep working, and only a run settles it. **Also check that the
   filter covers `fchmodat2`.** On systemd 255, `@system-service` allows `fchmodat2` (it appears in
   the recursive expansion in item 3), and glibc 2.39 (Ubuntu 24.04, the 10-series EL family) uses
   it for `fchmodat(..., AT_SYMLINK_NOFOLLOW)`. If the `RestrictSUIDSGID=` filter on a given
   systemd does not cover that syscall, the bits can still be set through it. The positive control
   must therefore try both the `fchmodat` and the `fchmodat2` path on each systemd version
   measured. On a systemd that does not know `fchmodat2` at all (249, 252), the allow-list denies
   it, so there the question is breakage, not bypass. The breakage is expected to be nil, because
   the glibc those releases ship (2.35 on Ubuntu 22.04, 2.36 on Debian 12, 2.34 on the 9-series EL
   family) predates glibc's use of `fchmodat2`, which began in 2.39. Those versions were taken from
   the distributions' package lists and were **not checked on the polygons**. Separately: GNU `chmod`
   with a four-digit octal keeps a directory's existing setgid bit, so the `chmod 0750` on a new
   home (`account_operations.rs:1314`) would be refused on a host where `/home` itself carries
   `g+s`. Account creation would then fail loudly. That is acceptable, but it should be known.
5. **That the `ProtectProc=invisible` rejection carries its observation**: a process at the
   account's uid that survives suspension and deletion with the directive on, and is killed or
   refused (`userdel` exit 8) with it off. Both must be seen from outside the sandbox.
6. **That the contradiction is gone and nothing else says otherwise.** Run
   `/usr/bin/grep -n -E "MemoryDenyWriteExecute" installer/systemd/maran-agent.service`. Every line
   it prints must describe the directive as set, and it must print at least the directive line
   itself (the control). Also run `/usr/bin/grep -n "rules/rust.md" installer/systemd/maran-agent.service`.
   No line it prints may credit rules/rust.md with saying the agent runs no JIT code, because that
   rule does not say it.
7. **That `systemd-analyze security` moved the way the comments claim.** Before: 4.4 offline on
   systemd 255. After: each adopted directive shows ✓, with no other line changed.
8. **That each directive is actually recognised by every systemd the installer accepts.** An
   unknown key in a unit is logged as a warning and then **ignored**, so on an older systemd the
   file can say a directive is set while the loaded unit does not have it. The installer's
   preflight accepts more than the eight releases in rules/architecture.md
   (`installer/lib/10-preflight.sh:47` lists 17 pairs, among them `almalinux:8`, `rocky:8`,
   `rhel:8` and `ol:8` on systemd 239, and `ubuntu:26.04`). `RestrictSUIDSGID=` first appeared upstream in
   systemd 242. Whether the EL8 build backports it, and the other directives this unit already
   relies on, was **not checked**. The reviewer confirms, at least on the `alma8` polygon, that
   `systemctl show maran-agent -p <Directive>` reports the value and that the journal has no
   "Unknown key" line for the unit. If a directive is ignored there, the unit comment says so
   rather than claiming the protection everywhere.
9. **That MDWE did not break a child that tries to JIT.** PCRE2's JIT is the realistic case: nginx
   with `pcre_jit on`, and PHP with `pcre.jit=1`, its default. A validation run (`nginx -t`,
   `php-fpm -t`) executes as the agent's child, under MDWE. The long-running daemons are started
   by PID 1 and are not affected. The record must show those validations exiting 0 under the
   directive, and must show that a JIT warning on stderr is not mistaken for a failure.
10. **That this branch did not touch** the package-install sentence (line 260 on `dev`) that #93
   already rewrote, and that `maran structure` still passes. It parses this unit's `ReadWritePaths=` against
   `agent_paths.rs`.
11. **That nothing in the agent spawns a package manager as its own child** any more, so the
   inherited-filter concern of the first draft stays closed. Command:
   `/usr/bin/grep -rn -E "apt-get|dnf" --include=*.rs agent/crates | /usr/bin/grep -v "/tests/"`;
   its hits must be the distro adapter's names for the package manager passed to the install unit,
   not a `Command` spawn. The author did not run it in this stage.
12. **That no marker is left, and none was removed without its measurement.** At merge,
   `/usr/bin/grep -n "UNCONFIRMED (#91)" installer/systemd/maran-agent.service` prints nothing.
   Positive control: run the same command on a copy of the unit taken when the code phase ended,
   where it must print six lines. Nothing on this branch is committed without the owner's word, so
   the verification phase takes that copy before it changes anything, and keeps it beside its
   record. A marker that disappeared must have been replaced by a measured result or must have
   taken its directive with it. Compare the unit with that copy (`diff`), not only its final text:
   an empty search also results from a marker deleted with the directive kept.
13. **That the interface in "What the parts depend on from each other" still holds.** Both
   placeholders are present and unchanged, `__MARAN_QUOTA_DEVICE_BINDS__` alone on its line, one
   `ReadWritePaths=` line, unchanged, and `EnvironmentFile=` still above `Environment=PATH=`. A
   render through the installer's own `render_agent_unit` (not a `sed` copy) must show no surviving
   `__MARAN_` token.

## What the author cannot verify

- **No second reviewer.** This is an agent session.
- **Anything at all about the unit as the code phase leaves it.** By the owner's order, that phase
  runs no build, no check and no systemd tool. Its output has not been parsed by
  `systemd-analyze verify`, rendered by the installer or loaded by PID 1. Until the verification
  phase has run, every directive this change adds is unobserved, and the markers say so.
- **The install polygon is a container on the workstation's kernel** (x86_64, cgroup v2, run with
  `--privileged --cgroupns=host`). It therefore cannot observe:
  - the kernel autoload path for nftables expression modules. The workstation's Docker has
    probably loaded them already, and unloading them would change the workstation's kernel, which
    this work must not do.
  - a cgroup v1 or hybrid host, so the `release_agent` argument in item 1 stays reasoned.
  - SELinux on the RHEL family, since the polygon runs none.
  - the IA32-emulation configuration of each distribution's kernel.
  - `aarch64` at all.
- **Every polygon runs on one kernel.** An earlier draft of this note said only Ubuntu 24.04 and
  AlmaLinux 9 had polygons. That was wrong. `docker/polygon/images/` holds install images for
  `ubuntu22`, `ubuntu24`, `ubuntu26`, `debian12`, `debian13`, `alma8`, `alma9`, `alma10`, `rocky8`,
  `rocky9` and three Oracle versions. Of the eight releases in rules/architecture.md, only
  Rocky 10 has none. The images exist, so the **systemd** version of each release (userspace: 249,
  252, 255, 257, and 239 on EL8) *can* be measured, and the reviewer should ask for at least one
  run on 249 and one on 257 beyond the two-family minimum. That covers the `fchmodat2` question in
  item 4, the 257 change that added `private` and `strict` to `ProtectControlGroups=` (whether
  `true` still means the read-only mount there is otherwise only read from documentation), and
  item 8. What none of them can vary is the **kernel**. Every container runs on the workstation's
  kernel, so per-distribution kernel behaviour (IA32 emulation, module autoload, hidepid support)
  stays unmeasured, as listed above. How many of those versions were actually run is for the
  measuring stage to record. This note does not claim any of them.
- **That the binary measured is the binary that will ship.** A `--mode local` run measures this
  branch's unit around the published beta agent, not around this tree's agent. A spawn added on
  `dev` after that release has never run under these directives. Only an offline bundle built from
  the tree closes that gap.
- **`InstallPhpVersion` under the new directives.** After #93 the install runs in its own oneshot
  unit outside this sandbox, so the agent's side is only the `systemctl start` of that unit. That
  start is a PID 1 request, but it is one of the rpcs the verification phase must drive.
- **The whole rpc set.** The representative operations above are a sample. An rpc that was not
  driven (FTPS, SSL issuance, the database rpcs) is covered by argument only: none of them writes
  cgroups, asks for a setid mode, loads a module, or runs a non-native binary, on the evidence of
  the searches above.

## Corrections made after the unit was edited

A later run of this stage made these corrections at 19:20 on 2026-10-26. By then the code phase
had already edited the unit (see the top of this note). Each one was checked against the code and
the issues, not against those edits. They are listed so a reviewer can see which sentences came
after the change:

- The line numbers are pinned to the `dev` version of the unit, with the working file's md5 at
  that check.
- Item 3 names the `aarch64` counterpart of the non-native ABI.
- The deletion half of the `ProtectProc=invisible` rejection now states that nothing culls an
  account's processes before `userdel`, with the search that shows it.
- The `ProcSubset=pid` refusal adds `/proc/mounts`, marked as not measured.
- Reviewer check 4 states why `fchmodat2` should not cause breakage on systemd 249 and 252,
  marked as not checked on the polygons.
- Reviewer check 8 adds `rhel:8` to the systemd 239 releases the preflight accepts.
- "What the parts depend on from each other" adds this note's own path, which the unit may cite.

A third check, on 2026-10-27, found `dev` advanced past the pinned version (#93 merged). The pin,
the line numbers, the "Not in this change" paragraph, the #93 interaction, the merge-order check
and the `InstallPhpVersion` limit were rewritten for the new `dev`. The directive set, the
`ProtectProc=invisible` refusal, the marker contract and the OUTSTANDING review are unchanged.

Not changed: the directive set, the refusal of `ProtectProc=invisible`, the marker contract
(six `UNCONFIRMED (#91)` lines after the code phase, none at merge), the reviewer checks, and the
OUTSTANDING status of the second review.

## Independent agent review (five lenses), and the decision on each finding

Five independent agent reviews were run over the working tree of this branch: correctness,
security, rules, compatibility and design. They are agents, not the human second reviewer, and they
discharge nothing: the second review stays **OUTSTANDING** (see the top of this note). Fixers are
applying the decisions below; this section records the decision, not the diff.

**Verdicts.** Correctness: approve. Security: approve. Design: approve. Compatibility: approve.
Rules: changes required (two majors, below). No lens closes issue #91, and none could, because the
directives are unmeasured until the verification phase.

### Security-lens checklist result (rules/security.md, run verbatim)

| Item | Result |
|---|---|
| 1 Input validated at the boundary | n/a, a unit file has no external input |
| 2 Paths contained by descriptor walk | n/a, no path handling changed |
| 3 No shell strings | yes, the change adds directives and comments, no execution |
| 4 Config-file injection | n/a, the unit is static, no caller value is written into it |
| 5 SQL | n/a |
| 6 AuthZ | n/a, no endpoints |
| 7 Configuration documented, no secrets committed | yes, no new variable, no secret |
| 8 Secrets not leaked | yes |
| 9 Crypto | n/a |
| 10 Surface | yes, no new port, daemon or outbound call; the sandbox narrows |
| 11 Dependencies | n/a, none added |
| 12 Agent rpc | n/a, no new rpc |
| 13 Module reach into the agent | n/a, no `AgentCapability` change |
| Escalation: note exists, OUTSTANDING, plan matches the code | yes, with the debt below |

No item is "no", so nothing in the checklist blocks the branch. The escalation item is "yes" only in
form: the human second review is not done.

### Blocking and major findings, and the decision taken

There was no blocking finding. The major findings were these.

1. **`installer/systemd/maran-api.service` says the agent unit does not set
   `MemoryDenyWriteExecute`** (rules lens, major; correctness, security and design lenses raised the
   same sentence as minor). It is false: the agent unit sets it and has since before this branch, and
   the agent unit now explains why, so the two files contradict each other. **Decision: accept.**
   The api unit's comment is corrected in this branch by a part that owns that file, to say that the
   agent unit sets the directive and why, keeping the point that the api's own omission is
   load-bearing. This also makes the claim "all edits are in the agent unit alone" untrue: the
   branch now edits two unit files, one of them only a comment. Read the earlier sections with that
   in mind; the directive set, which is the security-relevant part, is still confined to the agent
   unit.
2. **The old header "Nothing below has been observed to work" in the agent unit contradicts its own
   list** (rules lens, major; the others minor), because item 5 of that list is now confirmed and
   item 6 was rewritten. **Decision: accept.** Reword the header to say that items not marked
   confirmed have not been observed, and do not rewrite the rest.
3. **The human second review is not done** (security lens, major, a hand-off flag and not a code
   change). **Decision: accept as stated.** This branch must not merge to `main`, and the owner is
   told at hand-off that the root agent's systemd unit, an installer privileged surface, carries an
   outstanding review.

### Minor findings, and the decision taken

- **Comment defects in the agent unit** (correctness, security, rules, design): a sentence about
  systemd 242 appended to the `RestrictSUIDSGID` paragraph, and lines over 100 columns. **Accepted.**
  The version note moves into its own paragraph and the long lines are rewrapped.
- **The note carries the earlier B1, B2 and B3 wording that the unit has since corrected** (security,
  rules): only `FileMode` refuses setid bits, and `tar` is told `--no-same-permissions` rather than
  being the refuser; cgroup writes work through uid 0 file ownership, and `CAP_SYS_ADMIN` could only
  remount the read-only view. **Accepted.** The note is brought in line with the unit's wording by
  the part that owns the note's body sections; until it lands, where the two differ, the unit wins.
- **The `ReadWritePaths=/var/lib/maran-ftps` documentation gap** (security): unrelated to #91 and
  recorded above as not part of this change. **Decision: not fixed here.** It is for the owner to
  open as its own issue; this branch does not close it.
- **EL8 reasoning in the `RestrictSUIDSGID` comment** (compatibility): EL8 is not a supported
  system (rules/architecture.md lists Ubuntu 22.04 and 24.04, Debian 12 and 13, Alma and Rocky 9
  and 10, all on systemd 249 or newer), so an "EL8 polygon" measurement would waste a run.
  **Accepted.** The sentence and the owed EL8 measurement are replaced by the oldest supported
  systemd (Ubuntu 22.04, 249) and the EL9 build (252). The `rhel:8` mention in reviewer check 8
  concerns what the installer's preflight accepts, which is a separate fact from what is supported,
  and is left as written.
- **Restore of an archive made by the previous release** (compatibility): a backup taken before this
  change may hold setuid or setgid members, and if `tar`'s chmod of one is refused rather than
  dropped, old backups would fail to restore after an upgrade. **Accepted as a phase B measurement:**
  restore such an archive, with a setuid file and a setgid directory, on both distribution
  families, and record the result. The existing fallback stands: remove the directive if the restore
  fails.
- **`SystemCallArchitectures=native` on `aarch64`** (compatibility): **Accepted.** The marker says
  the loaded unit is read back on both architectures, or that `aarch64` is covered by the same
  read-back, so the claim is not left implicit.
- **Upgrade and rollback path** (compatibility): checked in `installer/lib/70-services.sh`, which
  runs `systemctl daemon-reload` and then `systemctl restart maran-agent.service` (lines 316 and
  324), so the running daemon picks up the new sandbox on upgrade rather than at the next reboot.
  **No unit change.** Rollback restores the previous unit text; phase B should confirm that on a
  directive that is removed after it proved to break an operation.
- **Not every claim of the `ProtectProc=invisible` rejection is checked** (correctness): the review
  could not confirm that nothing kills an account's processes before `userdel` on the deletion path.
  **No change.** The refusal stands, which is the safe direction, and that half of it is recorded as
  only partly checked.
- **Per-directive `UNCONFIRMED (#91)` blocks are long and duplicate the shared rule** (design):
  **Deferred.** Trimming speculation that no longer applies is done when phase B replaces each
  marker with a measurement, not before, because trimming now would remove the reasoning the
  measurement is meant to test.
- **Four directives are unmeasured** (security, correctness): `SystemCallArchitectures=native`,
  `ProtectControlGroups`, `ProtectKernelModules` and `RestrictSUIDSGID` carry six markers.
  **Accepted as phase B work, no code change.** Each is measured alone in a real-systemd polygon;
  the `chmod g+s` on `/home` at account creation is the one most likely to break something and is
  measured first. A directive that breaks an operation is removed, not weakened by an exception.

### Status after this review

The second review remains **OUTSTANDING**. The branch may stay a branch and must not merge to
`main`. The unit and this note are untracked or unstaged; committing them is the owner's decision.

## Verification evidence

Phase B ran after the code was written. Raw measurement logs are in the session scratchpad
(`m91/out-ubuntu24.log`, `out-alma9.log`, `out-alma9-agent.log`); they are not in the repository.
The second review stays **OUTSTANDING**. Nothing here discharges it, and the branch must not merge
to `main`.

### Gates (maran, worktree `91-installer-agent-unit-hardening`)

- `maran check`: all toolchains present. `maran structure`: STRUCTURE-OK.
- No Rust, C#, proto, endpoint, migration or dependency file differs from `dev`
  (`git diff dev --stat` on `scripts/test-baseline.txt`, `scripts/api-contract-baseline.txt`,
  `proto`, `backend`, `agent` is empty), so format, proto, api, migrate, licenses and backend test
  do not apply. Handshake was not run: neither binary changed and it never loads the unit.
- `maran test rust`: TEST VERDICT OK, 30 targets, 2205 passed / 0 failed / 141 ignored, equal to
  the 30 rust rows of `scripts/test-baseline.txt`. This is a regression floor only: the
  `*_on_a_real_host` suites run without systemd and cannot see the sandbox.
- Static checks: exactly one line each of the five adopted directives; no `ProtectProc=` or
  `ProcSubset=` line; zero `__MARAN_` placeholders after rendering.
- `systemd-analyze security` (255.4, offline): exposure 4.4 on `dev`, 3.6 on the branch; only the four
  new directives changed, 0.2 each.
- Polygon image builds for ubuntu24 and alma9 succeeded and ran the new static asserts (below).
- **Failed gate, not a pass:** `maran installer` end-to-end on ubuntu24 and alma9. The install stops
  at step 65 (`relation "firewall.BanEpisodes" does not exist`, the published API artifact failing
  to migrate), before step 70 installs the units, so 8 checks per OS fail. This was not shown to
  fail identically on pristine `dev`, so it is inconclusive, not attributed. Consequence: no
  rpc was driven through the real API.

### Measurements on real systemd

Ubuntu 24.04 (systemd 255) and AlmaLinux 9 (systemd 252), privileged containers with systemd as PID 1
on the workstation's kernel (7.0.0-38, IA32 emulation on). Nine unit variants per family (dev, dev plus
each directive alone, branch, dev minus MDWE as a control, dev plus `ProtectProc=invisible`, dev plus
`ProcSubset=pid`). A driver ran as the unit's `ExecStart` and invoked the programs the agent spawns.

- **All four new directives and MDWE:** loaded and read back correctly; no unknown-key or parse lines
  in the journal; the real agent starts, `NRestarts=0`, socket `srw-rw---- root maran`, clean stop.
  Exposure 4.4 to 3.6 on both families.
- **Operations unchanged under every variant, on both families:** useradd, home `chmod 0750` (also
  with `g+s` on `/home`), chgrp, chpasswd, usermod, userdel, `nginx -t` (also with `pcre_jit on`),
  `php-fpm -t`, `systemctl` reload, is-active, show and daemon-reload, openssl certificate,
  crontab set, list and remove, tar create, tar restore with `--no-same-owner --no-same-permissions`
  (setuid file and setgid dir come back without the bits), nft add, list and delete, SFTP mount unit
  enable and disable. Red in every variant including `dev` (baseline): `setquota` (no quota
  filesystem in the overlay).
- **SystemCallArchitectures=native:** a hand-built i386 ELF (`int 0x80`) exits 42 without the
  directive and dies with SIGSYS (159) with it.
- **ProtectControlGroups=true:** `/sys/fs/cgroup` rw becomes ro; writing the agent's own `cgroup.freeze`
  and moving a process to the root `cgroup.procs` succeed without it and give EROFS with it. nginx
  reload and the SFTP unit enable and disable through PID 1 still work. Only cgroup v2 was measured.
- **ProtectKernelModules=true:** a fake `/usr/lib/modules/m91-fake` and `/lib/modules/m91-fake` are
  listed without it and hidden with it; nft, useradd and setquota unchanged. Effect on real module
  loading not observed.
- **RestrictSUIDSGID=true:** `chmod 4755`, `chmod 2755` on a directory, `mkdir -m 2755` and
  `cp -p` of a setuid binary succeed without it and fail with it. The agent's own restore flags still
  succeed. `tar --preserve-permissions` of an archive holding setuid entries fails (rc 2), but the
  agent never passes that flag. `useradd` and the `chmod 0750` on a new home succeed even with a
  `g+s` `/home`.
- **MemoryDenyWriteExecute=true:** a Python `mmap(RWX)` probe is refused with it and granted when it is
  removed (the probe can see the directive). `nginx -t` and `php-fpm -t` exit 0. PHP with
  `pcre.jit=1` prints "Allocation of JIT memory failed, PCRE JIT will be disabled" to stderr, exits 0
  and still works. So MDWE is not a hidden breakage for these children, at the cost of the JIT.
- **ProtectProc=invisible, correction to the earlier reasoning.** The note and the unit say the
  directive would blind the daemon to other users' processes. Measured: it does not. The unit's
  process carries supplementary group 0, which is the `hidepid` exemption group, so another
  user's process stays visible and `pkill --uid` still kills it. The directive is inert here, not
  harmful. The refusal is kept (no benefit, and it would turn harmful if the exemption ever went), but
  the unit's stated reason is wrong and must be corrected.
- **ProcSubset=pid, refused on evidence:** on alma9 `pkill` fails (rc 102, "/proc must be mounted") and
  the suspended account's process stays alive; `/proc/mounts` becomes unreadable and `setquota`'s
  mountpoint lookup changes its message.
- **Pre-existing gap found, not caused by #91 (worth its own issue):** `userdel --remove` of a user with
  a live process refuses outside any sandbox (rc 8) but succeeds inside the agent's unit on every
  variant including `dev` (rc 0, account removed, process still alive). The note's claim that
  `userdel` protects against live processes does not hold under the unit. Hypothesis, not measured:
  the unit's mount namespace changes `/proc/<pid>/root`, which `userdel`'s in-use check compares.
  The deletion path therefore leaves processes running under a reusable uid unless something kills
  them first.

### Mutation proof (shell asserts T1, T2 and T4 in `docker/polygon/asserts/parts/units-and-preflight.sh`)

Each mutation ran alone on a copy of the tree; every restore was checked with `cmp`.

- T1: deleting `RestrictSUIDSGID=true`, setting `ProtectControlGroups=false`, appending
  `ProtectKernelModules=no`, commenting out `SystemCallArchitectures=native`, deleting
  `MemoryDenyWriteExecute=true`: all five killed by `assert_the_agent_unit_keeps_its_measured_hardening`.
- T2: appending `ProtectProc=invisible`, `ProtectProc=ptraceable`, `ProcSubset=pid`: all killed by
  `assert_the_agent_unit_never_hides_other_users_processes`; the inverse control `ProtectProc=default`
  stayed green as required.
- T4: indenting the `__MARAN_QUOTA_DEVICE_BINDS__` line, adding `# __MARAN_FOO__`, deleting the
  `__MARAN_GROUP__` gsub in `render_agent_unit`: all killed by
  `assert_the_agent_unit_renders_through_the_installer`. The gsub mutation died on the render's
  placeholder guard before reaching the `Group=maran` grep, so that grep alone is not proven.
- Inline guards: removing the count-is-one branch and setting `AGENT_UNIT_HARDENING_COUNT=4` were both
  killed. 14 of 14 targets killed on the first run; no test was strengthened.

### Still unverified

- No rpc went through the panel API or gRPC. The agent's own Rust syscalls (for example
  `close_range` in `fork_as_account`, `quotactl`) under the new directives were not observed. The
  Ubuntu agent binary is a release build from the owner's tree and the Alma binary an older debug
  build; neither is built from this branch.
- Quota is unobservable in the containers, so the `quotactl` and device path is untested.
- Not run: Ubuntu 22.04 (249), Debian, AlmaLinux 8 (239, unknown-key behaviour), systemd 257, SELinux
  enforcing, `aarch64`, cgroup v1 or hybrid hosts, kernels without IA32 emulation, cold kernel
  module autoload, whether the read-only cgroup mount holds against `CAP_SYS_ADMIN` remount.
- MDWE: mariadb-dump, FTPS, certificate install through the real agent code, and long-running nginx
  workers with JIT were not exercised; the `nginx -t` result is weak evidence on its own.
- Restore was tested with a tar built on the host, not an archive from a previous Maran release.
- The markers: all six `UNCONFIRMED (#91)` lines remain in the unit. The measurements above support
  replacing them for the four directives on systemd 252 and 255, with the gaps above stated in the
  replacement. Only the unit author may do that, and the `ProtectProc` paragraph must be corrected
  in the same edit. The T3 no-marker gate has deliberately not been added; it goes in when the
  markers go.
- `installer/systemd/maran-api.service` lines 140-143 still say the agent unit does not set
  `MemoryDenyWriteExecute`, which is false.

### Status

The second review remains **OUTSTANDING**. The branch must not merge to `main`. A human reviewer
must read this note, the measurements, and the unmeasured list before sign-off.
