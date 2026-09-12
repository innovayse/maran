# Threat note — the one-time setup token travels inside a URL

Date: 2026-09-11
Surface: `installer/lib/90-finish.sh` (`step_finish`, the printed setup link),
`installer/lib/60-config.sh` (`generate_setup_token`, `Setup__Token=` in `panel.env`),
`installer/nginx/maran.conf` (the vhost that serves `/setup` and logs the request),
`frontend/src/pages/auth/SetupPage.vue` (reads `?token=` out of the query string), and
`backend/src/Maran.Modules/Identity/Commands/CompleteSetup/CompleteSetupCommandHandler.cs`
(what the token buys and when it stops working).

This is token handling and an installer privileged step, so `rules/security.md`
("Sensitive change escalation") applies twice.

## THIS NOTE IS LATE, AND THAT IS THE FIRST THING TO KNOW ABOUT IT

`rules/security.md` requires the threat note **before** the change, and gives the reason: an
argument written afterwards validates a choice already made, and cannot contradict it. This note
does not meet that bar.

The delivery has been a URL since before this branch. It was flagged as uncovered on 2026-09-09 and
again on 2026-09-11 (`docs/superpowers/notes/2026-09-11-reviewer-packet.md` §2.1); the argument for
it existed only as a shell comment at `90-finish.sh:36-39`, which is a comment a reader has to find
rather than a note a reviewer is handed. **Everything below is reconstruction**: it was derived by
reading the shipped code and asking what an attacker would try. Saying so is not ceremony — a reader
must be able to tell a design that was argued from an argument assembled around code that already
exists, because only the first is evidence the design was considered.

## Second reviewer: OUTSTANDING

No reviewer has read this, and no agent session can be one. The requirement is **OUTSTANDING**.
`fix/live-findings` MUST NOT merge to `main` while this line stands.

## What the token is, in one paragraph

`openssl rand -hex 24` — 192 bits, 48 hex characters — generated in step 60 and written to
`/etc/maran/panel.env` (`root:maran 0640`) as `Setup__Token=`. Step 90 reads it back and prints
`https://<fqdn>:<port>/setup?token=<token>` to the operator's terminal. `/setup` is an SPA route
that prefills the token field from `route.query.token`; posting it with a username and password
creates the **first administrator**. `CompleteSetupCommandHandler` refuses the token once the
`Users` table holds any row (`:65 if (await _dbContext.Users.AnyAsync(…))`), refuses an empty
configured token (`:104`), and compares with `CryptographicOperations.FixedTimeEquals` (`:113`).

So: until the first administrator exists, this string **is** the whole server. Afterwards it is
worth nothing.

## What an attacker would try

Written as attacks, not as a list of what the author got right.

### 1. Read it out of a log file — and this one WORKS

A token in a URL is exposed wherever URLs are recorded, and the one place that matters on this host
is the panel's own web server. `installer/nginx/maran.conf` sets **no `log_format`**
(`grep -n log_format installer/nginx/maran.conf` → nothing), so nginx uses its built-in `combined`,
which logs `"$request"` — **method, URI and query string**. `:139 access_log
/var/log/maran/nginx-access.log;`.

Measured, not reasoned, in the Ubuntu 24.04 polygon image with a probe vhost written the same way
(`access_log` with no format argument):

```
"GET /setup?token=PROBETOKEN1234deadbeef HTTP/1.1" 200
"GET /setup HTTP/1.1" 200
```

The second request is the control: the same grep finds nothing in that line, so the probe
discriminates between a logged token and an unlogged one rather than matching anything.

**So the moment the operator opens the link, the token is written to
`/var/log/maran/nginx-access.log`.** That directory is `root:maran 0750`
(`installer/lib/40-user.sh`, and `install.sh` re-hardens it), so it is readable by every uid in the
`maran` group — which is the panel's own service account, not a hosting customer. And
`installer/logrotate/` holds rules for `maran-ftps` and `maran-sites` only: **the panel's own nginx
logs are never rotated**, so the line stays on disk for the life of the host.

Two things make this less bad than it reads, and both should be stated rather than relied on:

- The window is short. The token stops working the instant the first administrator exists, which is
  seconds after that request. A token recovered from the log afterwards buys nothing.
- The reader has to already be the `maran` uid or root. A hosting customer is in neither.

But the *combination* is the part worth a decision: an install that is begun and abandoned — the
operator opens the link, is distracted, never completes setup — leaves a **live** token in a file
that outlives the install, in exactly the place `rules/security.md` item 8 says a token must not go
("never in logs … a one-time setup token … goes to the operator's terminal and never to a log file
that outlives the install"). The install log was hardened against precisely this and the nginx
access log was not.

**This contradicts the reviewer packet**, which recorded "What is safe, verified: it never reaches a
log." That verification was true of `/var/log/maran/install.log` and of that file only; the packet
did not consider the vhost. Recorded here so the correction is not lost.

### 2. Read it out of the shell — and here the arguments hold

- **Shell history.** The token never appears in a command the operator types. It is generated inside
  a function, written to a file, and read back with `awk`; the operator's line is `bash install.sh`.
  Nothing to recover.
- **The process table.** `openssl rand -hex 24` takes the *length* on its command line, not the
  output, so a second uid running `ps` during the install sees no secret. The value moves through a
  shell variable and a `0640` file, never an argv.
- **The install log.** Genuinely closed, and deliberately: `90-finish.sh:33-35` says why, and the
  print goes to fd 3 (opened before logging was redirected) or to `/dev/tty`, never to stdout.
  `grep -n 'setup_url' installer/lib/90-finish.sh` shows the value used only in those two `printf`s.

### 3. Read it off the screen, or out of where the operator puts it

These are the exposures a URL adds over a bare token, and none is closable by code:

- **Terminal scrollback**, and a screenshot or a screen-share of it. Identical for a bare token, so
  the URL adds nothing here.
- **A copied line pasted into a ticket, a chat, or a wiki.** The URL makes this *more* likely, not
  less, because a link is the thing people paste. The printed text says "do not paste it into a chat
  or a ticket" — which is discipline, and this repository distrusts discipline.
- **Browser history and the address bar.** This is the real addition. The operator is now guided to
  put a credential into an address bar: it enters the browser's persistent history, its autocomplete
  suggestions, and — if the profile is signed in — whatever sync that browser performs. `SetupPage`
  calls `router.replace({ name: 'login' })` after success, which replaces the *session* entry; it
  does not unwrite the browser's history database. Nothing in this product can.
- **A `Referer` header to a third party.** Does not apply: the `/setup` page loads no external
  resource, and the CDN allow-list question does not arise because the SPA is served entirely from
  this host.

### 4. Guess it

192 bits from `openssl rand`, constant-time compared. Not a path.

### 5. Use it after setup

Refused by `Users.AnyAsync`, which is a check on state rather than on a stored flag, so there is no
"mark as used" step to fail. An empty configured token is refused outright, so a `panel.env` that
lost the line does not become a panel that accepts anything.

## Why the URL was chosen, and what the alternative costs

The comment at `90-finish.sh:36-39` gives the reason and it is a real one: the operator pastes one
thing instead of transcribing a 48-character secret by hand, and a mistyped token is a support call.
It was once a bare token, and `:10-14` records why the link came back — an installer that ends by
naming a screen that does not exist teaches the operator to distrust everything else it printed.

The alternative that keeps the convenience and closes attack 1 is to print the link **without** the
token in the query string and have the operator paste the token into the field — two things instead
of one. The cost is the transcription this change was made to avoid. A second alternative closes
attack 1 alone and nothing else: give the panel vhost a `log_format` that omits the query string, or
`access_log off` for `location = /setup`. That is a one-line change to
`installer/nginx/maran.conf` and it does not touch the operator's experience at all.

**No change is made by this note.** It is an argument, and the choice is the owner's.

## What a second reviewer must check, specifically

Each of these can come back the other way; none is a gesture.

1. **Reproduce attack 1.** `grep -n log_format installer/nginx/maran.conf` → expect nothing. Then on
   an installed host, open the setup link and run
   `grep -c 'token=' /var/log/maran/nginx-access.log`. **The author could not run that half**: the
   measurement above is from a probe vhost in a polygon image, which establishes nginx's default
   behaviour and that the vhost sets no format, not that an installed host's own log carries the
   line. A reviewer on a real install can close that gap in one command.
2. **Rule on whether this clears the bar for token handling** (`rules/security.md` item 8). The
   author's reading is that it does **not**, on the strength of attack 1 alone, and that the
   cheapest honest fix is the `log_format`/`access_log off` one — the query string is the exposure,
   not the link.
3. Confirm the token is absent from the install log on a real install:
   `grep -c Setup__Token /var/log/maran/install.log` and `grep -c 'token=' …` → expect 0 for both.
   This is the claim the previous pass verified; it is worth re-running because it is the one thing
   the design deliberately bought.
4. Confirm `/etc/maran/panel.env` is `root:maran 0640` on a real install, since that file is the
   token's only intended home.
5. Decide whether the printed "do not paste it into a chat or a ticket" is doing any work. It is
   discipline, and `rules/README.md` says a rule a machine can hold should not be left to a reader.
6. Decide whether an abandoned install — link opened, setup never completed — is an accepted state.
   It is the only case where a **live** token sits in an unrotated log file, and it is the case this
   note would most like a human to rule on.

## What the author could not verify

- Anything on a real installed host: every measurement here is from a polygon image or from reading
  the tree. The images run the installer's own code but boot no systemd and serve no panel.
- Whether any browser the operator is likely to use syncs the address to a remote profile. That is a
  fact about other people's software.
- Whether an operator in practice pastes the link somewhere. Unknowable from here, and it is the
  exposure the URL form adds.

---

## Correction and resolution, 2026-09-11 (second pass, after the change)

This section is written by the session that CHANGED the code this note argued about, so read it as
what it is: an author's account of their own decision. The note above still stands as the argument,
and the second reviewer requirement above is **still OUTSTANDING** — a fix does not discharge it,
and `fix/live-findings` still must not merge to `main` while that line stands. What changed is that
a decision was taken rather than deferred, and two facts in the note are now measured differently.

### 1. Attack 1 was reproduced properly, and the note's measurement was weaker than its claim

The note measured a **probe vhost written the same way**. This pass measured the shipped file: the
installer's own `step_nginx` rendered and installed `installer/nginx/maran.conf` in the Ubuntu 24.04
polygon image, the distribution's own nginx 1.24.0 served it, and two real requests were made.

```
127.0.0.1 - - [11/Sep/2026:17:40:21 +0000] "GET /setup?token=PROBETOKEN1234deadbeef HTTP/2.0" 200 38 "-" "curl/8.5.0"
127.0.0.1 - - [11/Sep/2026:17:40:21 +0000] "GET /setup HTTP/2.0" 200 38 "-" "curl/8.5.0"
```

in `/var/log/maran/nginx-access.log`, with `/var/log/maran` observed as `drwxr-x--- root maran`. The
note's conclusion was right; its evidence was one step short of the file that ships, and that
distinction is the difference between the two earlier passes being wrong and being unlucky.

### 2. A FACT THE NOTE DID NOT HAVE, and it inverts the note's recommendation

The note names the cheapest fix as a `log_format` that omits the query string, or `access_log off`
for the setup location, and calls the query string "the exposure, not the link".

**nginx's ERROR log takes no format and cannot be given one.** For any request that errors, nginx
writes the raw request line into it. Measured with the query-free access-log format already in
place:

```
--- access log ---
127.0.0.1 - - [11/Sep/2026:17:40:49 +0000] "GET /api/v1/anything HTTP/2.0" 502 166 "-" "curl/8.5.0"
--- error log ---
2026/09/11 17:40:49 [crit] ... request: "GET /api/v1/anything?token=PROBETOKEN1234deadbeef HTTP/2.0",
upstream: "http://unix:/run/maran/api.sock:/api/v1/anything?token=PROBETOKEN1234deadbeef", ...
--- secret count: access=0 error=1 ---
```

The secret appears twice in one error-log line. A 502 while the api is still starting and a 404
before the SPA files are in place are ordinary states during an install — which is exactly when the
token is live. So the `log_format`-only fix does not close attack 1: **the link IS the exposure, not
only the query string**, and the note's recommendation was the cheapest fix to a narrower problem
than the one it had found.

### 3. What was changed

- `installer/lib/90-finish.sh` — the printed link is `https://<fqdn>:<port>/setup` and the token is
  handed over on its own labelled line, with a sentence saying why it is not in the link. The screen
  is still named, which is what `:10-14` records as the reason the link came back. `SetupPage.vue`
  needed no change: its token field is an ordinary required input defaulting to empty.
- `installer/nginx/maran.conf` — a `map` cutting the path out of `$request_uri` and a
  `log_format maran_no_query` used by the one `access_log`. Second line of defence, not the fix:
  it makes this vhost structurally unable to log a query string, for the next secret as well as this
  one. `$uri` was rejected as the basis for that format because `try_files` rewrites it and every
  SPA route would be logged as `/index.html` — measured, and the polygon assertion catches it.
- `installer/logrotate/maran-panel` (new), installed by `installer/lib/80-nginx.sh` — daily,
  30 days, `USR1`. A separate defect: nothing had ever rotated the panel's own two nginx logs.
- `docker/polygon/assert-installer-steps.sh` —
  `assert_the_panel_vhost_can_never_log_a_query_string` (a census over every `access_log` and every
  `log_format` in the vhost, plus a behavioural pair of requests through real nginx with a control
  in both directions), and a census inside
  `assert_the_finish_message_tells_the_truth_about_this_install` requiring every occurrence of the
  token in the printed message to stand as its own word.

### 4. Answers to the checks this note asked a reviewer for

- **Check 1 (reproduce attack 1).** Done, in the polygon, on the shipped file (§1). The half the
  note could not run — an installed host's own log — is still not run: these images boot no systemd
  and serve no panel. What is now gated is the property rather than the instance.
- **Check 2 (does this clear item 8?).** The author's reading was that it did not. Agreed, and the
  note's proposed fix would not have cleared it either, for the reason in §2. It clears it now
  for the log; item 8's "never in URLs" clears by the token leaving the URL.
- **Check 6 (is an abandoned install an accepted state?).** STILL THE OWNER'S. Nothing expires the
  token: `CompleteSetupCommandHandler` gates on `Users.AnyAsync` alone and `60-config.sh` writes no
  issue time. An abandoned install now leaves the token in `panel.env` (`root:maran 0640`) and in
  the operator's scrollback — the two places the design accepts — and no longer in a group-readable
  file nothing rotates. An expiry needs `backend/**` and is a product judgement about the window.
- **Check 5 (is "do not paste it into a chat" doing any work?).** Less than it was: the line people
  paste no longer carries the secret. The sentence is kept, because the token is still printed and a
  human can still paste it, but it is no longer the only thing standing between a URL and a ticket.

### 5. What is still open after this change

- `frontend/src/pages/auth/SetupPage.vue` still reads `?token=` and prefills the field, so a URL
  carrying a token still works if somebody builds one by hand. Removing the prefill would close the
  shape entirely and is a frontend change.
- `installer/uninstall.sh` does not remove `/etc/logrotate.d/maran-panel`, which it does remove for
  `maran-ftps` and `maran-sites`. One line, in a file this pass could not write.
- No expiry on the token (above).

## Correction, 2026-09-12 (third pass): check 6 is closed, and the note's answer to it was wrong

This is a dated correction to §4 and §5 above, not a rewrite of them. What those sections say was
true when they were written and is now out of date in one specific place, which is named here so a
reader who trusts the earlier text is stopped rather than misled.

### What changed in the tree

**`CompleteSetupCommandHandler` no longer gates on `Users.AnyAsync` alone.** It now has three gates,
in this order: whether the panel already has an owner, whether the token is the configured one
(unchanged, still `FixedTimeEquals`), and — new — whether that token is still inside a **24-hour
window**. So the sentence in §4 check 6, "Nothing expires the token", and the last line of §5, "No
expiry on the token", are both now FALSE. They are left standing above with this paragraph pointing
at them.

**The clock the window is measured from is a thing the panel observes, not a thing the installer
writes.** §4 check 6 identified the obstacle correctly — `60-config.sh` writes no issue time — and
then drew the wrong conclusion from it, that an expiry therefore needed the installer to start
writing one. It does not. The first instant the panel can honestly record is the first time it ran
with that token configured, which on a real install is seconds after the token was generated, and
the panel can measure that for itself. `identity.SetupTokenWindow` is one row holding that instant;
`SetupTokenWindowSeeder`, run by a Host startup task, records it at every start and
`SetupTokenWindowKeeper.OpenAsync` makes re-recording a no-op. The note's framing turned a panel-side
measurement into an installer change, and that is why this was read as a bigger change than it was.

**The row holds a SHA-256 fingerprint of the token, never the token.** Item 8 forbids a secret at
rest in a place a database dump copies. The fingerprint answers the only question asked of the value
— is the token configured now the one this window was opened for — and the fact that it does not
answer any other question is the point.

### Why 24 hours, stated as the trade rather than as a number

An install is finished in minutes, so every candidate window is generous for the ordinary case and
the choice is entirely about the two failures either side. Too short strands the operator who runs
the installer and is then called away: they return to a server they cannot claim, which converts a
security control into a support call and teaches them to keep the token somewhere convenient — the
exact habit §3 of this note exists to discourage. Too long is the state this note calls dangerous.
A day is the longest span over which "the person who ran the installer is the person coming back to
it" is safe: it survives a lunch, a meeting, an evening and a night's sleep, and it does not survive
a weekend, a holiday, or a forgotten trial install — none of which should still be claimable.

### The dead end this note would otherwise have created, and how it is avoided

An expiry with no remedy is worse than no expiry: it produces a server nobody can claim, and the
operator's only route back is a reinstall. The remedy is the fingerprint, and it is the mechanism
rather than a special case beside it — an operator who sets a **new** token in `panel.env` and
restarts the panel gets a fingerprint that does not match the recorded one, which `Reopen` reads as a
new token and starts a fresh clock. The refusal message says exactly that, in all three locales, and
carries no filesystem path: *"This setup token has expired, so no administrator was created… Nothing
you entered is wrong. To claim this server, set a new setup token in the panel's environment
configuration, restart the panel, and enter that new token here."* The "nothing you entered is wrong"
clause is this module's calibration for a refusal the caller did not cause, and it is load-bearing
here: without it the screen reads as a rejected token and the operator retypes it.

### The ordering choice, which is a disclosure decision

The expiry is checked **after** the token comparison, not before. An expiry reported to a caller who
guessed wrong would tell them the value they guessed was otherwise acceptable — it would turn the
192-bit guess of §3 into an oracle. A stranger probing a live server therefore learns only
"unauthorized", and `An_expired_window_does_not_tell_a_wrong_token_that_it_was_otherwise_acceptable`
is the test that holds that ordering up rather than leaving it to the reading.

### What this correction does NOT close

- **The `?token=` prefill in `SetupPage.vue` is still there.** §5's first item stands unchanged; it is
  a frontend change and was outside this pass's writable scope. The window shortens the life of a
  token in a hand-built URL; it does not stop one being built.
- **`installer/uninstall.sh` still does not remove `/etc/logrotate.d/maran-panel`.** §5's second item
  stands unchanged, for the same reason: not this pass's scope.
- **The window is not a record of the token being spent**, and must not be read as one. That question
  is still answered by whether any user exists — state rather than a flag, so it has no "mark as
  used" step that can fail. This row only narrows the window in which the *unspent* token works.
- **An operator who abandons an install and never restarts the panel** has a window that opened at
  the install and closes 24 hours later, which is the intended behaviour. An operator who abandons an
  install on a panel that keeps restarting has the same window, because opening is idempotent — a
  panel cannot be kept claimable by restarting it, and `Restarting_the_panel_does_not_extend_a_window_that_is_already_open`
  is what makes that an assertion rather than a claim.
