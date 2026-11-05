# Unattended OS security updates (opt-in)

What the installer's `--os-security-updates` flag does, what it writes on each operating system
family, which packages it holds back and why, and how an administrator applies those by hand
(issue #163).

The second review of this privileged installer step is OUTSTANDING (rules/security.md "Sensitive
change escalation"); the threat note is
`docs/superpowers/notes/2026-11-04-os-security-updates-threat-note.md`.

## What it is, and what it is not

With the flag, the installer switches on the operating system's own unattended-upgrade mechanism,
restricted to security updates, and tells it which packages to leave alone. The updates are applied by
the distribution's own scheduled job, not by Maran. Maran adds no daemon, no timer or unit of its own,
no listening port and no outbound call: the job contacts the package mirrors the host already uses for
every manual `apt-get` or `dnf` run, and package signature checking is not touched.

Without the flag the installer changes nothing here and prints
`skipped (opt-in: --os-security-updates)`. The flag is off by default: a root job that changes
software on a customer's server is the operator's decision, not the installer's.

```
sudo ./install.sh --os-security-updates
```

The installer exports the choice as `MARAN_OS_SECURITY_UPDATES` (`yes` or `no`, default `no`). The step
is `installer/lib/68-os-updates.sh`.

## What it writes

| Family | Package installed | File written (root:root, 0644) | Timer enabled |
|---|---|---|---|
| Debian family (Ubuntu 22.04 and 24.04, Debian 12 and 13) | `unattended-upgrades` | `/etc/apt/apt.conf.d/52maran-os-security-updates` | `apt-daily.timer`, `apt-daily-upgrade.timer` |
| RHEL family (AlmaLinux and Rocky 9 and 10) | `dnf-automatic` and `yum-utils` (ships `needs-restarting`; `dnf-utils` is only a provide of it) | `/etc/dnf/automatic.conf` | `dnf-automatic-install.timer` |

Both files are written the same way: rendered in full, written to a temporary file in the same
directory, owner and mode set, synced, then renamed over the target, so an interrupted run leaves the
previous file intact. A file that already holds exactly the rendered policy is left untouched, so a
second run converges. Every rendered file carries a marker comment (`Maran-managed by
installer/lib/68-os-updates.sh`). The timers are the distribution's own; the step only enables them
and then checks with `systemctl is-enabled` that they are.

**Debian family.** The file clears the distribution's default origin lists
(`#clear Unattended-Upgrade::Allowed-Origins;` and `#clear Unattended-Upgrade::Origins-Pattern;`) and
then allows exactly one origin, the security archive of this release (`origin=Ubuntu,archive=<codename>-security`
on Ubuntu, `origin=Debian,codename=<codename>-security` on Debian), so only the security suite is
applied. It sets `Unattended-Upgrade::Package-Blacklist` to the hold list, each pattern as an anchored
regular expression (`php*` becomes `^php.*`, a name without `*` becomes `^name$`),
`Unattended-Upgrade::Automatic-Reboot "false";`, and
`APT::Periodic::Update-Package-Lists "1";` and `APT::Periodic::Unattended-Upgrade "1";`. The distribution
id and codename are read from the host's `/etc/os-release`; the codename is written only after it
passes a strict shape check (lowercase letters and digits), and any other distribution id is refused.
No value from the command line reaches the file.

**RHEL family.** `dnf-automatic` reads `/etc/dnf/automatic.conf` as one file and has no drop-in
directory, so the step writes that file whole. It sets `upgrade_type = security`,
`apply_updates = yes`, `random_sleep = 3600` (spreads load across hosts; it is not a control),
`emit_via = stdio` under `[emitters]` and, under `[base]`, `exclude=` the hold list (space separated
globs). It sets no `reboot` key. A file that was not written by Maran is replaced: its previous bytes are first kept beside it as
`<file>.maran-previous` (root-owned, 0600; if that name exists already, a UTC timestamp is appended so
the first copy survives) and the step prints that it did. A file that carries Maran's marker is simply
rewritten. The same rule applies to the Debian drop-in.

## The hold list, and why

Held from the unattended run only:

| Group | Pattern |
|---|---|
| Database engines | `mariadb*`, `mysql*`, `postgresql*` |
| Web server | `nginx*` |
| PHP runtimes | `php*` |
| FTP daemon | `vsftpd*` |

The list lives in one place, `MARAN_OS_UPDATE_HOLD` in `installer/lib/00-common.sh`, and is rendered in
each family's native syntax. The reason is timing, not security: an unattended upgrade of these
restarts the service at an hour nobody chose, which drops every customer's connections (an interrupted
database transaction, reset web connections, ended FTP transfers) and can change a PHP version a site is
bound to. Those are events an administrator schedules.

**The hold list is a policy, not protection.** A held package with a known vulnerability stays
vulnerable until somebody applies the update. The list shortens one risk (an uninvited restart) by
lengthening another (a delay in patching these packages), and that trade is the administrator's.
Patterns are deliberately wide (`php*` also matches a package that merely starts with `php`): that errs
toward holding too much and never toward upgrading something that should be held.

Held packages are not invisible. The hold is written into the unattended job's own configuration, not
into `apt-mark hold`, `dnf versionlock` or the package manager's main configuration, so a manual
`apt-get upgrade` or `dnf upgrade` still applies them, and **Servers > Updates** still lists them as
pending, classified `security` where the distribution says so
([pending-updates.md](../monitoring/pending-updates.md)).

A security fix to a shared library that a held daemon uses is not held (that is the point of security
updates). The daemon keeps running the old copy until it restarts, which is why the panel also shows
reboot-required.

## Applying held packages

When the panel lists a held package as pending, apply it in a maintenance window, on the host:

```
apt-get update && apt-get dist-upgrade   # Debian family
dnf upgrade                              # RHEL family
```

To apply one held package and restart its service deliberately, name it
(`apt-get install --only-upgrade <package>` or `dnf upgrade <package>`), then restart the service.

## Changing the hold list

Edit `MARAN_OS_UPDATE_HOLD` in `installer/lib/00-common.sh` (a space-separated list of patterns) and
re-run the installer with the flag; the step renders the file again and converges. Each pattern must be
lowercase letters, digits and hyphens with at most one trailing `*`, and the list may not be empty: the
step refuses either rather than write a policy it cannot stand behind. Editing the written file by
hand works until the next run of the step, which overwrites it.

## Reboots are never automatic

The Debian file sets `Automatic-Reboot "false"` explicitly, so a later change of the package default
cannot turn a reboot on. The RHEL file sets no `reboot` key and relies on the package default of no
reboot; the step prints that it did not set it. The panel shows a reboot-required notice when the host
says one is needed (the Debian marker file, or `needs-restarting -r` on the RHEL family) and the
decision, and the timing, stay with the administrator. A probe that cannot run shows nothing; it is
never shown as "no reboot needed".

## Uninstalling

`uninstall.sh` removes the file Maran wrote (the Debian drop-in, or `/etc/dnf/automatic.conf`), and only
when it carries Maran's marker line: a file at the same path without it is the operator's and is left in
place, with a note. If a `.maran-previous` copy exists it is named in the output and is not put back
automatically. It does not remove `unattended-upgrades` or `dnf-automatic` and does not disable the
distribution's timers: the package and the timers are the customer's system after the install, and
silently stopping security updates would weaken a server Maran no longer manages. It prints that, and
that without the file the job runs with the package's own settings. It does not restore a package
default for `/etc/dnf/automatic.conf`, and what `dnf-automatic` does with no file was not observed.

## Blind spots

- A real security run was not observed on either family: the test containers have no reachable
  security channel with pending updates. The step prints `UNOBSERVED HERE: a real security run`. What is
  checked is that the file is written with owner root:root and mode 0644, that on the Debian family
  `apt-config dump` (apt's own reading across every file in `apt.conf.d`) shows exactly the one security
  origin, an empty `Allowed-Origins`, every held pattern, `Automatic-Reboot "false"` and both periodic
  switches (so a later file that adds an origin back fails the step), that on the RHEL family the
  re-read file is identical to the render, and that the timers are enabled. That the job then applies only security
  updates and skips the hold list is taken from the package managers' documentation.
- `unattended-upgrade --dry-run` is not run (it contacts the mirrors), and `dnf check-update` is not a
  validator of `automatic.conf`.
- If the security suite is spelled differently on a release, the Debian job applies nothing (the
  filter matches no origin) and the panel classifies everything `ordinary`. That fails safe and
  silently; each supported release has to be checked.
- SELinux enforcing on the RHEL family, `needs-restarting` and its exit codes on the tenth releases, and
  services that restart themselves after a library update (`needrestart`-style tools) were not
  exercised.
- When the job and a Maran package operation (a PHP install) meet, the package manager's own lock
  serialises them; the loser is told to try again. This was read from the code, not run side by side.
