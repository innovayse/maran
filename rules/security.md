# Security Rules

Normative. Security is the product's core promise — every PR is reviewed against this checklist and any "no" blocks merge.

## The checklist (reviewer runs it verbatim)

1. **Input:** every external input (HTTP body/query/route, provisioning API, agent rpc) validated at the boundary — FluentValidator on the C# request, revalidation in the agent. No value reaches a template, path, or SQL untouched.
2. **Paths:** anything path-like goes through canonicalization and the `/home/<account>/` containment check (`resolve_in_home`). Never trust extension, never trust `..`-free-looking strings.
3. **Shell:** no shell strings anywhere, no string-built commands. Agent spawns processes with argv arrays only, allow-listed binaries, no `sh -c`. The API spawns nothing at all.
4. **Config-file injection:** any caller-supplied value written into a line-oriented or
   structured config file — a crontab entry, an nginx directive, an env file, a systemd unit —
   MUST reject newlines, carriage returns and control characters before it is written. This is the
   panel's equivalent of SQL injection: one embedded newline turns a single entry into several,
   which is how a customer escalates from "my own cron job" to "an extra job, a MAILTO, a PATH".
   Rendering through a template does not make it safe; the value is validated, not escaped.
5. **SQL:** EF Core parameterized only; approved `// raw-sql:` comments are parameterized too. String-concatenated SQL is an automatic reject + incident.
6. **AuthZ:** every endpoint declares its permission; every tenant query is account-scoped via the global filters. New tenant entity ⇒ registered in the filter fixture ⇒ IDOR test exists.
7. **Configuration is documented, secrets are not committed:** every variable the product reads has an entry in an `.env.example` (repository root for development, `docker/.env.example` for the dev stack, `installer/panel.env.example` for what the installer generates on a server). The example files carry names, comments and safe placeholders — never real values. `.env` itself is git-ignored.
8. **Secrets:** never in logs, error messages, URLs, or git. This includes anything that *acts* as a secret — a one-time setup token is permission to become the administrator, so it goes to the operator's terminal and never to a log file that outlives the install. Customer-facing errors carry no paths, versions, or tool output (role-aware error mapping). New config values with secrets go to `panel.env` (root:panel 0640), not appsettings.
9. **Crypto:** Argon2id for passwords; Ed25519 for licenses/artifacts; TLS everywhere external; no home-grown crypto, no MD5/SHA1 for anything security-relevant.
10. **Surface:** no new listening ports, no new daemons, no new outbound calls (the only phone-home is the documented license lease). Anything of the kind requires a spec change first, not a PR.
11. **Dependencies:** additions need a one-line justification in the PR; `cargo audit`/NuGet/`npm audit` clean; pinned versions.
12. **Agent:** new rpc = closed typed command, idempotent, re-validated, audited; customer file ops under account UID. Anything resembling "run this for me" is rejected on sight.

## Sensitive change escalation

Changes to auth, session/token handling, the agent's privs module, license verification, or the installer's privileged steps require a second reviewer and an explicit threat note in the PR description: what could an attacker do with this surface, and why is it safe now.

## Disclosure

`SECURITY.md` at repo root carries the report contact and the promise: acknowledge in 48h, fix-or-mitigation target 14 days for critical. Vulnerabilities are never discussed in public issues before a fix ships.
