# Threat note: "Check for updates" from the panel (issue #516)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to session-gated authority over a root process. This note covers the
"Check for updates" button of the Panel updates screen: an administrator's web session asks the server to
fetch the signed release manifest and record what it names. It is a sibling of
`2026-11-07-panel-update-from-panel-threat-note.md` (issue #382) and reuses its mechanism on purpose: a
request marker the panel creates, a root-owned systemd path unit that notices it, a fixed command. It is
written BEFORE the code, so every statement is a requirement the code must meet. Where the code lands and
differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (rules/security.md), the branch must not merge to main.** The work is
carried out by agent sessions, which cannot obtain a second reviewer. This note discharges nothing in the
notes it builds on, and they carry their own OUTSTANDING reviews.

**Reading of rules/security.md item 10: NOT RECORDED.** The one documented exception is "the root `maran`
CLI may fetch a signed release manifest and its artifacts over HTTPS when an operator runs `maran update`
or `maran update --check`". A press of the button makes the root CLI run `maran update --check` on an
administrator's say-so, with no terminal. This note reads that as the same exception (operator-triggered,
root CLI, never the API or the agent, no identifier sent, `--offline-tarball` remains the call-free path)
and the API itself makes NO outbound call. A timer-driven check is explicitly NOT covered by the
exception and this change adds none: a check happens only when a person presses the button. The reading
is the owner's to confirm; until then it is an assumption, not a fact.

## What is added

1. **A second marker in the existing request directory:** `/run/maran-update-request/check-requested`.
   The directory is the one #382 built (`root:<service group> 0770` by `systemd-tmpfiles`, on a tmpfs, so
   a marker never survives a reboot and never starts a check at boot). The API creates the marker with
   `FileMode.CreateNew`; its content is the fixed token `check` and root never reads it. It carries no
   channel, address, path or flag, and the API holds none of those to send.
2. **Two root units,** `maran-update-check.path` and `maran-update-check.service`, installed
   `root:root 0644` by the same step, `installer/lib/69-update-request.sh`. The path unit is
   `PathExists=` on exactly the check marker and starts the check service. The service is a oneshot run
   as root with ONE fixed command, `/usr/local/sbin/maran update --check`, and ONE `ExecStopPost`,
   `rm -f -- <the check marker>`. There is no `ExecStartPre`, no `EnvironmentFile`, no shell, no template
   argument. The verb is the operator's existing `--check`: it takes the update lock, verifies the signed
   manifest, records the last check in `update-check.state` for the panel's own channel, prints a
   sentence and ends. **It installs nothing**: `--check` ends at the version decision, before any
   artifact is downloaded. No change to the `maran` CLI was needed.
3. **The marker is removed when the check ends, not when it starts.** In #382 the marker is consumed first
   because the update publishes its own running record. A check publishes none, so its marker is the
   "checking" signal the screen shows: it exists from the request until `ExecStopPost` removes it, on
   success, on failure and on the timeout alike. Removing it at the start would leave a window in which
   the check runs and nothing says so, and the button would be offered a second time. The unit's
   `TimeoutStartSec=4min` is pinned below the five minutes (`PanelUpdateLedgerPolicy.MarkerGrace`) after
   which the panel stops believing a marker, so a marker older than that is one the unit never took.
4. **One endpoint,** `POST /api/v1/panel-update/check`, administrators only, no body, 202 with no body.
   `GET /api/v1/panel-update` gains `checkInProgress`; the result is the existing `lastCheckedAt` and
   `available`. No task is opened: a check changes nothing on the server.
5. **A named rate-limit policy** `update-check` (`RateLimitPolicies.UpdateCheck`), a fixed window per
   signed-in administrator, 10 per hour by default (`RateLimiting__UpdateCheckMaxRequests`,
   `RateLimiting__UpdateCheckWindowSeconds`).
6. **One audit action,** `PanelUpdateCheckRequested`, subject `request=check`, written BEFORE the marker is
   created; a refusal and a marker that could not be created are journalled as failed entries.

## What an attacker could do with each surface, and why it is safe

### 1. A hijacked administrator session

An attacker holding an administrator's access token can press the button. What they get is **a fetch of
the signed manifest and a refreshed "last checked" fact on the screen**. They cannot choose a channel, a
URL, a version or a path, and no artifact is downloaded or installed. The cost to the server is one
outbound HTTPS request per accepted press, bounded by:

- **Administrator policy** (`AuthorizationPolicies.AdminOnly`) on the controller class. A customer, a
  reseller and an API key are refused (403, the documented exception to the 404 rule because there is no
  identifier to hide); an anonymous caller is 401. The SPA hiding the button is advice only.
- **CSRF:** the panel-wide `X-Maran-Request` check on every cookie-bearing state change. No new mechanism.
- **Rate limit:** the `update-check` bucket, 10 per hour per administrator, a 429 with `Retry-After` beyond
  it. It slows a stolen session and does not stop it. Behind it, one marker means one check at a time
  (`CreateNew`), and the update lock makes the root side refuse a check that meets a running update.
- **Audit:** one entry per request, before the marker. A started check is never an unaudited one.
- **Concurrent requests:** `PanelUpdateCheckRunning` (409) while the marker is younger than the grace
  period; `PanelUpdateRunning` (409) while an update is queued or running, so a check never starts over an
  update. Both are advice that spares a marker; the exclusive creation and the root lock are the controls.
- **Step-up re-authentication: NOT PRESENT,** as in #382, and for the same stated reason. The residue is
  smaller here: the action installs nothing and changes no release.

### 2. A compromised panel process (the service account) aims at root

The panel account can write the request directory. What it can do to root through this change:

- **Create the check marker at will.** That starts `maran update --check`, which does what an
  administrator's press does. Repeated creation is bounded by the service's start limit (three starts in a
  minute) and by the root lock. A process that can already stop the panel and read its secrets gains a
  trigger for one HTTPS fetch of a public, signed manifest.
- **Make the marker a link, a directory, a FIFO or a huge file.** Root only tests its existence and
  `rm -f --`s it: `rm` unlinks a link without following it and never opens the file, so a link to
  `/etc/shadow` is removed, not read or truncated. A directory named `check-requested` makes the `rm`
  fail, the service fails, and the start limit stops the retrigger loop; checks from the panel then stay
  off until an operator looks (a self-inflicted denial by an account that can already stop the panel).
  Updates are unaffected: they have their own units and marker.
- **Make the check marker start the UPDATE.** It cannot: the two path units name different markers and
  different services, and the polygon assertion pins each pair (`PathExists=`, `Unit=`) and refuses a
  check path unit that points at the update service or at the update marker.
- **Replace the request directory with a link.** It cannot: the directory is root-owned inside root-owned
  `/run`, and the account has write on its contents only.
- **Forge what the screen shows.** It cannot write `/etc/maran`, so `update-check.state` is root's word.
  The API treats the file as untrusted text exactly as in #382 (strict parsing, version grammar, plain
  https notes address, a value that fails is absent, a file over 4 KiB is refused whole).

The service sandbox is the update service's, narrowed: `NoNewPrivileges`, `ProtectSystem=strict`,
`ProtectHome`, `PrivateTmp`, the kernel and clock protections, `RestrictAddressFamilies` for IP, unix and
netlink. The writable set is `/usr/local/maran /etc/maran /var/lib/maran-cli-staging
/run/maran-update-request`: no dump directory (a check writes none), and the request directory instead of
all of `/run`. `/var/lib/maran`, which the panel's account owns, is deliberately NOT in it.

### 3. A network attacker, or a compromised release host

Unchanged: the root CLI's checks (signature before fields, channel, version grammar, TLS with no override)
are the ones in `2026-11-04-panel-self-update-threat-note.md`. The button adds an on-demand trigger for a
manifest fetch that was already operator-triggerable, and nothing is installed from it. The release text
reaches the screen exactly as in #382: re-validated by the CLI, again by the API, linked only when it is a
plain https address. A freeze or withhold of the manifest is unchanged and accepted. **A failed check is
not reported as a result:** the screen shows the last good check and says the new one did not record
anything, with no server text, path or tool output (the CLI's own message goes to the unit's journal).

### 4. What happens when the check cannot run

Unit missing (the installer step did not run), path unit disabled, or the release host unreachable: the
marker exists until `ExecStopPost` removes it, or it is older than five minutes. The handler withdraws a
marker older than the grace period before creating a new one, so a lost request never blocks the button
for good. Without the request directory the endpoint answers `PanelUpdateUnavailable` (503) and says
nothing about a path.

What this does NOT defend against, stated plainly:

- **No step-up** (above). A hijacked administrator session can cause manifest fetches, at 10 an hour.
- **Availability:** the service account can make the request directory unusable for itself and can start
  checks; it can already stop the panel.
- **A compromised signing key or release host** is fatal for every update path; this adds nothing to it.
- **Metadata:** each fetch tells the release host the server's address and that a check happened. That is
  what an operator's `maran update --check` already does.

## What a reviewer must check

1. `installer/systemd/maran-update-check.service`: one `ExecStart` (`maran update --check`), no
   `ExecStartPre`, one `ExecStopPost` (the `rm`), no shell, no `EnvironmentFile`, `TimeoutStartSec=4min`,
   no `[Install]`, and the writable set above. The polygon assertion
   `docker/polygon/asserts/parts/update-request-units.sh` holds this as code with 30 planted breaks for the
   service, 8 for the path unit, and accepting controls for both (78 planted and 6 accepting in all, with
   the update request's own).
2. `maran-update-check.path`: `PathExists=` on exactly `check-requested`, `Unit=` the check service.
3. That `maran update --check` still ends before any download and still records `update-check.state` only
   for the recorded channel (`cli_update`).
4. That the API never reads the marker's content, returns no path, and refuses a check while an update is
   in flight.
5. The `update-check` policy and its numbers (10 per hour), and that the endpoint opts into it at the
   action and not only the class.
6. Item 10: that the owner reads it as the same exception.

## What the author could not verify

- A run of the units under a real systemd on both families: the polygon image boots no systemd, so "the
  path unit starts the service", "`ExecStopPost` runs after a failed or timed-out check" and "the sandbox
  leaves `--check` what it needs" (the stale-release cleanup in `/usr/local/maran`, the lock and work copy
  in `/var/lib/maran-cli-staging`, SELinux on AlmaLinux) are read from the files' text and from the verb's
  libraries, not observed.
- That systemd re-evaluates `PathExists=` only after `ExecStopPost` has removed the marker (so a finished
  check does not retrigger itself). It is the documented ordering of a oneshot's stop sequence; it was not
  observed on a booted host.
- The `systemd-analyze security` score of the new service: it carries the update service's directives, so
  its ceiling is recorded at the same 5.7, but the figure was not measured by `maran units` in this change.

## Second review: OUTSTANDING (#331)

A reviewer must read items 1 to 6 above against the code, re-derive "why it is safe" for sections 1 and 2
rather than accept them, and decide whether the check marker should share the update's request directory or
have a directory of its own.
