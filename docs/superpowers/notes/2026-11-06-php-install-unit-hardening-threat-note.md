# Threat note: hardening the PHP package-install unit (issue #408)

`rules/security.md` requires a threat note, written first, for a change to the installer's privileged
units. This note amends `2026-10-26-php-package-install-unit-threat-note.md` section 2.2 ("Its own
sandbox"), which allowed directives for this unit only "after each one has been measured on both
families". Written BEFORE the edit; where the unit lands differently, the unit or this note is wrong.

**Second review: OUTSTANDING — must not merge to `main`.** Agent sessions cannot obtain a second
reviewer; a human must read the directive list and the measurements before `main`.

## What changes

`installer/systemd/maran-php-install@.service` gains sandbox directives. The unit still runs the
package manager as root with the same fixed argv; no daemon, port, rpc or outbound call is added.
The unit scored 9.4 (`maran units`, systemd 255) with no hardening at all.

## What an attacker could do, and what this does and does not stop

The unit installs a package named by the agent after the agent checked the PHP version against its
closed set. A malicious or compromised package runs maintainer scripts as root, and root can undo
any sandbox directive: **these directives are not a boundary against a hostile package** and are
not claimed to be. What they bound is an accident or a bug in the package manager or a script: a
script that writes to kernel tunables, loads a module, changes the clock or hostname, opens a raw
socket family, creates namespaces or devices, leaves files in the shared `/tmp`, reads `/home`.
Equally the added directives cannot widen anything: each only removes access.

## Directives, with the measurement behind each

Measured 2026-11-06 on a booted systemd in a container, one fresh container per trial, a package
installed through the unit and the package database checked afterwards, with an inverse control
(`ProtectSystem=strict` must fail, and did, with EROFS):

| family | systemd | package | result |
|---|---|---|---|
| Ubuntu 24.04 | 255 | `php8.3-fpm` (`apt-get install -y --no-remove --`) | baseline, set A, A plus each single directive, and the union all PASS; dpkg audit empty |
| AlmaLinux 9 | 252 | `php-fpm` (AppStream, `dnf install -y --`) | the same eleven trials PASS; `rpm -V` clean |

Adopted: `ProtectKernelTunables`, `ProtectKernelModules`, `ProtectKernelLogs`, `ProtectControlGroups`,
`ProtectHostname`, `ProtectClock`, `RestrictRealtime`, `LockPersonality`, `SystemCallArchitectures=native`,
`RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK` (netlink is needed for name resolution
and for account tools on the RHEL family), `PrivateDevices`, `RestrictNamespaces`, `ProtectHome`,
`PrivateTmp`, a syscall deny-list (`~@mount @swap @reboot @raw-io @module @cpu-emulation @debug @clock`)
with `SystemCallErrorNumber=EPERM`, and a capability deny-list (module, raw I/O, boot, time, pacct,
tty config, wake alarm, suspend, BPF, perfmon, syslog, net raw, net broadcast, lease).

EPERM, not the default kill action, is deliberate: a process killed by SIGSYS leaves no package
manager text, and the agent's failure classifier (`classify_install_failure`) would read an
unclassifiable run instead of a failed install.

Not adopted, with reasons: `ProtectSystem=` (the job writes `/usr`, `/etc`, `/var`), `NoNewPrivileges`
(SELinux/AppArmor domain transitions of rpm scriptlets, not measured on an enforcing host),
`RestrictSUIDSGID` (dpkg and rpm must unpack setuid files), `MemoryDenyWriteExecute` (passed both
trials but a PHP-related maintainer script with a JIT is not covered by the packages measured; the
gain is small), `PrivateNetwork`, `ProtectProc`, `DynamicUser` and `PrivateUsers` (impossible here).

## What the measurement could not establish (OUTSTANDING for the reviewer)

- SELinux was not enforcing in the container; an enforcing AlmaLinux/Rocky host is unobserved.
- One package per family. The real packages (Sury on Debian, Remi on RHEL) and their dependency
  closures, which depend on what the host already has installed, were not run.
- Ubuntu 22.04 (systemd 249), Debian 12 and 13, Alma and Rocky 10 were not run; the deny-lists are
  chosen so a syscall unknown to an older systemd is simply not denied.
- On Ubuntu the container's `policy-rc.d` blocked service starts, so a maintainer script starting
  php-fpm under the new directives was not observed (unpack, configure and enable were).
- `maran units` scores the unit text offline; a unit that fails to start scores the same.
