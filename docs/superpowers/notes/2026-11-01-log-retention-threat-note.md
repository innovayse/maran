# Threat note: log retention and the runtime log level (issue #131)

`rules/security.md` ("Sensitive change escalation") asks for a threat note when a change touches the
installer's privileged steps. This change adds one: step 41 installs a file into
`/etc/logrotate.d/`, which root's logrotate reads every day. It also adds an administrator setting
that changes what the panel writes to its log. The note is written before the code.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/131-log-retention` MUST NOT merge to `main` until a human second
reviewer has read the change against this note. The owner is told so at hand-off.

## What changes

1. **`/etc/logrotate.d/maran-install`**, root:root 0644, installed by the new
   `installer/lib/41-log-retention.sh` and removed by `installer/uninstall.sh`. One stanza, one
   literal path: `/var/log/maran/install.log`, monthly, twelve kept, compressed, `nocreate`,
   `su root root`. It was the one file under `/var/log/maran` no policy rotated.
2. **`PUT /api/v1/log-level`** (administrators only) changes the panel's minimum log level at
   runtime through a Serilog `LoggingLevelSwitch`, stores the choice in `monitoring.LogLevelSettings`
   and journals `PanelLogLevelChanged` with the level before and after. A hosted service applies the
   stored choice at start-up. `GET /api/v1/log-level` reads the running level and the choices.

## What an attacker could do with it, and why it is safe

### The rotation policy (root, daily)

- **Make root rotate a file it should not.** The stanza names one literal path, no glob, inside
  `/var/log/maran`, which install.sh creates root:<service group> 0750 and re-asserts on every run
  (`harden_log_directory`, which moves a planted link aside). The service account can read and
  traverse that directory and cannot create a name in it, so nothing below root can put a symbolic
  link at `install.log`. logrotate also refuses to rotate through a symbolic link by default.
- **Make root create a file somewhere.** `nocreate`: the policy never creates the file. The next
  installer run's `tee -a` does, as root, in the hardened directory — unchanged from before.
- **Run code as root.** There is no `postrotate`, `prerotate`, `firstaction` or `lastaction` script.
- **Change who the rotation runs as.** `su root root` names it, so a later change of the directory's
  owner cannot make logrotate infer another identity.
- **Lose evidence.** Twelve monthly transcripts are kept. The site and panel nginx logs keep their
  own, unchanged policies (`maran-sites`, `maran-panel`).

### The runtime log level (an administrator setting)

- **A customer changes the level.** The controller is `AdminOnly` at class level; the integration
  test proves 401 for an anonymous caller and 403 for a customer, and that the switch is untouched.
- **An administrator silences the log to hide what they do next.** Possible, and by design an
  administrator controls the panel. It is not silent: the change is journalled in the audit log,
  which is in the database and is not governed by the log level, and the journal entry names who,
  from where, and the level before and after. The audit journal is the record of administrative
  action; the application log is a diagnostic stream.
- **An administrator raises the level to `Verbose` to make secrets appear.** The panel's rule is that
  secrets never reach a log at ANY level (rules/security.md item 8, `SensitiveString`, the redaction
  floor). The per-namespace overrides in `appsettings.json` stay in force, so EF Core's SQL text
  (`Microsoft.EntityFrameworkCore.Database.Command`) stays at Warning whatever the default is, and
  parameter logging is not enabled anywhere. What `Verbose` adds is volume.
- **Fill the disk by choosing `Verbose`.** The panel writes to standard output and the journal, which
  journald bounds (`SystemMaxUse=`). A busy panel at `Verbose` rotates the journal faster and loses
  history sooner; it cannot fill the disk past journald's limit.
- **Inject a value.** The level is an enum. JSON reads it by name (camelCase) and, by default, by
  number; the validator refuses a number that names no member (`LogLevelUnknown`, 400), tested.
- **Spoof the journal's origin.** `IpAddress` and `UserAgent` carry the guard rules/csharp.md
  requires (`[property: JsonIgnore][property: BindNever][BindNever]`) and are stamped by the action.

## What the author could not verify

- An actual rotation over time on a real host: the polygon parses the installed policy with the real
  logrotate (and a broken copy is refused), but it runs no cron or timer.
- That the distribution's logrotate timer or cron job is enabled on every supported family: both
  families ship it enabled in their default server installs; this was not measured here.

## What a reviewer must check

- `installer/logrotate/maran-install` reaches nothing but the one literal path, and has no script.
- `installer/lib/41-log-retention.sh` installs root:root 0644 from the payload and nothing else.
- `PanelLogLevelController` is `AdminOnly`, and `ChangeLogLevelCommand`'s server-established members
  are guarded.
- No logger call anywhere assumed that `Debug`/`Verbose` would never be on in production when deciding
  whether a value was safe to log.
