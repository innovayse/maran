# Pending server updates

How the panel knows which operating system packages have a newer version waiting, which of them are
security fixes, whether the host needs a reboot, where it says so, and what it cannot see (issues
#324 and #163).

## Why it matters

A server whose packages fall behind is a server running known defects. The panel does not install
updates, but an administrator should not have to open a terminal to learn that some are waiting,
which of them are security fixes, or that a finished update still needs a reboot.

## What is read

The server agent asks the host's own package manager, through one rpc with no input
(`MonitorService.ListPendingUpdates`, contract revision 17), and reports one row per package that
has a newer version available. The package manager is chosen by the family the agent runs on, and
the command is a fixed argument list with nothing a caller can add to it:

| Family | Command | Exit status |
|---|---|---|
| Debian | `apt-get -s -q -o Debug::NoLocking=true dist-upgrade` | 0 is success; any other status is a failure to read |
| RHEL | `dnf check-update -C -q` | 0 means nothing is pending, 100 means updates are pending, anything else (1 included) is a failure to read |

On the Debian family a second, fixed command runs first: `apt-get indextargets --format '$(FILENAME)'`.
`apt-get -s dist-upgrade` against an empty package-lists directory exits 0 and prints "0 upgraded",
which is indistinguishable from an up-to-date host (recorded on a real container), so an empty answer
from the probe is reported as a catalogue that was never built, an error, and never as "0 updates".
The RHEL family needs no probe: `dnf check-update -C` exits 1 when there is no cache.

`apt-get -s` is a simulation: nothing is installed, removed or configured. `dnf check-update -C` is
cache-only: `-C` forbids a metadata refresh. Each row carries:

| Field | Meaning |
|---|---|
| `packageName` | The operating system's name for the package. |
| `availableVersion` | The version the update would install. |
| `installedVersion` | The version installed now. Absent on the RHEL family, whose update check does not report it; the API sends `null`, never an empty string. |
| `source` | The repository or origin the update comes from. Empty when the package manager names none. |
| `classification` | `security`, `ordinary` or `unmeasured`. See "Security classification". |

## Security classification

Each row says whether the package manager's own metadata marks the update as a security fix. The
panel decides nothing here: it reports what the host's repositories declare, and it reads it from the
local cache only, like the list itself.

- **Debian family.** The simulated upgrade already prints each package's origin list. An update whose
  origin list has a suite ending `-security` is `security`; any other is `ordinary`. No second command
  runs.
- **RHEL family.** The first reading does not say. A second, fixed, cache-only command
  (`dnf check-update --security -C -q`) names the packages the metadata marks as security; those are
  `security` and every other row is `ordinary`.
- **`unmeasured`** is a third answer, not a blank. It is what an older agent reports, and what a row
  carries when the second reading on the RHEL family could not be made (it failed or timed out). The
  list is still returned in that case, because a failed second reading must not take the first one
  down with it, and no row is guessed `ordinary`: "ordinary" is a measured answer.

The API carries the same three words. The list takes an optional `classification` filter
(`security`, `ordinary` or `unmeasured`; anything else is refused) and `security` is one more sort
field. The count endpoint carries `securityCount`, which is `null` when the classification was not
measured and a number only when it was: a missing measurement is never shown as "0 security updates".

## Cache only, and why

The panel reads what the package manager already knows and never makes it refresh. A refresh is an
outbound call from the only root process on the server, and the panel's only phone-home is the
documented licence lease (rules/security.md item 10). The consequence is stated on the screen's own
terms: the list is as fresh as the host's package metadata, and a host that never refreshes it
reports a frozen list. Keeping the metadata current is the host's job, through its own scheduled
refresh, not the panel's.

## A failed read is an error, never zero

A catalogue the agent could not read is a typed failure, not an empty list:

- the RHEL family's cache was never built (`dnf check-update -C` exits 1);
- the Debian family's package lists are unreadable, or an apt line did not match the grammar the
  agent knows (a line it does not recognise fails the whole read rather than being skipped);
- the program exceeded its 60 second deadline or its 4 MiB output cap.

The API answers such a read with the agent's own error (HTTP 500, or 503 when the agent is not
reachable), the Updates page shows the reason, and the navigation badge disappears. "0 updates" is
only ever the answer to a read that succeeded: a zero shown for a catalogue nobody could read would
say "up to date" about something that was never checked.

## Where the panel says so

- **Servers > Updates.** The list of pending packages, searchable by package name and sortable by
  package name or source, paged. Administrators only.
- **Navigation badge.** The Updates item carries the number of pending updates. The interface asks
  for the count every ten minutes and only when the signed-in user is an administrator. A failed read
  draws no badge, and a count of zero draws none either.

### Endpoints

Both are administrators only (a signed-in customer is answered 403, an anonymous caller 401). Both
ask the agent live; the panel stores nothing.

- `GET /api/v1/monitoring/updates?page&pageSize&search&sort&classification` returns a page of
  `{"packageName": "…", "availableVersion": "…", "installedVersion": "…" | null, "source": "…", "classification": "security" | "ordinary" | "unmeasured"}`.
  The agent answers its whole list in one call and the panel pages it in memory. `search` matches the
  package name only, literally and ignoring case. `sort` is `packageName` (the default, ascending) or
  `source` or `security`, either with a leading `-` for descending. `classification` keeps only the rows of
  that one class, on the server. Versions are not sortable: package managers
  compare versions by their own rules and the panel has no implementation of them, so an order it
  produced could disagree with theirs.
- `GET /api/v1/monitoring/updates/count` returns `{"count": 12, "securityCount": 3 | null}`. The
  navigation badge keeps showing `count`, the total.
- `GET /api/v1/monitoring/updates/reboot-required` returns `{"required": true | false | null}`.
  `null` means the probe could not say.

## Reboot required

Replacing a library, the kernel or an init component on disk does not change what a running process
has loaded, so a finished update can leave the host needing a reboot. A second agent operation with no
input (`MonitorService.GetRebootRequirement`, contract revision 19) asks the host:

| Family | Probe | Reading |
|---|---|---|
| Debian | whether the distribution's reboot-required marker file exists | present: required; absent: not required |
| RHEL | `needs-restarting -r` | exit 0 not required, exit 1 required, any other status is a failure |

The Updates page shows a notice only when the answer is `required`. When the probe cannot run (the
program is missing, it timed out, the marker could not be read) the agent answers with an error and the
panel shows nothing: it errs quiet, and never reports "no reboot needed" for a probe that did not run.
The panel never reboots the host. Whether and when to reboot is the administrator's decision.

## Held packages

If the opt-in unattended security updates are enabled at install time
([os-security-updates.md](../installer/os-security-updates.md)), the host's own job skips the database
engines, the web server, the PHP runtimes and the FTP daemon, because an unattended restart of those
drops customers' connections. This changes nothing on this page: the hold applies to the unattended run
only, so those packages are still listed here as pending, classified `security` where the distribution
says so. A held security update showing as pending is the correct state, and the administrator applies
it in a maintenance window.

## What it cannot see

- Updates the host has not yet learned about. The panel never refreshes metadata, so a package
  published since the host last refreshed is invisible here, and so is a security flag the host has
  not yet downloaded.
- On the RHEL family, the classification comes from a second cache-only reading. When that reading
  fails the rows are shown as `unmeasured` and the security count is absent, rather than guessed.
- Whether a pending update is safe to apply. The security flag is the repository's declaration, not a
  judgement about your workload.
- Which running services still use an old library after an update. The reboot notice answers whether
  the host as a whole needs a restart, not which daemon to restart.
- Pins and exclusions the host's administrator set in the package manager's own configuration. Whatever
  the package manager leaves out when it answers, the panel leaves out. The unattended-update hold list
  is not one of these: it is outside the package manager's answer and held packages stay listed.

## Applying updates

The panel never installs, upgrades or removes a package. On the host:

```
apt-get update && apt-get dist-upgrade   # Debian family
dnf upgrade                              # RHEL family
```
