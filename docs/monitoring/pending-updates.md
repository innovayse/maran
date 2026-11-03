# Pending server updates

How the panel knows which operating system packages have a newer version waiting, where it says
so, and what it cannot see (issue #324).

## Why it matters

A server whose packages fall behind is a server running known defects. The panel does not install
updates, but an administrator should not have to open a terminal to learn that some are waiting.

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

- `GET /api/v1/monitoring/updates?page&pageSize&search&sort` returns a page of
  `{"packageName": "…", "availableVersion": "…", "installedVersion": "…" | null, "source": "…"}`.
  The agent answers its whole list in one call and the panel pages it in memory. `search` matches the
  package name only, literally and ignoring case. `sort` is `packageName` (the default, ascending) or
  `source`, either with a leading `-` for descending. Versions are not sortable: package managers
  compare versions by their own rules and the panel has no implementation of them, so an order it
  produced could disagree with theirs.
- `GET /api/v1/monitoring/updates/count` returns `{"count": 12}`.

## What it cannot see

- Updates the host has not yet learned about. The panel never refreshes metadata, so a package
  published since the host last refreshed is invisible here.
- Which updates are security fixes. The rows carry no such flag; the source is the only hint, and
  naming a repository a security one is a property of how the host's administrator configured it.
- Held, pinned or excluded packages beyond what the package manager itself already applies when it
  answers. Whatever it leaves out, the panel leaves out.
- Whether a pending update is safe to apply, or whether a reboot is needed afterwards.

## Applying updates

The panel never installs, upgrades or removes a package. On the host:

```
apt-get update && apt-get dist-upgrade   # Debian family
dnf upgrade                              # RHEL family
```
