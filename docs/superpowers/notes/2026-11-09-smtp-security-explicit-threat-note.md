# Threat note: the SMTP protection mode is a deliberate choice (issue #491)

## What was wrong

`SmtpSecurity.None` was the enum's value 0. A save request that omitted `security` deserializes to
`default(SmtpSecurity)`, which is 0, so it selected the cleartext mode: the panel then sent mail, and
the submission credential with it, unprotected. The same held for any stored row or code path that
produced a default value. The security pre-review found it.

## What changed

- `SmtpSecurity` has no member with the value 0. `StartTls = 1` and `ImplicitTls = 2` keep the numbers
  they always had; `None` is now 3 and has to be named. `default(SmtpSecurity)` names nothing.
- `SaveSmtpSettingsCommandValidator` already required `IsInEnum()`, which now refuses an omitted
  field (0) and `99` alike with `SmtpSecurityInvalid` (en, ru, hy). A new settings screen still starts
  on STARTTLS (`GetSmtpSettingsQueryHandler`).
- `SmtpMailer.ToSocketOptions` reads any value no mode names as STARTTLS, never as cleartext (the
  default arm was already the stricter encrypted mode; it is now also the reading of a stored 0).
- Migration `20261109120000_MoveCleartextSmtpSecurityOffZero` rewrites rows holding 0 to 3, so a panel
  that sent mail in the clear on purpose (a localhost relay) keeps doing exactly that, and nothing
  else changes. `Down` restores 0.

## Why it is safe now

An omission can no longer select cleartext at any layer: the request is refused, a stored default is
read as STARTTLS, and the only way to cleartext is the named value. Tests plant the omission and require
the refusal, require the three named modes to be accepted (inverse control), pin the numbers, and send a
stored 0 to a fake server to prove nothing is delivered in the clear.

## Cost and not covered

- A client that sent the numeric `0` for cleartext now gets a 400. The OpenAPI enum is a string list;
  its order changed (`startTls`, `implicitTls`, `none`), which is additive.
- Rolling the application back to the previous release without running `Down` makes it read 3 as an
  unrecognised mode and fall back to STARTTLS, so a localhost relay that takes no TLS stops receiving
  mail until the setting is saved again. It fails closed. `maran migrate guard` does not match an
  `UPDATE ... SET` (its stated blind spot), so the rewrite is not gated mechanically.
- The mode remains an operator's choice: nothing stops an administrator naming `none` for a remote
  relay.

Second review: OUTSTANDING (#331).
