# The panel's logs: where they go, how long they stay, and how much is written

Issue #131.

## Where each log is, and what bounds it

| Log | Written by | Bounded by |
|---|---|---|
| The panel (`maran-api`) | standard output → the systemd journal | journald (`SystemMaxUse=`, by default a tenth of its filesystem, at most 4 GiB) |
| The server agent (`maran-agent`) | standard output → the systemd journal | journald, as above |
| `/var/log/maran/install.log` | every installer run (`tee -a`) | `/etc/logrotate.d/maran-install`: monthly, twelve kept, compressed |
| `/var/log/maran/nginx-access.log`, `nginx-error.log` | nginx, for the panel's own address | `/etc/logrotate.d/maran-panel`: daily, thirty kept |
| `/var/log/maran/sites/<account>/*.log` | nginx, for the customers' sites | `/etc/logrotate.d/maran-sites`: daily, fourteen kept |
| `/var/log/maran/ftps.log` | vsftpd, when FTPS is on | `/etc/logrotate.d/maran-ftps`: weekly, four kept |

Each policy explains its own choices in the file itself. `/var/log/maran/panel` is the panel's
writable place for a file sink an operator configures; no shipped build writes one, so no policy
rotates it. A file sink you add there needs a policy of its own.

Read the panel's log with `journalctl -u maran-api`, the agent's with `journalctl -u maran-agent`.

## Changing how much the panel writes, without a restart

Monitoring → **Panel log level** shows the level the panel's logger is writing at right now and lets
an administrator choose another: Verbose, Debug, Information (the normal level), Warning, Error or
Fatal. The change:

- takes effect for the next line the panel logs — no restart;
- is stored, and applied again every time the panel starts;
- is recorded in the audit log as "Panel log level changed", with who changed it, from where, and the
  level before and after.

Until an administrator chooses, the level is the one in configuration
(`Serilog__MinimumLevel__Default` in `/etc/maran/panel.env`, `Information` when unset). Once a level
has been chosen, the stored choice wins at start-up.

The level is the panel's own. The libraries the panel is built on are held apart from it: the
message bus (Wolverine), the database driver (Npgsql), the HTTP client, ASP.NET Core, Entity
Framework Core and the rest of the platform are written at Warning or above whatever you choose. So
`Verbose` makes the panel's own code talk and does not start writing every query, request line or
message envelope into the journal, and `Error` silences their warnings along with the panel's. The
platform's start-up and shutdown lines (`Microsoft.Hosting.Lifetime`) are the one exception and stay
at Information. A setting in configuration cannot lower these either: the list is fixed in the
panel (`ThirdPartyLogPins`), not an operator setting. No level makes the panel log a password, a key
or a token.

API: `GET /api/v1/log-level` returns `{"level": "information", "choices": [{"level", "displayName"}]}`
and `PUT /api/v1/log-level` takes `{"level": "debug"}`; both are for administrators only. A level that
is not one of the six is refused with 400 `LogLevelUnknown`.

## The agent's audit lines

Issue #106. The root agent writes one line to the journal for every request that changes the
server, so there is a record of what the agent did that does not depend on the panel. The panel
keeps its own audit table in its database; if the panel process and that database are ever not to
be trusted, the agent's lines are the second, separate account. They are written by a different
process, and a process running as the `maran` user cannot edit or delete an entry already in the
journal.

Read them:

```
journalctl -u maran-agent -t maran-agent | grep event=rpc
```

Trust a line only when its journal metadata says `_SYSTEMD_UNIT=maran-agent.service`
(`journalctl -o verbose` shows it). Any process can write text that looks like an audit line;
only the journal stamps the unit.

A line looks like this:

```
event=rpc method=maran.agent.v1.SitesService/CreateSite outcome=ok duration_ms=212 account=acme site=example.com
```

| Field | Meaning |
|---|---|
| `event` | always `rpc` |
| `method` | the agent's own name for the call, service and method |
| `outcome` | `ok`; the agent's error code (for example `INVALID_INPUT`, `NOT_FOUND`; the code name as the agent writes it); `unset` when the call ended before the agent said how, usually because the client left; or `transport:<name>` when the connection layer ended it, for example `transport:PermissionDenied` for a peer the socket policy refused |
| `duration_ms` | how long the call took, in milliseconds |
| `account` | the account the call named, or `-` |
| `site` | the site's domain the call named, or `-` |

`unset` means the agent was asked and did not record a result; it is not a statement that nothing
happened. A call to a long job (a backup, a restore, a PHP install) is one line, written when the
job ends.

The lines never carry a password, key, token, file path, file content or error message. Calls that
only read (lists, status, monitoring) write no line, so the volume is one line per change: a busy
server with many customers writes a few hundred lines a day, and the lines count against the same
journald limits as everything else the agent writes.

What the lines do not prove:

- journald rate-limits each service (`RateLimitIntervalSec`, `RateLimitBurst`) and discards the
  excess, noting only "Suppressed N messages". A burst of changes can push lines out, and the agent
  cannot tell. A missing line is not proof that a call did not happen. If you need a lossless trail,
  raise the limit for `maran-agent.service` and send the journal off the host; Maran does not change
  journald's configuration.
- A request for a path the agent does not serve is not recorded.
- `ok` is the agent's verdict, not a check of the server afterwards.
- Someone with root on the host, or code running inside the agent, can write or remove journal
  entries. The trail is not evidence against a compromised agent or host.
- The journal is readable by root and by members of `systemd-journal` and `adm`. The lines hold
  account names and domains.

## Not in this change

The server agent's own log level is still fixed when it starts (`RUST_LOG` in its unit). Changing it
at runtime needs a new agent operation and a contract revision, and is left for a later change.
