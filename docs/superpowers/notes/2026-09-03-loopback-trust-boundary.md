# Threat note — the loopback trust boundary, and what to do about it

Follows `2026-09-04-cron-firewall-monitoring-threat-note.md` §3, which named this and deliberately did not
propose a closure: *"the panel's trust boundary is 'loopback', while the machine's actual
boundary is 'not a customer's process' … the obvious closures are a spec change and are
named here, not proposed here."* This note does the proposing. It changes nothing.

`rules/security.md` ("Sensitive change escalation") puts any change to this surface behind
a second reviewer and a written threat note, which is why this is a **recommendation with
options and a decision left to the owner**, not a patch. **This needs a second reviewer
before any of it is built.**

## Verdict

**The flaw is real, it is live today, and it is older than Plan 5.**

Kestrel binds `http://127.0.0.1:5080` (`installer/lib/60-config.sh`, `write_config`), and
`AddPanelForwardedHeaders` trusts `IPAddress.Loopback` and `IPAddress.IPv6Loopback` as
proxies. Nothing between them checks *which* local process connected. Every per-address
protection in the panel — the login rate-limit partition, the audit journal's address
column, the session list, and the ban path once it has a producer — therefore reads a value
that any local process chooses for itself.

**Severity: high for integrity of the audit journal and for the login rate limiter, both
of which are live now; high for the ban path when Task 13 lands; not a privilege
escalation in any case.** The attacker must already hold an account on the host. What they
gain is not access they lacked but the ability to *speak as somebody else* to every
address-keyed mechanism the panel has.

**Recommendation: Option 1 — a shared secret set by nginx**, with the secret in a
root-owned `0600` include file (not in the `0644` vhost the installer writes today), the
panel refusing to start when the key is absent, and a missing-or-wrong secret degrading to
"use the real peer address" rather than to a `403`.

The rest of this note is the evidence, then the options.

---

## 1. Verification: is it real?

The dispatch asked for this to be disproved if possible. Each of the five things that
could have prevented it was read, and none does.

### The bind address

`installer/lib/60-config.sh` writes `ASPNETCORE_URLS=http://127.0.0.1:5080` into
`/etc/maran/panel.env`, and `installer/nginx/maran.conf` proxies to `http://127.0.0.1:5080`
from `location /api/`. Loopback-only is confirmed, and it is what stops a *remote* attacker.
It does nothing about a local one: `127.0.0.1:5080` is reachable by every uid on the box.

### The forwarded-headers configuration

`backend/src/Maran.Host/Extensions/ForwardedHeadersExtensions.cs`, at `HEAD`: `XForwardedFor
| XForwardedProto`, `ForwardLimit = 1`, both known lists cleared and then `IPAddress.Loopback`
and `IPAddress.IPv6Loopback` re-added. This is correct code, and the re-add is the
load-bearing part (§3 of the Plan 5 note explains why an empty list means "trust everyone").
Its own doc comment states the intent exactly: *"a direct connection to port 5080 keeps its
own address whatever it claims."* **That sentence is false as written, and it is false
because the connection's own address is `127.0.0.1`, which is on the trusted list.** The
configuration does what it says; what it says is not what the deployment needs.

`app.UseForwardedHeaders()` is first in the pipeline (`Program.cs`), before
`UseRateLimiter()` — so the rewritten address is what the limiter and every controller see.

### The firewall

`agent/crates/templates/tests/golden/nftables/ruleset.nft` — `policy drop` on input, with
`tcp dport 5080` **not** among the allowed ports. That closes the remote path a second time.
The first rule in the chain is `iif "lo" accept`, and it must be: with `policy drop` and no
loopback exemption the host cannot talk to itself at all. There is **no output chain and no
uid-aware rule anywhere in either table**, so nftables neither can nor does distinguish
nginx's connection to `5080` from a customer's.

### A sandbox on the cron path

There is none, and there cannot usefully be one. `agent/crates/ops/src/cron/installed_line.rs`
renders `<schedule> /bin/sh <home>/…/<id>.cmd > <log> 2>&1; echo $? > <exit>` into the
account's real crontab via `crontab(1)`; the command file holds whatever
`CronCommand::parse` accepted, which is *"1 to 4096 bytes of UTF-8 … with no control
character … Everything else is allowed"* (`agent-core/src/validation/system/cron_command.rs`).
The system cron daemon runs it as the account's uid, outside any unit the panel controls.
`CronEntriesController` is `[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]`,
so this is an ordinary customer capability. One line — `curl -H 'X-Forwarded-For: 203.0.113.9'
http://127.0.0.1:5080/api/v1/auth/login -d …` — is the whole exploit.

`maran-api.service` is heavily sandboxed, but the sandbox constrains the *panel*, not the
caller. `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6` is what lets it listen at all.

### The bit the dispatch did not ask about, and it matters: cron is not the only vector

Plan 3 already shipped it. `agent/crates/templates/templates/php-fpm/pool.conf.j2` disables
`exec,passthru,shell_exec,system,proc_open,popen,putenv,pcntl_exec,pcntl_fork,dl,proc_nice,
proc_terminate` and sets `allow_url_fopen = off` — and leaves `curl_exec`, `fsockopen` and
`stream_socket_client` enabled, which is correct for ordinary sites and sufficient for this
attack. A customer with a PHP site has had a local HTTP client since Plan 3.

**So Plan 5 did not create this. It widened it** — from "a customer who runs PHP" to "every
customer", and it made the timing acute by putting a ban feature next to it. Recording this
matters for how the fix is scheduled: it cannot be filed as "a Plan 5 regression to fix
before Plan 5 merges", because reverting Plan 5 does not close it.

**Verdict on §1: the flaw exists. Nothing in the binding configuration, the vhost, the
ruleset or the cron path prevents it.**

---

## 2. What it actually buys an attacker

Stated precisely, because "forge your IP" covers four different things with four different
severities.

### Live today

- **Login rate-limit evasion.** `LoginRateLimitPolicy.BuildPartitionKey` is
  `context.Connection.RemoteIpAddress?.ToString() ?? "unknown"` and nothing else — by
  design, after an earlier version let the caller pick the partition through a query
  string. A local attacker rotating `X-Forwarded-For` gets a fresh five-attempt bucket per
  request, which is the same defect in a new place. **Bounded by `User.MaxFailedLoginAttempts
  = 10` and `LockoutDuration = 15 min`**, which stops password guessing — and converts the
  attack into a **denial of service**: any customer can hold the administrator's account
  locked indefinitely, ten forged attempts at a time, from an address that is not theirs.
- **Audit-journal forgery.** Every controller in the panel builds its audit entry from
  `HttpContext.Connection.RemoteIpAddress` (fifteen call sites: Accounts, Auth, Sessions,
  Setup, Sites, Ssl, Sftp, Databases, Cron ×2, Firewall ×3, and the rate limiter). A local
  caller writes whatever it likes into the `IpAddress` column of `AuditEntry`. This is the
  consequence with the longest tail: an incident response that trusts that column is reading
  attacker-supplied text.
- **Session-list forgery**, the same value by the same route.

### Dormant until Task 13

- **Aiming a ban at a third party.** `BruteForceDetectedHandler` exists and
  `BruteForceDetected` has **no producer outside its own tests** (confirmed by grep over
  `backend/src` and `backend/tests`). When the detector lands, forged failures under a chosen
  address become a `drop` element in `banned_v4`/`banned_v6` on this host — an operator's
  office, a monitoring probe, an ACME validator, a competitor.
- **Evading one's own ban.** Two ways, and only one of them is about the firewall. The
  panel-side one is the real one: with a fresh forged address per request the attacker's own
  address never accrues failures, so no ban is ever aimed at it. The packet-level one is
  that a local process reaches nginx and Kestrel over `lo` whatever the ban set holds.

### The bound `iif "lo" accept` already provides, exactly

Both tables accept `iif "lo"` before consulting the ban sets — at priority `-5` in
`maran_bans` and again at priority `0` in `maran` — and an `accept` verdict ends only its own
chain, so the packet is accepted twice over. That is a tested property of the goldens.

**What it bounds:** a ban aimed at `127.0.0.1` or `::1` cannot sever the nginx→Kestrel hop,
so *"ban loopback and take the panel offline"* is not available. Nor can it cut the panel off
from PostgreSQL or the agent, which are unix sockets and never touch the network stack.

**What it does not bound:** aiming a ban at any **non-loopback** address — which is the whole
attack; **journal forgery**, which nftables has no view of; **rate-limit evasion**, which is a
partition key in managed code, not a packet filter; and **ban evasion**, for the panel-side
reason above. The exemption is also, unavoidably, part of the enabling condition: it is what
makes `127.0.0.1:5080` reachable from a customer's process in the first place.

### Checked and found harmless

`X-Forwarded-Proto` is forgeable by the same route and has no consequence found:
`RefreshCookie` hardcodes `Secure = true`, and a grep for `IsHttps`, `CookieSecurePolicy` and
scheme-dependent redirects across `backend/src` returns nothing else. Recorded so the next
reader knows it was looked at rather than forgotten.

---

## 3. The options

### Option 1 — a shared secret set by nginx (**recommended**)

nginx adds `proxy_set_header X-Maran-Proxy <secret>;` inside `location /api/` of the panel
vhost. The panel honours `X-Forwarded-For` only from a loopback peer **that also presented
the secret**; anything else keeps its real peer address.

**What it defeats.** Every case in §2, from every local vector — cron, PHP, a compromised
service, a future one nobody has thought of. The check is on the request, so it is
independent of how the caller got a process.

**What it does not defeat.** Anyone who can read the secret. It is a credential, and it has
credential failure modes.

**What it costs to build.** Small, and every piece has an existing pattern.
`CsrfHeaderMiddleware` is the header-checking middleware to copy. `SecurityOptions` is where
the key binds. `60-config.sh` already generates secrets, already preserves selected ones
across re-runs (`existing_value`), and runs before `80-nginx.sh`, which re-renders the vhost
unconditionally on every run — so the pair can be written in one installer pass.
`assert-installer-steps.sh` already asserts panel.env ↔ vhost agreement for the port numbers;
the same assertion shape covers the secret. `ForwardedClientAddressTests` already drives the
real `Program` pipeline and reads the address back out of the audit journal, so the new
proposition — *a loopback caller with no secret is recorded as `127.0.0.1`, not as what it
claimed* — is a third test in a file that already exists.

**What it costs an operator on upgrade.** Nothing they do by hand. `panel.env` is regenerated
and the vhost re-rendered by the same installer run. The one real hazard is an operator who
hand-edited the vhost (to add Let's Encrypt, say) — but 80-nginx.sh already overwrites such
edits today, so this adds no new class of surprise.

**How it fails if misconfigured** — three modes, and the first two are **fail-open**:

- **FAIL-OPEN: the key is unset and the check is written as "require it if configured".**
  This is bug-for-bug the empty-`KnownProxies` defect: absent configuration reads as "trust
  everyone". *Mitigation, and it is not optional: the panel refuses to start when the key is
  missing or empty.* A panel that will not boot is a loud failure; a panel that trusts
  everyone is a silent one.
- **FAIL-OPEN: the secret leaks.** `80-nginx.sh` installs the vhost with `install -m 0644`
  into `/etc/nginx/conf.d/`, which every customer on the box can read. **A secret written
  into that file is not a secret.** It must live in its own root-owned `0600` file, included
  from the vhost — nginx's master reads its configuration as root, at start and at reload,
  so `0600 root:root` works and `nginx -t` in the installer still passes. This is the single
  implementation detail that decides whether Option 1 is worth building.
- **Fail-degraded, not fail-open: nginx stops sending the header.** Every request is then
  recorded as `127.0.0.1` — the pre-forwarded-headers state, with one shared login bucket and
  a journal that says only "the server". Bad, but it is the failure direction that does not
  hand an attacker an identity. It must be logged at warning and be visible; a silent
  degradation here is how a defence gets removed and nobody notices.

One property worth stating because it is easy to miss: a customer **can** create a proxy site
whose upstream is `127.0.0.1:5080` — `Upstream::parse` deliberately permits loopback — but
that path does not forge, because the customer vhost uses `$proxy_add_x_forwarded_for`, which
*appends*, and `ForwardLimit = 1` takes the last entry. The secret would not be set on that
location either. Two independent stops. The direct-to-Kestrel path is the only one that
forges, and it is the one Option 1 closes.

### Option 2 — a unix domain socket with peer credentials

Kestrel listens on `/run/maran-api/api.sock` instead of a TCP port; only processes the socket's
permissions admit can connect. This is the pattern the repository already uses between the
panel and the agent, and it was read before being assessed:
`crates/agent/src/peercred/peer_policy.rs` holds an allow-list of exactly one uid, and
`peer_guard.rs` is a tonic interceptor reading it from `SO_PEERCRED` — *"the kernel fills it in
at connect time: it cannot be set by the caller, unlike anything carried in the request
itself"*, with absent credentials treated as denial rather than as a reason to fall back.

**What it defeats.** Everything Option 1 defeats, and it cannot be defeated by a file being
readable, because the guarantee is enforced by the kernel and by filesystem permissions on a
directory under `/run` rather than by a value staying unknown.

**What it does not defeat.** Nothing local — but note that the agent's `SO_PEERCRED` check is
*not* what does the work here. The agent needs it because its socket directory is deliberately
group-readable by `panel`; a panel socket that only nginx's group may open gets the same
guarantee from the mode bits, and a peer-cred check on top is belt-and-braces rather than the
boundary. Reading `peercred/` is what makes that clear, and it is the honest answer to "does
this option get the agent's pattern for free": it gets the *idea* for free and none of the
code, because tonic-over-UDS and Kestrel-over-UDS share no machinery.

**What it costs to build — and this is the argument.** `HttpContext.Connection.RemoteIpAddress`
is **null** over a unix socket: Kestrel's remote endpoint is a `UnixDomainSocketEndPoint`, not
an `IPEndPoint`. `ForwardedHeadersMiddleware` decides whether to honour the header by matching
that address against `KnownProxies`. With no address there is nothing to match, so the
middleware either declines to apply the header — leaving every request as "unknown" — or
faults. **Which of the two must be measured on .NET 9, not reasoned about**, in the spirit the
firewall step is written in (`nft -f` on a missing include was measured, not assumed). Either
way the framework middleware has to be replaced or fronted by a connection middleware that
synthesises a peer address, and `AddPanelForwardedHeaders` — the file this whole note is
about — is rewritten rather than adjusted. Add: the socket's creation, ownership and mode
(the api unit has `RuntimeDirectory=maran-api` at `0750` owned `panel:panel`, so nginx's group
must be granted access, and `web_server_user()`/`web_server_group()` are already distro facts);
stale-socket handling across restarts; `ASPNETCORE_URLS` and the vhost's `proxy_pass` both
changing; and an integration-test harness for unix sockets that does not exist —
`ForwardedClientAddressTests` runs over HTTP today.

There is also a rules question the owner should answer before anyone starts:
`rules/security.md` §10 says *"no new listening ports, no new daemons … Anything of the kind
requires a spec change first, not a PR."* Swapping a TCP listener for a unix socket is a
change to the panel's listening surface. It is a *reduction*, which is the right direction, but
it is still the kind of change that rule sends to the spec first.

**What it costs an operator on upgrade.** More than Option 1, in one specific way: there is a
window in which the new api is listening on a socket and the old vhost is still proxying to
`127.0.0.1:5080`, or the reverse. Both halves are rewritten by the same installer run, but
`systemctl restart maran-api` and `systemctl reload nginx` are two steps, and the panel is down
between them. Recoverable, loud, and not silent — which is the good kind of failure — but it is
a real outage where Option 1 has none.

**How it fails if misconfigured** — two **fail-open** modes, and one of them is worse than
today:

- **FAIL-OPEN, and strictly worse than the status quo: the socket's mode or its directory's
  mode is too wide** (`0666`, or `RuntimeDirectoryMode=0755`). Every local process can connect
  again — and by then the address-based trust check has been *removed*, so there is nothing
  behind it. One permission digit silently turns the entire defence off.
- **FAIL-OPEN: Kestrel keeps the TCP listener too**, because `ASPNETCORE_URLS` carries both or
  a stale `panel.env` was not regenerated. The old path stays open and nothing reports it.
- Fail-closed and loud: a missing or stale socket is an nginx `502`.

### Option 3 — accept the risk, in writing

**What it defeats.** Nothing.

**What the residual actually is, bounded.** An attacker must already hold a panel account on
this host and be able to run a process on it (a cron entry, or PHP in their own site). They
gain no privilege. What they gain is: a forged address column in the audit journal; unlimited
login attempts against the account lockout (ten, then fifteen minutes, repeatable — a
denial of service against the administrator, attributed to somebody else); a forged session
list; and, once Task 13 lands, the power to have this host `drop` an address of their choosing
and to keep their own address off the ladder. They cannot ban loopback into an outage, and a
whitelisted address is skipped on the automatic path (though **not** on the manual one —
`BanAddressCommandHandler` does not consult the whitelist).

**What it costs to build.** Nothing. **On upgrade.** Nothing. **How it fails.** It does not
fail; it is the steady state. The cost is paid later, by whoever has to explain the journal.

**This option is only defensible while the detector does not exist**, and it comes with a
condition: the Plan 5 note's residual list already carries the item, and `ForwardedHeadersExtensions`'
doc comment carries a sentence that is now known to be false. Accepting the risk means fixing
that sentence in the same breath, because a defence that documents a guarantee it does not
provide is worse than one that documents none.

### Option 4 — an nftables rule keyed on the sending uid (considered, not recommended)

An output chain in `table inet maran`: `oif "lo" ip daddr 127.0.0.1 tcp dport 5080 meta skuid
!= { 0, <web server uid> } reject`. Zero backend code, and it fits the crate that already owns
the ruleset.

Rejected for three reasons. It adds an output chain to a ruleset that has only ever had an
input chain, which is a new failure domain in the one file whose failure mode is *the host
boots with no firewall at all*. It hard-codes a uid that a package upgrade can change. And its
failure mode is the worst of any option here: **`systemctl stop nftables` removes the panel's
address trust boundary, silently, and nothing in the panel knows.** An application-layer
invariant enforced only in the packet filter is invisible to every test the backend has.

---

## 4. Recommendation

**Option 1**, built with these four conditions, none of which is optional:

1. The secret lives in its own file, `root:root 0600`, `include`d by the panel vhost. **Never
   in `/etc/nginx/conf.d/maran.conf`, which the installer writes world-readable.**
2. The panel **refuses to start** when the key is missing or empty. Absent configuration must
   never read as "trust everyone" — that is the defect this whole area already produced once.
3. A loopback caller without the secret keeps its real peer address (`127.0.0.1`) and is
   **logged at warning**. Not a `403`: a mismatch after a partial upgrade should degrade the
   journal, not take the panel offline.
4. Loopback stays in `KnownProxies`. The secret is an *additional* condition, not a
   replacement — the trust boundary is not being moved, it is being narrowed from "any local
   process" to "the local process nginx is".

Why this one, in one paragraph: it is the only option that closes every case in §2 without
changing the panel's listening surface, it reuses four patterns that already exist in this
repository (`CsrfHeaderMiddleware`, `SecurityOptions`, `existing_value` preservation in
`60-config.sh`, the polygon's panel.env↔vhost assertion), it drops a third test into a file
whose two existing tests already assert exactly the propositions either side of it, and its
worst realistic failure — nginx stops sending the header — degrades the journal instead of
handing an attacker an identity. Option 2's guarantee is stronger and its cost is concentrated
in exactly the place a stronger guarantee is least helpful: a rewrite of the middleware that
decides the value, plus a test harness that does not exist, plus a rule that says to change the
spec first.

### The strongest argument against this recommendation

**It converts a structural property into a secret, and this repository has already
demonstrated that it publishes the file that secret would live in.** `80-nginx.sh` writes the
vhost `0644`; every customer on the box can read `/etc/nginx/conf.d/`. Condition 1 above is
therefore not a detail, it is the whole defence — and unlike Option 2's kernel-enforced
answer, a leak of this secret is **undetectable**: no log line, no failure, nothing to alert
on, and the panel goes on believing the header. Option 2 cannot leak by being read. If the
owner weighs "can fail silently and invisibly" above "costs a middleware rewrite and a spec
change", Option 2 is the correct answer and this recommendation is wrong.

### What would change my mind

- **Measurement on .NET 9 showing `ForwardedHeadersMiddleware` behaves usably with a null
  `RemoteIpAddress`**, or that synthesising a peer address is a short connection middleware
  rather than a replacement. Option 2's cost is dominated by that unknown; if it collapses,
  Option 2 wins on the merits, because it removes the TCP port entirely and every *future*
  decision that keys on "the caller is local" is then correct by construction.
- **A second local consumer of the API appearing in the spec** — a CLI, a plugin, hostpanel on
  the same host. A shared secret in two places is a credential-distribution problem; a socket
  with peer credentials is an allow-list of uids, which is what the agent already does and what
  it would then be right to copy properly.
- **The owner deciding Task 13 is not in this release.** That does not make Option 3 correct —
  the journal and rate-limiter forgeries are live today with no detector — but it changes the
  urgency from "before the ban feature ships" to "before the next audit-driven investigation
  trusts that column".
- **Evidence that `web_server_user()`'s uid is stable across package upgrades on both
  families**, which is the only thing standing between Option 4 and being cheap. It would still
  fail open when the firewall is stopped, so this would move it from "rejected" to "rejected
  for one reason instead of three".
