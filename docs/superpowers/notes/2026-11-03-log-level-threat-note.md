# Threat note: the panel log level no longer raises third-party logging (issue #337)

Second reviewer: **OUTSTANDING** (rules/security.md, "Sensitive change escalation"). This note was
written beside the change by its author, so it closes nothing; it is what the reviewer reads instead
of a diff.

## What is changing, and what an attacker could do with it

Issue #131 let an administrator raise the panel's log level at runtime. The level was the logger's
default minimum, so it also applied to every library that logs through the same logger: Npgsql
(command text, parameter values at Debug), Wolverine (message envelopes), the HTTP client (request
lines), EF Core, gRPC. Rules/security.md item 8 says secrets are never in logs; with the level at
Verbose that held only as long as no library put one there.

An attacker who is an administrator, or who holds an administrator's session, could raise the level
and read the journal (or any later reader of the journal could) to obtain what a library logs and the
panel itself never would. A password-reset token travels in a Wolverine envelope; SQL parameters can
carry a hashed credential or an encrypted value's ciphertext.

## The change

`Maran.Host/Logging/ThirdPartyLogPins.cs` is the one list of namespaces the panel does not own, each
with a floor (Warning; `Microsoft.Hosting.Lifetime` Information). A namespace is written at the more
severe of its floor and the panel's level, so raising the panel level never raises a library, and
lowering it to Error silences library warnings as well. Two mechanisms hold it:

1. a `MinimumLevel.Override` per namespace with its own `LoggingLevelSwitch`, moved by
   `SerilogPanelLogLevelSwitch.Apply` together with the panel's switch;
2. a filter applied after configuration is read, because an override from `appsettings` or
   `panel.env` for a longer name (`Npgsql.Command`) beats the shorter one and would otherwise lower
   the pin. The filter picks the longest matching pin by source context and drops the event when it
   is below that pin's switch.

The Development file's `Microsoft.AspNetCore: Information` override was removed: it could no longer
take effect and would have read as if it did.

Tests (`Maran.Host.Tests/Logging/ThirdPartyLogSwitchesTests.cs`): each pinned family refuses Debug
and below at Verbose; the refusing control (the panel's own namespace follows the switch to Verbose);
the level raised after the logger was built moves the panel and not the library; Error silences
library warnings; a longer configured override cannot lower a pin; the Lifetime exception; a name
that only shares letters with a pin is not pinned; an event with no source context is not refused;
a duplicated pin is refused at construction.

## What a reviewer must check

- `ThirdPartyLogPins.All` against `Directory.Packages.props`: every package that logs is covered by
  a prefix. Command to list candidates: `grep -n "PackageVersion" backend/Directory.Packages.props`.
- That `Microsoft.Hosting.Lifetime` at Information carries no request data (it logs listening
  addresses, the environment name and the content root).
- That the filter's source-context match cannot be dodged: a library that logs with a different
  `SourceContext` than its namespace, or with none, is not covered by either mechanism.

## What the author could not verify

- No build or test was run for this change (the owner's one-gate-pass rule); the tests are written,
  not observed green. Serilog's rule that the longest override prefix wins, and that a later
  `Override` for the same name replaces an earlier one, is taken from its documentation.
- Libraries that write straight to the console, or through a logger the host did not build, are not
  reachable by this change.
- Third-party code that logs at Warning or above can still carry a secret; the pin removes the
  verbose levels, not the risk of a careless warning.
