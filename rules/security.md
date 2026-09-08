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
6. **AuthZ:** every endpoint declares its permission; every tenant query is account-scoped via the
   global filters. This is no longer a thing a reviewer remembers: `TenantScopeTests` walks every
   module's EF model and fails when an entity carrying `AccountId` has no query filter, so a new
   tenant table — ours or a third party's — is covered without anyone extending a list. An entity
   that must NOT be filtered is named in that test's exemption list with the reason it is safe, and
   an exemption for an entity that no longer exists fails too. `IgnoreQueryFilters` is a banned
   symbol in `backend/src`, so each deliberate bypass carries its reason on the line. The answer for
   a resource another account owns is **404, never 403** (`ErrorType.NotFound`) — a 403 confirms the
   row exists — and each tenant module still carries its own IDOR test proving the endpoint answers
   that way.
7. **Configuration is documented, secrets are not committed:** every variable the product reads has an entry in an `.env.example` (repository root for development, `docker/.env.example` for the dev stack, `installer/panel.env.example` for what the installer generates on a server). The example files carry names, comments and safe placeholders — never real values. `.env` itself is git-ignored.
8. **Secrets:** never in logs, error messages, URLs, or git. This includes anything that *acts* as a secret — a one-time setup token is permission to become the administrator, so it goes to the operator's terminal and never to a log file that outlives the install. Customer-facing errors carry no paths, versions, or tool output (role-aware error mapping). New config values with secrets go to `panel.env` (root:panel 0640), not appsettings.
9. **Crypto:** Argon2id for passwords; Ed25519 for licenses/artifacts; TLS everywhere external; no home-grown crypto, no MD5/SHA1 for anything security-relevant.
10. **Surface:** no new listening ports, no new daemons, no new outbound calls (the only phone-home is the documented license lease). Anything of the kind requires a spec change first, not a PR.
11. **Dependencies:** additions need a one-line justification in the PR; `cargo audit`/NuGet/`npm audit` clean; pinned versions.
12. **Agent:** new rpc = closed typed command, idempotent, re-validated, audited; customer file ops under account UID. Anything resembling "run this for me" is rejected on sight.
13. **Module reach into the agent is declared and enforced.** `Maran.Agent.Client` is one door to
    the only root process on the server, shared by every module in the panel process, so a module
    that has no business touching the firewall could nevertheless resolve `IAgentFirewallClient` and
    open a port. Every module's `Manifest` therefore lists the `AgentCapability` values it needs, an
    administrator sees that list before installing, and `AgentCapabilityGuard` refuses to compose a
    module whose declared dependencies reach past it — at startup, and again in CI. A new agent
    service must add a value to `AgentCapability` or the guard throws rather than treating it as the
    one part of the agent nobody has to declare. **Stated limit:** a module that takes
    `IServiceProvider` and asks for a client at runtime declares nothing in its metadata and is not
    caught; service location is already a review reject, and closing it properly means not
    registering the clients in the shared container at all.

## Sensitive change escalation

Changes to auth, session/token handling, the agent's privs module, license verification, or the installer's privileged steps require a second reviewer and an explicit threat note in the PR description: what could an attacker do with this surface, and why is it safe now.

**When no second reviewer can be obtained, the change is NOT compliant — and this rule says so
rather than pretending otherwise.** Most work in this repository is done by agent sessions, which
cannot dispatch a reviewer; several privileged changes have therefore landed in the working tree
with a written threat note and the reviewer requirement recorded as OUTSTANDING. That is a real
gap, not an alternative path, and the rule is not weakened to describe it as one: the second
reviewer exists because the author of a privileged change is the person least able to see what they
assumed, and a threat note written by that same author closes nothing. What it does buy is that the
reviewer, when they arrive, reads an argument instead of a diff.

So the deferral is permitted only in this exact shape, and it is a debt with a name:

- The threat note is written **first**, before the change, and lands in `docs/superpowers/notes/`
  as a file, not as prose in a report that only one session ever reads. Written after the fact it
  is a justification validating a choice already made, which rules/architecture.md names as the
  mechanism that stops the next reader looking.
- The note states the requirement as **OUTSTANDING**, names what a reviewer must check, and names
  what the author could not verify.
- The change **may land on a branch; it MUST NOT merge to `main`.** `main` is protected and the
  second reviewer is a merge gate (rules/git.md "Pull requests"), so an outstanding review means an
  unmergeable branch — which is the honest state and the one the owner can see.
- The owner is told at hand-off which privileged surfaces are carrying an outstanding review.

A note recording the debt is the minimum, never the discharge of it.

## Disclosure

`SECURITY.md` at repo root carries the report contact and the promise: acknowledge in 48h, fix-or-mitigation target 14 days for critical. Vulnerabilities are never discussed in public issues before a fix ships.
