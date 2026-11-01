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

The level is the panel's default. The narrower settings in configuration stay as they are: the
framework's request logging stays at Warning and the database layer's SQL text stays at Warning, so
`Verbose` does not start writing every query into the journal. No level makes the panel log a
password, a key or a token.

API: `GET /api/v1/log-level` returns `{"level": "information", "choices": [{"level", "displayName"}]}`
and `PUT /api/v1/log-level` takes `{"level": "debug"}`; both are for administrators only. A level that
is not one of the six is refused with 400 `LogLevelUnknown`.

## Not in this change

The server agent's own log level is still fixed when it starts (`RUST_LOG` in its unit). Changing it
at runtime needs a new agent operation and a contract revision, and is left for a later change.
