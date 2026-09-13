# Threat note — installer privileged steps (artifact staging, PostgreSQL exposure)

rules/security.md: "Changes to auth, session/token handling, the agent's privs module, license
verification, or the installer's privileged steps require a second reviewer and an explicit
threat note in the PR description: what could an attacker do with this surface, and why is it
safe now."

**The second-reviewer requirement is OUTSTANDING.** This note is the written half only. No
second reviewer has looked at these changes. They must not ship without one.

Scope of the changes: `installer/lib/50-artifacts.sh`, `installer/lib/30-postgresql.sh`,
`installer/uninstall.sh`, `installer/lib/90-finish.sh`.

## 1. Artifact staging — panel -> root privilege escalation (was exploitable)

### What an attacker could do, before

The api runs as the unprivileged `panel` uid. `40-user.sh` creates `/var/lib/maran` as
`panel:panel 0750` and `maran-api.service` lists it in `ReadWritePaths=`, so `panel` owns that
directory both in the filesystem and under the sandbox. `50-artifacts.sh` staged downloaded
release archives at `/var/lib/maran/artifact-staging` with a bare `mkdir -p`.

Owning the parent directory is enough to place any entry at that name. `mkdir -p` exits 0 on an
existing path, follows a symlink, and checks neither owner nor mode — so a compromised api
process could, at any moment before the next install or upgrade, create either a symlink or a
plain directory it owns at the staging name and wait. `fs.protected_symlinks` does not cover
this: it only refuses symlinks inside world-writable **sticky** directories, and
`/var/lib/maran` is `0750` without the sticky bit.

Root would then download the release archives into a directory the api controls. The Ed25519
manifest signature and the per-archive sha256 did not close this, because
`download_and_verify_online` checksums all three archives in one loop and `unpack_artifacts`
extracts them afterwards: the checks authenticate the bytes at check time and nothing looks at
them again at extract time. The api could replace `agent.tar.gz` in that window. The extracted
file becomes `/usr/local/maran/agent/maran-agent`, which `maran-agent.service` runs **as root**.

That is a full panel -> root escalation, reached from an api compromise with no further
privilege, needing only that the operator later re-runs the installer or upgrades. It was
demonstrated end to end against the real step file (see the audit report's Finding 5 section:
the good case installs "GENUINE agent payload", the attack installs "ROOT BACKDOOR").

### Why it is safe now

- Staging moved to `/var/lib/maran-artifact-staging`, directly under `/var/lib` (`root:root
  0755`). No unprivileged uid can create an entry there at all, and it is outside every path in
  either unit's `ReadWritePaths=`.
- `prepare_staging_dir` replaces `mkdir -p`: it removes whatever is at the path (unlinking a
  symlink, never its target), creates a real directory `root:root 0700` with `install -d`, and
  then **refuses** — aborting the install — if the result is a symlink, is not a directory, is
  not owned by uid 0, or is not mode 0700. Proven to fire: mutating the creation to `0770` makes
  the install abort with "found owner uid 1000, mode 770".
- The good case is proven still ACCEPTED (rules/testing.md's inverse control): a correctly
  signed manifest with matching checksums installs the genuine payload, unchanged.
- Defence in depth: each archive's sha256 is re-checked against the signed manifest immediately
  before its own extraction. With the staging directory root-only nothing should be able to
  change the bytes in between; this check is what makes that an observation rather than an
  assumption. Proven: the swap attack now aborts with "checksum mismatch for agent.tar.gz" and
  installs nothing.

### Residual risk

- Root itself, and anything root runs, can still write staging. That is not a boundary Maran
  can defend and is not a change from before.
- The manifest's own JSON reader (`manifest_field`) is a fragile awk matcher; see Finding 9 in
  the audit report. It is a correctness and robustness concern, not an escalation: the manifest
  is authenticated by Ed25519 before any field is read from it.

## 2. PostgreSQL was listening on TCP while the installer said it was not

### What an attacker could do, before

`step_postgresql` wrote `listen_addresses = ''` and then ran
`systemctl reload || systemctl restart`. `listen_addresses` has GUC context `postmaster` — only
a full restart applies it — and the reload succeeds on both supported families (Debian's meta
`postgresql.service` is literally `ExecReload=/bin/true`; RHEL's is `kill -HUP $MAINPID`), so
the `||` short-circuited and the restart never ran. The server kept the listener the package
started it with, `listen_addresses='localhost'` on Debian, while the step printed
"PostgreSQL ready: ... unix-socket only."

The exposure is local (127.0.0.1:5432), so the attacker is any local uid, plus anything that
can reach loopback: an SSH port-forward from a customer account, an SSRF in a hosted site. What
made it dangerous was not the listener alone but the message: the operator was told a surface
did not exist, so nobody would think to check it. rules/architecture.md treats a message that
describes what the code does not do as a defect of equal severity to the behaviour.

### Why it is safe now

- The step does `systemctl restart` unconditionally. No reload, no `||` fallback.
- `pg_assert_no_tcp_listener` asks the running server `SHOW listen_addresses` and aborts the
  install if it is non-empty. The claim in the final message is now backed by an observation of
  the running server rather than by the fact that a config file was written.

### Residual risk / UNPROVEN

- Between the package's own post-install start and this step's restart, PostgreSQL is briefly
  listening on loopback. Closing that window entirely would mean configuring before the package
  starts the server, which neither family's packaging offers cleanly. The window is now bounded
  by one install step instead of lasting until the next reboot.
- On Debian the restart of the meta unit reaches the real cluster through the instance unit's
  `PartOf=postgresql.service`. That is documented systemd propagation but has **not** been
  observed on a live Debian host in this audit (no systemd available here). If it ever did not
  propagate, `pg_assert_no_tcp_listener` aborts the install rather than printing a false claim —
  which is the behaviour this note is asserting, not the propagation.

## 3. uninstall reported dropping a database it may not have dropped

`drop_database` ran two `psql` commands each ending in `|| true`, then unconditionally set
`MARAN_DATABASE_KEPT=0` and printed "Database and role dropped." A failure of either — `sudo`
absent, peer auth refused, the server down — produced a success message and told the rest of
the uninstaller the data was gone, so the encryption key protecting it was then removed as no
longer protecting anything. The operator believes the data is destroyed; it is on disk,
unprotected by anything the panel still holds.

Now both DROPs must succeed before `MARAN_DATABASE_KEPT=0` and the success message; otherwise
the operator gets an explicit warning that nothing, or only part, was removed, and
`MARAN_DATABASE_KEPT` stays 1 so the key keeps being treated as protecting live data.

## 4. `sudo` was an undeclared dependency of two privileged steps

`30-postgresql.sh` and `uninstall.sh` invoked `sudo -u postgres psql`. `sudo` is in neither
family's `base_packages_for_family` and is absent from minimal Debian netinst and minimal RHEL
images. Both now use `runuser -u postgres --`, from util-linux, which is part of the base system
on both families. The installer is already root, so only the uid switch was ever needed; this
removes a dependency rather than adding one, and removes a `sudo` invocation from a root
context, which is one less path through a setuid binary.

## 5. The restore staging root was created the way the other two used to be

Scope of this section: `installer/lib/40-user.sh`. Added after sections 1–4, same file by
instruction rather than a new note.

**Where this note falls, stated plainly (rules/security.md).** The rule as amended today requires
the threat note to be written FIRST, and says that one written afterwards "is a justification
validating a choice already made" rather than a check on it. This section was written AFTER the
change, so it is the weaker kind. What partly offsets it is that the reasoning it records is not
retrospective: the premise was measured before the fix, on both families, and the measurement
CONTRADICTED the specification it was handed (below), which is the thing an after-the-fact
justification is supposed to be incapable of doing.

### What an attacker could do, before

`/home/.maran-restore` (`AgentPaths::RESTORE_STAGING_ROOT`) is where a restore extracts a
customer's home before swapping it into place, and where the previous home is parked during the
swap. It was created with a bare

    install -d -o root -g root -m 0711 /home/.maran-restore

which is the exact shape that was a proven panel -> root escalation twice already in this file
(section 1 here, and the bulk scratch in `docs/superpowers/notes/2026-09-05-backups-threat-note.md`
section 1): `install -d` exits 0 on a path that already exists, follows a symbolic link there, and
checks neither owner nor mode.

Measured today as root in `ubuntu:24.04` (coreutils 9.4) and `almalinux:9` (coreutils 8.32), and
the measurement is WORSE than the specification handed to this session claimed. `install -d
-o root -g root -m 0711` against a symlink pointing at an existing directory:

- succeeds silently, leaving the symbolic link in place; and
- applies the ownership and the `0711` to the link's **TARGET**.

So root does not merely fail to notice the plant — root widens the attacker's own directory to
the mode the panel afterwards trusts, and every restore from then on extracts a customer's home
through the link into a tree the attacker controls. (A link pointing at a path that does NOT
exist is a different case: `install -d` fails with "cannot change permissions", and `set -e`
stops the install. Only the link-to-a-real-directory shape is silently accepted.)

Not exploitable **today**: `/home` is `root:root 0755` on both families, so no unprivileged uid
can create the entry. The safety was therefore a property of a directory nobody in this step
asserts anything about, not of the step — the same standing this path had in the two cases that
did turn out to be exploitable, both of which were also "safe" until a parent's mode changed.

### Why it is safe now

    remove_unless_root_only_directory /home/.maran-restore
    install -d -o root -g root -m 0711 /home/.maran-restore
    assert_root_only_directory_with_mode /home/.maran-restore 711

- `remove_unless_root_only_directory` deletes whatever occupies the path unless it is ALREADY a
  real directory owned by uid 0. Unlinking a symbolic link removes the link and not its target,
  so this is safe to run against a path aimed at something valuable.
- `assert_root_only_directory` was hardcoded to mode `700`. It is now
  `assert_root_only_directory_with_mode <path> <octal>`, with the old one-argument name kept as a
  wrapper passing `700` — `docker/polygon/assert-installer-steps.sh` calls the one-argument form
  and needs no change. The mode is a parameter rather than a loosened check, so `0711` is asserted
  exactly and `0755` is still a refusal.
- The mode stays `0711`, which is what `docker/polygon/assert-installer-steps.sh` pins. **No
  `docker/` change is required by this fix.**

### Why the removal is CONDITIONAL, where the scratch's is not

An unconditional `rm -rf -- /home/.maran-restore`, which is what this session was handed, would
destroy customer data. `restore_backup.rs` returns `HomeParkedAt { path }` when the second rename
of a restore fails, and leaves the account's ONLY home directory at
`/home/.maran-restore/<account>.previous.<id>` for an operator to move back by hand — that path in
the error message is the whole recovery procedure. Step 40 runs again on every install and every
upgrade, so an unconditional removal would delete that home in the middle of the recovery the
agent's own error text sends the operator into.

Nothing an unprivileged uid can plant is a root-owned real directory (creating an entry in `/home`
needs root, and `chown` to uid 0 needs root), so "keep it only when it is already a real directory
owned by root" removes every state an attacker can create and no state an operator depends on. The
MODE is deliberately not part of that decision: a wrong mode on a real root-owned directory is
repaired by the `install -d` and then asserted, whereas deleting the directory over it would take
the contents with it.

### Evidence

A harness run as real root inside `ubuntu:24.04` and `almalinux:9`, 12 assertions, 0 failures on
each, sourcing the real `installer/lib/40-user.sh`:

- the unguarded `install -d` follows the planted link and re-modes the target (the premise);
- the guarded removal destroys the link and leaves the target directory, its content and its
  `0700` untouched;
- a clean run of `step_user` produces `0:0:711`, a real directory;
- the FULL step over a planted link produces `0:0:711` and never touches the victim;
- a re-run preserves `acme.previous.b1/public_html/index.html` and repairs a widened `0755`;
- the gate refuses a symlink, a `panel`-owned directory, a `0755` one and a `0700` one (so the
  mode parameter is demonstrably read), and — the inverse control — ACCEPTS the real `0:0:711`;
- the one-argument wrapper still accepts a `0700` scratch and still refuses a `0711` one, so it
  really passes `700`.

### Residual risk / UNPROVEN, and what a reviewer must check

**The second-reviewer requirement is OUTSTANDING for this section too.** Named specifically:

1. Whether the conditional removal is the right trade. It is a deliberate deviation from the
   unconditional `rm -rf` used for the scratch and the artifact staging, and it means a
   root-owned directory planted at this path by a root-level compromise survives the install.
   That is judged acceptable because a root-level compromise needs no symlink; a reviewer who
   disagrees should say what the installer should do with a parked home instead.
2. The window between `remove_unless_root_only_directory` and `install -d` is unguarded, as it is
   for the other two directories. Nothing that can win that race exists while `/home` is
   `root:root 0755`; that is an argument, not a measurement.
3. Not observed on a live host with systemd, or under SELinux enforcing on a real AlmaLinux
   install — only in containers. Ownership and mode semantics do not depend on either, but the
   claim is scoped to what was run.
4. The bulk scratch (`/var/lib/maran-scratch`) keeps its UNCONDITIONAL `rm -rf`. That was not
   changed here and is believed right — it holds only in-flight dumps — but if a dump can ever be
   in flight while step 40 re-runs, the same data-loss argument would apply to it.

---

## Section: relocating the SFTP jail base to `/var/lib/maran-sftp` (2026-09-07)

**This note is the WEAKER kind, and says so on its own first line.** `rules/security.md`
requires the threat note to be written *first*, before the change. This one was written after
the installer, unit, agent constant and polygon edits were already applied in the working tree.
It is therefore a justification of a choice already made, which `rules/architecture.md` names as
the mechanism that stops the next reader looking — read the "what a reviewer must check" list
below as the part that matters, not the argument above it.

### The surface being changed

Privileged installer steps (`installer/lib/40-user.sh`, `installer/lib/86-sftp.sh`), the root
daemon's systemd sandbox (`installer/systemd/maran-agent.service` `ReadWritePaths=`), the
uninstaller's handling of live bind mounts of customer homes, and the agent's `SFTP_JAIL_ROOT`
constant. All run as root; the last one decides where a chroot is.

### The defect, measured before the change

`installer/lib/40-user.sh` creates `/var/lib/maran` as `panel:panel 0750`, and the SFTP jail base
was `/var/lib/maran/sftp`. OpenSSH walks EVERY component of a `ChrootDirectory` path and refuses
the session if any component is not root-owned or is group/other-writable. Reproduced by hand in
a privileged container on **both** supported families (`sshd -E`, a hand-built jail, a real
password login):

```text
Accepted password for <login> from 127.0.0.1 port 58788 ssh2
bad ownership or modes for chroot directory component "/var/lib/maran/"
```

and, after `chown root:root /var/lib/maran` and nothing else, `sftp> pwd` →
`Remote working directory: /`. Mode was left at `0750` in the working case, so the trigger is
the OWNER of the component, not its mode.

Two consequences, and the second is the security one:

1. **Availability.** Step 40 runs before step 86 on a real install, so SFTP has never worked on
   a real server. The polygon suites passed only because the images used to manufacture a
   root-owned `/var/lib/maran` themselves.
2. **Escalation shape.** The `panel` uid owned an ancestor of every customer's chroot. The owner
   of a directory can rename an entry inside it aside and leave an entry of its own at that name
   without ever having permission to enter it — here, at the name of a chroot root trusts. This
   is the third instance today of one shape; the other two (bulk dump scratch, release artifact
   staging) were proven panel→root escalations and were fixed by the same relocation.

### What an attacker could do with the change, and why it is safe now

- The new base is `/var/lib/maran-sftp`, a SIBLING of the panel-owned state root. `/var/lib` is
  `root:root 0755` on both families, so no unprivileged uid can create, replace or rename an
  entry at this name at all, and every path component of every chroot is root's.
- Mode is `0700`, TIGHTER than the old base's `0755`. Only root traverses it from the outside
  (sshd chroots as root before it drops privileges; systemd performs the bind mount as root).
  The per-account jail one level below stays `0755`, which is what an SFTP login sees as `/`.
  Under the old panel-owned `0750` parent the base's `0755` was masked; directly under a
  world-listable `/var/lib` it would have published one directory name per hosting account to
  every local uid. So the relocation does not widen anything.
- Creation is `remove_unless_root_only_directory` → `install -d -o root -g root -m 0700` →
  `assert_root_only_directory`, the same gate family the scratch and the restore staging root
  use. `install -d` alone exits 0 on an existing path, FOLLOWS a symlink, and checks neither
  owner nor mode; the removal unlinks a planted link (never its target) and the gate turns the
  result into a refusal rather than a hope.
- The removal is **CONDITIONAL**, like the restore staging root's and unlike the scratch's, and
  the difference is CUSTOMER DATA: each jail has the account's real home bind-mounted at
  `<account>/home`, so an unconditional `rm -rf` on an upgrade would recurse through a live
  mount and delete a customer's home. Only a state an unprivileged uid could have created (a
  symlink, or a directory another uid owns) is removed; a root-owned real directory is kept and
  merely re-moded.
- `ReadWritePaths=` gains `/var/lib/maran-sftp`. This does not widen the sandbox: the same tree
  was already writable as a child of `/var/lib/maran`. `scripts/lib/check-structure.sh` compares
  the unit's writable set against the `AgentPaths` constants, so a missed entry is a build
  failure, not a runtime one.

### A second, separate defect found while making this change

`installer/uninstall.sh` globbed the per-account mount units as
`/etc/systemd/system/var-lib-maran-sftp-*.mount`. A `.mount` unit's file name is systemd's
escaping of its own `Where=`, `-` is the escaping of `/`, and a literal `-` therefore becomes
`\x2d`: `systemd-escape -p --suffix=mount /var/lib/maran-sftp/acme/home` returns
`var-lib-maran\x2dsftp-acme-home.mount`. The readable glob matches **nothing**, so after the
relocation every jail's bind mount of a customer home would have survived an uninstall on a host
the operator believed was clean — and `remove_var_lib`'s still-mounted refusal would then have
declined to remove `/var/lib/maran` as well. Fixed with a quoted-literal glob, verified both by
`systemd-escape` and by matching against planted file names. The agent side needed no change:
`AccountJail::escape_path` already implements systemd's full rule.

### What a reviewer must check — the requirement is **OUTSTANDING**

No second reviewer can be obtained by this session. Per `rules/security.md` this change is
**NOT compliant**, may live on a branch, and **MUST NOT merge to `main`** until reviewed.
Specifically unverified by the author:

1. **Never observed on a real host.** Everything was measured in containers with no systemd and
   with a `systemctl` stand-in that emulates `enable --now <unit>.mount`. What is proved is that
   the unit NAME and the mount are right, not that a real systemd schedules the unit at boot,
   and not that a real `sshd` under systemd's own sandboxing behaves as the hand-started one did.
2. **SELinux.** AlmaLinux under enforcing SELinux gives `/var/lib/maran-sftp` no policy label of
   its own. `/var/lib/maran` inherited `var_lib_t` and so does the sibling by default file
   context, so no change is expected — but this was NOT measured, and an sshd chroot into a
   mislabelled directory is exactly the kind of failure that reproduces only on a real install.
3. **Migration is DELIBERATELY NOT PERFORMED.** See below; a reviewer should decide whether the
   printed instructions are the right answer or whether the installer should do more.
4. **The `0700` choice.** It is tighter than the old base and passes on both families in my runs,
   but OpenSSH's requirements on non-final path components are being taken from its observed
   behaviour, not from a specification reading. If some configuration needs a non-root traversal
   of the base, `0700` breaks it and `0755` would not.
5. **The window between the conditional removal and `install -d` is unguarded**, exactly as it
   is for the other two directories. Nothing that can win that race exists while `/var/lib` is
   `root:root 0755`. That is an argument, not a measurement.

### Existing installs

An upgrade does **not** migrate old jails, and this is a decision, not an omission:

- Everything under `/var/lib/maran/sftp` is customer data reachable through a live bind mount of
  a real home. An `rm -rf` across one deletes the home it points at.
- Each existing SFTP login's passwd home is the OLD jail path, so relocating the directory
  without `usermod`-ing every login leaves logins pointing at a path that no longer exists.
- **Nothing is lost that the operator had.** Those logins have never worked — sshd refused every
  one of them at the old base. There is no working credential to preserve, only a directory tree
  and some mount units.

`step_sftp` therefore calls `report_legacy_jails`, which prints, when `/var/lib/maran/sftp`
exists: the fact that it is neither migrated nor deleted, why, and the per-account manual
procedure (disable the old `var-lib-maran-sftp-*.mount` unit, remove it, `daemon-reload`,
confirm nothing under the old base appears in `/proc/self/mounts`, recreate the account's SFTP
users in the panel so the agent builds the new jail/unit/passwd home, and only then
`rm -rf /var/lib/maran/sftp`). The old-path units are also why the uninstaller's OLD glob is
still the right spelling for a legacy host and the new quoted one for a current host — a
reviewer should decide whether the uninstaller should carry both.

## Section: `/var/log/maran` — root writes leaf files inside a panel-owned directory (2026-09-07)

**This section was written BEFORE the change it describes, and that is deliberate.** The three
sections above it were written after their fixes and say so; `rules/security.md` names the
after-the-fact note as a justification for a choice already made rather than a check on it. What
follows therefore states the attacker, the primitive, the measurement and what the proposed fix
will and will not close, while the fix is still a proposal. Where the implementation later differed
from this plan, the difference is recorded at the end of the section rather than by editing the
paragraphs above it.

### The surface being changed

`installer/lib/40-user.sh`'s `create_directory_layout` (the directory itself), `install.sh`'s
`setup_logging` (root's install log), `installer/nginx/maran.conf` (the panel vhost's access and
error logs, opened by the root nginx master), `installer/systemd/maran-api.service`
(`ReadWritePaths=`/`NoExecPaths=`), `installer/uninstall.sh` and the polygon's layout assertions.
This is an installer privileged step, so the escalation rule applies in full.

### The attacker

The `panel` uid — that is, **anything that compromises the maran-api process**: an RCE in the API,
a deserialization bug, a path traversal that lands a write, or an operator-installed module running
in the panel process. The panel uid is unprivileged by design (`rules/architecture.md`: the API is
never root), and the whole product's security argument rests on a compromise of it not being a
compromise of the server.

### The defect, measured before the change

`installer/lib/40-user.sh:29` creates `/var/log/maran` as `panel:panel 0750`. Two **root** writers
then create and append to leaf files inside it:

1. `install.sh:108` — `exec > >(awk … | tee -a /var/log/maran/install.log)`. This runs as root, on
   every install and every upgrade, and it is the FIRST thing `install.sh` does: before preflight,
   before any gate, before the panel user is even created.
2. `installer/nginx/maran.conf:126-127` — `access_log /var/log/maran/nginx-access.log;` and
   `error_log /var/log/maran/nginx-error.log;`. nginx's **master** process runs as root and opens
   both, on every start, every reload and every `SIGUSR1` log rotation.

The panel uid owns the directory, so it can unlink either name and leave a symbolic link there
without ever having permission to enter the target. Both follows were reproduced under real root on
`ubuntu:24.04` with the distribution's own nginx and `fs.protected_symlinks=1`:

- `install.log` pointed at a root-owned `0600 /root/victim.txt`; root's `tee -a` appended a
  timestamped installer line into it (1 line before, 2 after) and left the link in place.
- `nginx-access.log` pointed at a root-owned `0600 /etc/crontab_victim`; the root nginx master
  appended `127.0.0.1 - - [07/Sep/2026:11:40:15 +0000] "GET /*/2/*/*/*/root/echo_PWNED HTTP/1.1"
  200 3 "-" "curl/8.5.0"` — a line whose **request target and User-Agent the attacker chose**,
  prefixed by nginx's own client address and timestamp.

`fs.protected_symlinks=1` does not help, for the same reason it did not help the release artifact
staging directory: the kernel's protection engages only in a **world-writable, sticky** directory,
and `/var/log/maran` is `0750`. The follower/owner mismatch rule is never consulted.

**The primitive, stated honestly:** append to any file on the host that root can open for append,
with content the attacker partly controls (fully for the request line and headers via nginx,
not at all for nginx's own prefix). It is not an arbitrary-write and it is not a truncate — `tee -a`
and nginx both open `O_APPEND`, and neither creates the target with attacker-chosen mode, because
the target already exists. What that is worth to an attacker is nevertheless root code execution on
most hosts: append-only to a root-owned file is enough for `/etc/cron.d/*` (which parses
line-by-line and ignores lines it cannot understand, so nginx's prefix does not defeat it if the
attacker can get a newline-led valid line in), for `/root/.ssh/authorized_keys`, for
`/etc/ld.so.conf`, and for any shell profile root sources. **NOT MEASURED by this note:** an
end-to-end escalation through any one of those files. What was measured is the append itself into a
root-owned `0600` file.

### What stops it today

**Nothing.** There is no `remove_unless_root_only_directory`, no `assert_root_only_directory`, no
`O_NOFOLLOW`, and no ancestor check on either leaf. This is the fourth instance of the same class
found in this repository today (release artifact staging, the database dump scratch, the SFTP jail
base) and the first that cannot be fixed by relocating to a root-only sibling, because the
directory is documented as shared between the panel and root.

### A premise this note corrects before the change is designed

The sharing is **intent, not fact**. `backend/src/Maran.Host/Extensions/ObservabilityExtensions.cs`
configures Serilog with `.WriteTo.Console(...)` and nothing else; `appsettings.json` and
`appsettings.Development.json` set levels only. `Serilog.Sinks.File` is present in the dependency
graph solely as a transitive dependency of `Serilog.AspNetCore`. No C# and no Rust source in this
repository names `/var/log/maran` at all. The claim that the API writes there survives only as
prose in `maran-api.service`'s `ReadWritePaths=` comment and in `maran-agent.service:188`'s
explanation of why the agent does not copy it, and as a line in the design spec
(`docs/…/2026-08-29-maran-design.md:245`: "journald + `/var/log/maran/`").

That does not make the panel's write access removable outright: `ReadFrom.Configuration` means an
operator can enable a Serilog **file** sink from `/etc/maran/panel.env` at any time without a
rebuild, and the spec says that is intended. So the panel needs a place it can write — it does not
need to own the directory root writes into.

### The proposed fix, and what it will and will not close

**Split by ownership inside one tree, not into two trees.** `/var/log/maran` becomes `root:panel
0750` — root-owned, still group-readable by the panel so an operator and the API can read the
install log and the vhost logs — and the panel gets `/var/log/maran/panel`, `panel:panel 0750`, as
the only place under that tree it can create entries. `maran-api.service`'s `ReadWritePaths=` and
`NoExecPaths=` name the subdirectory instead of the parent. `install.sh`'s `setup_logging` creates
the parent as `root:root 0750` (it runs before the `panel` group exists) and refuses to proceed if
what it finds there is a symlink or is owned by anyone but root; step 40 then re-asserts
`root`-owned `0750` after setting the group.

What it closes: the panel uid can no longer create, rename or unlink an entry in the directory that
holds `install.log`, `nginx-access.log` and `nginx-error.log`, so there is no name at which a
symlink can be planted for root or the root nginx master to follow. Both measured attacks become
`EACCES` at the plant.

What it does **not** close, stated plainly:

1. **Anything already planted on an existing server.** An upgrade inherits the old directory and
   whatever is in it. The fix must neutralise a planted link at the three trusted names without
   deleting an operator's logs, and it must not `rm -rf` the directory — that is customer- and
   operator-visible data, and the same reasoning already changed the SFTP fix above.
2. **The panel's own subdirectory.** Root does not write there, and no root-trusted leaf has it as
   an ancestor, so the class does not apply — but any future code that makes root write into
   `/var/log/maran/panel` reopens exactly this defect one level down.
3. **nginx's own log recreation.** nginx (re)creates its log files itself, as root, at a fixed
   path; there is no gate to place in front of that open. The defence is entirely the parent
   directory's ownership. If a future change moves the vhost logs into a directory any non-root uid
   owns, no assertion in `nginx.conf` can catch it.
4. **`logrotate`, if an operator adds a policy.** A rotation config that `create`s under a
   non-root-owned directory would be the same defect again. None ships today.
5. **The group-readability of the install log.** Unchanged by this fix and already noted in
   `90-finish.sh`: the setup token is deliberately kept out of that file for exactly this reason.
6. **The window between checking and using.** The parent's ownership is checked at install time and
   at step 40; neither is a statement about noon tomorrow. What makes it hold is that only root can
   create an entry in a `root:panel 0750` directory under a `root:root 0755 /var/log`, which is an
   argument about permissions, not a continuous check.

### What a reviewer must check — the requirement is **OUTSTANDING**

No second reviewer can be obtained by this session. Per `rules/security.md` this change is
therefore **NOT compliant**, may live on a branch, and **MUST NOT merge to `main`** until reviewed.
Named for the reviewer, in advance:

1. **Is `root:panel 0750` the right parent?** It gives every uid in the `panel` group read and
   traverse on the install log and the vhost logs. That is what the directory already granted; the
   change does not widen it, but a reviewer may decide the install log should be `root:root 0700`
   and the panel should read nothing.
2. **Existing servers.** The migration must be judged: does moving a planted symlink aside (rather
   than deleting it) leave the operator with a clear signal, and is `chown`ing pre-existing regular
   files at the trusted names to root the right call?
3. **SELinux on AlmaLinux.** `/var/log/maran` inherits `var_log_t`; a new subdirectory should
   inherit it too, but this is NOT measured under enforcing SELinux, and neither is nginx's ability
   to open its logs after the ownership change on a real enforcing host.
4. **No real host, no real systemd.** Everything here is measured in containers. `ReadWritePaths=`
   naming a subdirectory that must exist before the unit starts is a new ordering dependency on
   step 40 that a container cannot exercise.
5. **The operator experience.** `grep -r /var/log/maran` still finds everything, but a two-owner
   tree is a thing an operator must now understand. A reviewer should decide whether the finish
   step should say so.
6. **What the author could not verify at all:** whether any deployment in the field has already
   been exploited through this path. The migration is designed to preserve, not destroy, the
   evidence.

### Where the implementation differed from the plan above (recorded, not edited in)

Written by a later session, after the implementation settled. The paragraphs above are the
pre-change note and are left exactly as they were; this reconciles them with what shipped.

1. **`setup_logging` does not refuse a non-root owner — it takes the directory back and says so.**
   The plan above said it "refuses to proceed if what it finds there is a symlink or is owned by
   anyone but root". What shipped (`installer/install.sh`, `harden_log_directory`) refuses only the
   **symlink-at-the-directory** case; a directory owned by another uid is `chown`ed to root, a
   `NOTE:` is buffered, and the install continues. The reason is upgrades: every server installed
   before this change has a `panel`-owned `/var/log/maran`, and a refusal would make the fix
   unreachable by exactly the hosts that need it — the operator would be told to fix by hand the
   one thing the installer is there to fix. **A reviewer should weigh this deliberately:** the
   alternative reading is that a wrong owner means the host may already be compromised and should
   stop. The compromise chosen is that it does not stop, but it is loud, and it is loud *in the
   install log* — the warnings are replayed after the `tee` redirect precisely so the message
   outlives the terminal.
2. **A planted symlink at a trusted name is moved aside, never deleted.**
   `<name>.planted-symlink.<UTC timestamp>`, with the link's target reported in the warning. No
   root process opens that name. Point 1 of "what it does not close" asked for this; it shipped.
3. **A pre-existing regular file at a trusted name** is `chown`ed to root and has `g-w,o-w`
   removed, contents untouched. Reviewer check 2 above is the open question about this.
4. **The trusted names are one list in one place.** `MARAN_ROOT_TRUSTED_LOG_NAMES` in `install.sh`
   holds `install.log nginx-access.log nginx-error.log`; the polygon reads that assignment rather
   than repeating it, and fails if it reads an empty answer.
5. **The polygon now observes the ownership boundary, and states the half it cannot observe.**
   `docker/polygon/assert-installer-steps.sh` asserts the layout (`/var/log/maran` `root:panel`
   `0750`, `/var/log/maran/panel` `panel:panel` `0750`), walks every ancestor of every trusted leaf,
   and carries an inverse control that puts the original defect back on the real path — `chown
   panel:panel /var/log/maran`, verified landed with `stat` — requires all three leaves refused by
   name, restores it and requires all three accepted. Both the layout assertion and the control
   print an `UNOBSERVED HERE` block: **the nginx half is not gated and cannot be**, because the
   root nginx master re-opens both files after every check the installer could run. Point 3 of
   "what it does not close" is therefore now stated by the check itself and not only here.
6. **Still not verified, adding to the reviewer list above:** no booted host, no enforcing SELinux,
   no real nginx reload observed against the new ownership, and no upgrade run from a real
   pre-change server. The `harden_log_directory` paths were reasoned and read, not executed on a
   host that had the defect. **The second-reviewer requirement remains OUTSTANDING** for this
   section and for every section of this note.

---

## Section: the backup root's preflight warning, the uninstaller's message, and their gate (Task 15)

**Written BEFORE the change**, which is the only order rules/security.md accepts: a note written
afterwards is a justification for a choice already made, and this repository has produced that
shape often enough to name it. Nothing described below existed in the tree when this section was
written; the "what changed" list is what the change is *about* to do.

**Second reviewer: OUTSTANDING.** This session cannot obtain one. The change may live on `dev` and
MUST NOT merge to `main` while this line stands.

### The surfaces touched, and the privilege each carries

1. `installer/lib/10-preflight.sh` — runs as root, before anything on the machine is touched. The
   addition is a **read** (`df -P` on the backup root or on the nearest existing ancestor of it)
   and a printed line. It creates nothing, changes no mode, and its outcome is a **warning**, never
   a refusal: an operator who intends to mount a volume at `/var/backups` after installing must
   still be able to install, and a preflight that refuses them would push them to skip preflight.
2. `installer/uninstall.sh` — runs as root and deletes directories. The addition **deletes
   nothing**; it prints the backup root's path and how many artifacts are under it, and says they
   were kept. The uninstaller already never touched `/var/backups/maran`; what it lacked was any
   statement an operator could act on, and any check that the behaviour is still true tomorrow.
3. `docker/polygon/assert-installer-steps.sh` — build-time gate, not shipped to a server.

### What an attacker could do with this surface

- **Nothing new is created or written.** Neither change opens a file for writing, so neither can be
  aimed at a path an attacker planted. The preflight read follows symlinks the way `df` does; that
  is acceptable because the worst outcome of a lie there is a wrong number in a warning, and the
  gate that decides whether the backup root is safe to WRITE to is `LocalBackupRoot::resolve()` in
  the agent plus step 40's `assert_root_only_directory`, neither of which is touched here.
- **The artifact count is read as root out of a root-only directory** (`/var/backups/maran`,
  `root:root 0700`). It is counted with `find -maxdepth 2 -type f`, which does not follow symlinks
  and prints no file contents — only a number reaches the terminal. No file name is printed,
  because the names are `<account>.<backup id>` and an uninstall transcript is not a place to
  enumerate which customers have backups.
- **The uninstaller's real risk is the inverse one** and it is the reason this section exists at
  all: the plan says an uninstaller that removes a customer's only copy of their data "would be one
  line". Today that line is absent by luck of omission — nothing in the repository would go red if
  someone added `rm -rf /var/backups/maran` to `remove_var_lib`, next to the three sibling
  directories it does delete. The polygon assertion added here is precisely that missing red.

### Why it is safe now

- The preflight addition has no write path and cannot refuse an install.
- The uninstaller addition has no delete path. Its function is asserted, in both directions, by a
  polygon check that plants a real artifact under the real path, runs the uninstaller's real
  deleting functions in the order `main` runs them, and requires the artifact to still be there
  afterwards with its bytes unchanged — and, as its inverse control, runs a copy of the uninstaller
  with a deletion of the backup root planted in it and requires the assertion to REFUSE that copy.
  Without the inverse control the check would pass with its body deleted, since the uninstaller
  does not delete the directory today.

### What the author could not verify (the reviewer must)

1. **No booted host.** Nothing here has run under real systemd, nor on a machine that had ever
   completed a real install. The preflight warning has been observed only inside the polygon image.
2. **`df` on a path whose filesystem is not yet mounted.** The preflight runs before step 40 exists
   the directory, so it measures the nearest existing ancestor and says which path it measured. On
   an operator's intended layout — a volume mounted at `/var/backups` after install — that number
   is about the WRONG filesystem, and the warning says so in words. A reviewer should decide
   whether the honest-but-wrong number is better than no number; the author's judgement is yes,
   because it is labelled.
3. **The count is a count of regular files two levels deep**, which is the shape the agent writes
   (`<root>/<account>/<id>.tar.gz` plus its sidecar). A future layout deeper than that would
   under-count, and nothing would say so.
4. **The polygon proves the uninstaller's shell functions, not an uninstall of a real install.**
   No `maran-api`, no agent, no systemd, and the operator's confirmation prompts are bypassed with
   `--yes` semantics inside the child shell.

## Correction, 2026-09-11 — the service account and group are `maran`, not `panel`

Added by the reviewer-packet pass (`docs/superpowers/notes/2026-09-11-reviewer-packet.md`). Every
`panel:panel`, `root:panel` and "the `panel` user" above names an account no installed host has:
`installer/install.sh:57-58` sets `MARAN_USER=maran` and `MARAN_GROUP=maran`, and
`rules/security.md` item 8 says the same (`root:maran 0640`). The rename and its reasoning are in
`docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md`.

The arguments above survive the rename unchanged — a hosting account is a member of neither group —
but a reviewer checking a table here against a real host would find no such group, which is the
failure mode a stale name causes: it stops the next reader re-deriving the fact.
