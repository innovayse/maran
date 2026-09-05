# Security Policy

Maran installs a daemon that runs as root and a panel that provisions system users. A defect
here is not an inconvenience, so a report about one is welcome and will be answered.

## Reporting a vulnerability

Write to **security@innovayse.com**. Do not open a public issue, and do not describe the
problem in a pull request before it is fixed.

We acknowledge within 48 hours. Critical issues target a fix or a documented mitigation within
14 days; less severe ones are scheduled and you are told when. Coordinated disclosure is
honoured, and you are credited by the name you choose unless you ask not to be.

If you would rather encrypt, say so in a first message with no details and we will send a key.

### What to include

Enough for us to reproduce it, and nothing that is not yours to send:

- what an attacker gains — read another tenant's files, escalate to root, bypass a limit
- the smallest sequence that shows it, and which distribution and version you saw it on
- whether it needs an authenticated session, and at which role
- logs if they help, **with tokens, hostnames and customer data removed**

## Supported versions

Until 1.0, only the latest release receives fixes. After 1.0 this section will name the
versions that do.

## Scope

**In scope** — anything in this repository: the panel API, the SPA, the Rust agent and its
socket, the installer, and the templates the agent renders. Reports about the design are in
scope too: if a rule we wrote down is the wrong rule, that is worth more than a bug.

**Out of scope** — findings that require an attacker to already be root on the host,
denial of service by exhausting the machine's own resources, missing hardening headers with no
demonstrated impact, and reports produced only by a scanner with no reasoning attached.

## Testing safely

Test against **your own installation**. Probing somebody else's server running this software is
not research, and no finding excuses it — we will not accept a report obtained that way, and it
may be a crime where you or they are.

A local install is one command; `CONTRIBUTING.md` describes a development stack that runs
without touching a real host at all.

## What we do on our side

- Changes to authentication, sessions and tokens, the agent's privilege dropping, licence
  verification, or the installer's privileged steps require a second reviewer and a written
  threat note in the pull request. `rules/security.md` is the checklist that review uses.
- Dependency versions are pinned and updated deliberately: `backend/Directory.Packages.props`
  for .NET, `agent/Cargo.lock` for Rust, `frontend/package-lock.json` for npm. There is no
  automatic update bot — an update to a panel that runs as root is a change somebody reads.
  `THIRD-PARTY-NOTICES.md` lists every distributed dependency and its licence.
- Secrets never enter the repository. `/etc/maran/panel.env` is `root:panel 0640` on a server,
  and the development values in `.env.example` are throwaways that must never reach one.
