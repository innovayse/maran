# Threat note: `maran issue-panel-certificate`, the console verb for the panel's certificate (issue #426)

`rules/security.md` requires a threat note for a change to the installer's privileged surfaces. This
change adds one verb to the root-only `maran` wrapper (follow-up of #132, whose note is
`2026-11-01-panel-certificate-threat-note.md`). It orders a trusted certificate for the panel's own
configured host name and installs it, or renews it. The note was written after the code of the
branch, not before it, which the rule does not allow; it is recorded as that debt here and not hidden.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it and
signed off. Recorded together with the other outstanding reviews under #331.

## The surfaces this change adds

1. **One verb in the wrapper**: `issue-panel-certificate`, in `installer/cli/lib/panel-certificate.sh`,
   sourced by `installer/cli/maran` and listed in the installer's library list (step 66).
2. **One verb in the panel binary**: `Maran.Host --cli issue-panel-certificate`, run by the wrapper
   as the service account through the existing `cli_dotnet`.
3. **One audit shape**: `SystemAuditEntry.CreateFromConsole`, the panel as actor, `local-console` as
   origin, the operator's name as an unverified claim.

No new daemon, port, outbound call target (the authority is the one #132 already reaches), agent rpc
or database column.

## What an attacker could do with each, and why it is safe

**Who can run it.** Root, by the wrapper's own check, which runs before anything is sourced; the
panel binary refuses to run as root, so the wrapper drops to the service account. A caller who is
root can already read `panel.env` and the keys; the verb grants nothing beyond that.

**What it takes.** Nothing. The verb refuses any argument in the wrapper (exit 1) and again in the
panel (usage, 64), so there is no typed value for a shell or the panel to misread, and the host name
cannot be aimed at another name. The name is the operator's `Panel__HostName`, read by the Ssl
module through the window the endpoint already uses; an unusable name (empty, a placeholder,
`localhost`, malformed) is refused as `PanelHostNameNotConfigured` before anything is ordered.

**What it does.** The same call the administrator's endpoint and the daily sweep make
(`PanelCertificateIssuer`): the authority is asked first, only material it issued reaches the agent,
the agent replaces the pair with nginx's validation and a rollback, the row is written last. A
failure at any step leaves the pair the panel was serving untouched. Running it again renews.

**Output.** One line on standard output: the host name and the expiry. No key, no certificate
material, no authority text; a failure prints only its machine code on standard error.

**Accountability.** The journal row (`CertificateIssued`) is written on success and on refusal. The
operator is `SUDO_USER` as a claim, tidied (control characters dropped, 64 characters) and recorded in
the user-agent column behind `operator-claim:`, as the administrator recovery verb does. The wrapper
also writes the `maran-cli` line to the authentication log through `cli_audit_log`.

**Denial of service.** Each run is one authority order. The authority rate-limits an operator who
loops it; the panel's own ACME pipeline retries with backoff. A root caller looping it is already
able to stop the panel.

## What a reviewer must check

- `installer/cli/lib/panel-certificate.sh` and the wrapper hunk: the verb is matched against the
  fixed case list, and the only argv ever built is `--cli issue-panel-certificate`.
- `Services/PanelCertificateConsoleIssuer.cs`: the host name is read from configuration only, and the
  refusal path journals before it returns.
- The polygon assertion (`docker/polygon/asserts/parts/cli-recovery.sh`, 5a): every argument is
  refused with exit 1, the bare verb is not refused for its arguments.

## What the author could not verify

- The verb against a live panel with a real authority: the polygon image boots no systemd and holds
  no panel, so only the wrapper's argument handling is observed there. The issuance itself is covered
  by unit tests with the authority and the agent faked.
- That `SUDO_USER` survives `runuser` on every supported distribution; the administrator recovery
  verb relies on the same behaviour and records `root` when it does not.
