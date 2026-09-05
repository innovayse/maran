# Threat note — moving the panel's trust boundary onto the kernel

Required by `rules/security.md` ("Sensitive change escalation"): this changes the panel's
listening surface and the input every address-keyed protection it has depends on.
**This change needs a second reviewer.**

Closes the flaw investigated in `2026-09-03-loopback-trust-boundary.md`. That note recommended
Option 1, a shared secret set by nginx. **The owner chose Option 2, a unix domain socket with
peer credentials**, under the criterion "secure and clean", and against the note's own strongest
argument for it: Option 1 converts a structural property into a secret, and a leaked secret is
undetectable — no log line, no failure, and the panel goes on believing the header. A socket
cannot leak by being read. This repository also already authenticates a local peer this way, in
the other direction: `agent/crates/agent/src/peercred/`.

The attacker model is unchanged from the investigation: a hosting customer who holds a panel
account on this host and can run a process on it — a cron entry, or PHP in their own site, where
`curl_exec`, `fsockopen` and `stream_socket_client` are all still enabled. They control neither
the panel's code nor its configuration.

---

## 1. What was wrong

`installer/lib/60-config.sh` bound Kestrel to `http://127.0.0.1:5080`, and
`AddPanelForwardedHeaders` trusts `IPAddress.Loopback` and `IPAddress.IPv6Loopback` as proxies.
A loopback port is reachable by **every uid on the machine**, and a process that reaches it
connects with source address `127.0.0.1` — which is exactly what the trusted list contains — so
its `X-Forwarded-For` was honoured.

What that bought a customer, live at `HEAD` before this change:

- **Audit-journal address forgery at fifteen call sites.** Every controller builds its audit
  entry from `HttpContext.Connection.RemoteIpAddress`. The `IpAddress` column of `AuditEntry` was
  attacker-supplied text. This is the consequence with the longest tail: an incident response
  that trusts that column is reading what the attacker wrote.
- **Login rate-limit evasion, becoming an indefinite administrator lockout.**
  `LoginRateLimitPolicy.BuildPartitionKey` is the remote address and nothing else. A rotating
  forged header gives a fresh five-attempt bucket per request; `MaxFailedLoginAttempts = 10` and
  a 15-minute lockout then let any customer hold the administrator's account locked forever,
  attributed to an address that is not theirs.
- **Session-list forgery**, the same value by the same route.
- **Aiming a firewall ban at a third party**, once the brute-force detector has a producer.

`X-Forwarded-Proto` is forgeable by the same route and was checked to have no consequence:
`RefreshCookie` hardcodes `Secure = true`, and nothing else in `backend/src` is scheme-dependent.

---

## 2. The measurement, because it inverts the naive fix

The whole cost of the chosen option turned on one unknown: over a unix socket
`HttpContext.Connection.RemoteIpAddress` is `null`, so `ForwardedHeadersMiddleware` has no
address to match against `KnownProxies`. Does it decline, throw, or accept?

**It silently ACCEPTS. On .NET 9 a null peer address does not fail the known-proxy check — it
SKIPS it. The forged header is honoured from any peer, with the known-proxy list bypassed
entirely.**

Measured, not reasoned: a minimal `net9.0` app (SDK 9.0.317) wired with the panel's exact
`ForwardedHeadersOptions`, listening on a unix socket, probed over that socket.

```
M1-no-header      : HTTP 200 :: remote=<null>       xff=''            x-original-for=''
M1-forged-header  : HTTP 200 :: remote=203.0.113.7  xff=''            x-original-for=''
M2-synth-no-header: HTTP 200 :: remote=127.0.0.1    xff=''            x-original-for=''
M2-synth-forged   : HTTP 200 :: remote=203.0.113.7  xff=''            x-original-for='127.0.0.1:0'
M2-synth-stranger : HTTP 200 :: remote=198.51.100.4 xff='203.0.113.7' x-original-for=''
```

The tell is `X-Original-For`. On the M1 forged line it is **empty**: the middleware did not run
its comparison and find a match, it never ran the comparison. .NET's implementation carries an
explicit carve-out allowing a null remote address "for servers that don't support it natively",
and that carve-out is a bypass. `X-Forwarded-Proto` rides along the same way — `scheme=https` on
a plaintext socket.

**This is the same failure shape as the empty-`KnownProxies` defect the existing tests pin:
absent information reads as "trust everyone".** A naive port to a unix socket — change
`ASPNETCORE_URLS`, change `proxy_pass`, touch nothing else — would have *removed* the last
address check while looking like a hardening change. **Had this not been measured, the fix would
have shipped as a regression.**

Three further facts decided the build, and each was also run:

- **The framework middleware does not have to be replaced.** A component ahead of
  `UseForwardedHeaders()` that assigns `Connection.RemoteIpAddress` restores the designed
  behaviour completely (M2): loopback + forged header → honoured *and* `X-Original-For`
  populated; a non-loopback synthetic address + forged header → declined, with the header left
  unconsumed. `AddPanelForwardedHeaders` keeps its options block and both `Add` calls unchanged.
- **Peer credentials are reachable from managed C#.** `IConnectionSocketFeature` is present on a
  Kestrel UDS connection and `Socket.GetRawSocketOption(SOL_SOCKET, SO_PEERCRED)` returns the
  12-byte `struct ucred`. No libc P/Invoke.
- **Kestrel gives the socket whatever the umask gives it, and offers no option to choose.**
  Measured `775` under this repository's development umask. Under the shipped unit's `UMask=0027`
  it is `0750` — re-measured inside a booted container on both families, with the real unit, so
  the earlier "world-connectable under systemd's default `0755`" line in this note was true of a
  default the panel does not run under and is corrected here. **The direction of the finding is
  unchanged and it is still the most dangerous one on the page: the socket's own permissions
  cannot be the boundary unless something sets them after bind.** Group write — which is what
  nginx needs — is granted by none of those defaults, and a panel started under a laxer umask is
  world-connectable outright.

---

## 3. What defends this now

### 3.1 The boundary: a directory a customer cannot traverse

The socket is `/run/maran-api/api.sock`. Its directory is built by **`/etc/tmpfiles.d/maran-api.conf`**
— rendered by `installer/lib/70-services.sh` from `installer/systemd/maran-api.tmpfiles.conf` — as
a single line:

```
d /run/maran-api 2710 panel <web server group> -
```

`www-data` on the Debian family, `nginx` on the RHEL family, from `MARAN_WEB_SERVER_GROUP`, the one
place either name is decided.

**Why a tmpfiles snippet and not `RuntimeDirectory=`, which is the obvious spelling.** The first
version of this change used `RuntimeDirectory=maran-api` with `RuntimeDirectoryMode=2710` and an
`ExecStartPre=+/usr/bin/chgrp <web group>`, and this section described it as working. It did not.
**systemd re-runs its exec-directory setup for EVERY command invocation of a unit, not once per
start, and that setup re-applies the unit's `User=`/`Group=` to `RuntimeDirectory=`.** The chgrp
therefore took effect inside its own invocation and was undone before the next one, so `ExecStart`
always saw `panel:panel`, the socket inherited the group `panel`, and:

```
uid 33 (www-data)  connect("/run/maran-api/api.sock")  ->  EACCES (13, Permission denied)
```

That is the syscall nginx makes for `proxy_pass http://maran_api`, so a **fully applied install
answered 502 to every API call, on both families.** It was measured by the second reviewer on
booted systemd — Ubuntu 24.04 / systemd 255 and AlmaLinux 9 / systemd 252 — with a trace showing
`IN-SAME-PRE 2710 panel:www-data` and `AT-NEXT-PRE 2710 panel:panel`. The failure was closed rather
than open (a customer uid was still refused), but the panel did not work, and the operator's
shortest route back to a working panel — `chmod 0755 /run/maran-api`, or adding the web server to
the `panel` group — is precisely how this boundary gets widened by hand.

systemd offers no per-`RuntimeDirectory` group setting, so the group has to come from something the
unit's own command invocations do not re-apply. `systemd-tmpfiles` is that something: it runs
before the unit (`systemd-tmpfiles-setup.service` is `Before=sysinit.target`, and every ordinary
unit is ordered after `sysinit.target`), it corrects a directory that already exists rather than
only creating one, and nothing a unit does touches it. The unit therefore declares **no**
`RuntimeDirectory=` at all; it names the directory only in `ReadWritePaths=`, because
`ProtectSystem=strict` would otherwise leave it read-only, and removes its own socket in
`ExecStopPost=` because systemd no longer deletes the directory on stop.

**Measured after the fix, on booted systemd on both families**, with the real unit rendered by
`render_api_unit` and only `ExecStart=` replaced by a stub that binds the socket and chmods it
`0660` exactly as `ListenSocketGuard` does:

```
systemd 255 (255.4-1ubuntu8.17)          systemd 252 (252-67.el9_8.4.alma.1)
  DIR  2710 panel:www-data                 DIR  2710 panel:nginx
  SOCK  660 panel:www-data                 SOCK  660 panel:nginx
  uid 33   (www-data) connect -> CONNECTED   uid 998  (nginx)    connect -> CONNECTED
  uid 1001 (customer) connect -> EACCES 13   uid 1001 (customer) connect -> EACCES 13
```

unchanged across a start, a restart, a reload (the shipped unit defines no `ExecReload`, so this
was run with an `ExecReload` drop-in, which is the case that would re-apply ownership if any
still could) and a `SIGKILL` of the main process with the automatic restart that follows.

**The alternatives, and what each costs.** `Group=<web group>` on the unit gives the directory a
stable group, but it takes the panel out of group `panel` — it could then no longer read
`/etc/maran/panel.env`, which is `root:panel 0640` — and every file the panel writes lands in the
web server's group. `SupplementaryGroups=<web group>` does not solve the problem at all: it widens
the panel's rights without changing the directory's group, and it only helps if the directory is
opened to `0711`, which makes the socket's own mode the boundary — the "one permission digit
silently turns the defence off" shape this design exists to avoid. A root `ExecStartPost` chgrp of
the socket races the bind. A directory outside `/run` is the same answer as this one without the
tmpfs's free cleanup on reboot.

**What stops a customer's process, stated exactly: the directory has no permissions for "other",
so a uid that is neither `panel` nor a member of the web server's group cannot resolve a path
inside it. `connect(2)` fails at path resolution with `EACCES` — before any socket permission is
consulted, before any byte is sent, and before the panel is involved at all.** A customer's cron
job runs as their own account uid, and their PHP-FPM pool runs `user = <account>` /
`group = <account>` (`templates/php-fpm/pool.conf.j2`), so neither is in that group. This is a
kernel check on filesystem permissions, not an application check on a value.

The `0710` half is applied **at creation**, so the directory is never briefly open: it covers the
window between Kestrel's `bind()` and the panel narrowing the socket, which is the one moment the
socket file itself is permissive.

**Two things the directory not being a `RuntimeDirectory=` changes, and how each is answered.**
systemd no longer deletes it when the unit stops, so it no longer deletes the socket either, and
the server refuses to bind over an existing path rather than reusing it — a `SIGKILL`ed panel would
otherwise never restart. `ExecStopPost=-/usr/bin/rm -f /run/maran-api/api.sock` removes it; it runs
however the service stopped, and the `-` prefix keeps a failure to unlink from replacing the loud
"address already in use" refusal with a second one. **Mutation-checked on both families:** with
`ExecStopPost=` reset away, the same stop/start produced
`OSError: [Errno 98] Address already in use` and the unit stayed down. And because the directory is
outside the unit's own paths, `ReadWritePaths=` must name it: without that, `ProtectSystem=strict`
leaves it read-only and the panel cannot create a socket. If the tmpfiles snippet is missing
entirely the unit does not start at all — measured, `226/NAMESPACE`, "Failed to set up mount
namespacing: /run/maran-api: No such file or directory" — which is a closed and loud failure rather
than a panel serving on a directory nobody arranged.

**Re-running the installer is a repair.** `systemd-tmpfiles --create` corrects an existing
directory, so a host whose ownership was changed by hand is put back: measured, `2755 panel:panel`
after a hand `chgrp`/`chmod`, `2710 panel:<web group>` after `--create`. `70-services.sh` then
checks the result on the real host — `stat -c '%a %U %G'` against `2710 panel:<web group>` — and
refuses the install if it is anything else, and waits for the socket and checks it is
`660 panel:<web group>` before reporting success. That postcondition is the only thing in the
product that observes this boundary on the machine it protects.

The setgid half exists because the api unit holds **no capabilities at all**
(`CapabilityBoundingSet=` is empty) and therefore cannot `chgrp` anything. With setgid, the
socket Kestrel creates inside inherits the directory's group — measured twice, and visible above
as `SOCK 660 panel:<web group>`.

### 3.2 The second lock: the socket's own mode

`ListenSocketGuard` runs at `ApplicationStarted`, reads the endpoints the server actually bound
from `IServerAddressesFeature` — so there is no second copy of the socket path to disagree with
the first — narrows each unix socket to `0660`, and **reads the mode back**. Owner (`panel`) and
group (the web server's) may use it; nobody else may.

### 3.3 The third lock, and the only one a test can drive: peer credentials

`PanelPeerAddressMiddleware`, registered immediately before `UseForwardedHeaders()`, reads the
peer's uid from `SO_PEERCRED` for any connection with no IP address. The permitted uid — the web
server's, resolved at install time by `60-config.sh` into `ReverseProxy__PeerUid` — has its
connection stamped `127.0.0.1`, which makes the framework's known-proxy comparison run and match.
Any other peer is refused `403` before the header is looked at.

`PanelPeerPolicy` mirrors `peer_policy.rs`: one uid, **no special case for root**, and absent
credentials are a denial rather than a reason to fall back to something weaker — the choice
`peer_guard.rs` makes for the same reason on the other side of this relationship.

"Absent" is checked rather than assumed. On Linux `getsockopt(SO_PEERCRED)` **succeeds** on a TCP
socket and reports `pid=0 uid=4294967295 gid=4294967295` — measured — so a reader that only caught
the exception would hand the policy the number `4294967295` to compare instead of an absence to
refuse. `PeerCredentials.TryRead` turns `(uid_t)-1` back into `null`, and a test pins both
directions.

In production this check is unreachable, because §3.1 already stopped the caller. Its value is
that it holds if §3.1 is ever wrong, and that it is the one stop a test can exercise.

### 3.4 Every failure direction is closed and loud

| If this goes wrong | What happens |
|---|---|
| The socket's mode cannot be set or reads back with "other" bits | Critical log, exit code 1, `StopApplication()` |
| A unix socket is bound with no `ReverseProxy:PeerUid` | Critical log, exit code 1, `StopApplication()` |
| A unix socket AND a TCP endpoint are bound at once | Critical log, exit code 1, `StopApplication()` |
| Peer credentials unreadable, or absent, on a socket connection | `403` |
| The peer is any uid but the configured one | `403`, logged at warning with uid and pid |
| The socket directory has the wrong owner, group or mode | The installer refuses the install, naming the observed and required values |
| The api never binds its socket | The installer refuses the install, pointing at `journalctl -u maran-api` |
| The tmpfiles snippet is missing | The unit fails to start, `226/NAMESPACE` |
| The panel is on TCP (development, or a server not yet re-installed) | Warning on every boot naming the exposure |

The exit code is deliberate. `StopApplication()` alone is a graceful shutdown, so the process
exits `0`, systemd records `inactive (dead)` — indistinguishable from an operator having stopped
the panel — and `Restart=on-failure` does not fire. Setting the exit code first makes it `failed`,
which is what `systemctl status` reports and what an operator looks for.

**The mixed-transport row is a finding, not a hypothetical.** `ASPNETCORE_URLS` is a list and
Kestrel binds every entry, so `http://unix:/run/maran-api/api.sock;http://127.0.0.1:5080` — one
line in `/etc/maran/panel.env` — leaves the socket half looking perfectly healthy while the TCP
half restores §1's flaw in full. It was the only silently-insecure state this design had; it is now
refused, with a test.

**The one branch that looks fail-open and is not.** `PanelPeerAddressMiddleware` stands aside for a
request with no address, **no socket** and no peer uid configured — which is the in-memory test
server and nothing else, since it presents neither of the two things the component reads. Refusing
there would fail every host test in the repository for a reason that has nothing to do with the
panel. A connection that HAS a socket is always decided, including when no uid is configured: an
unconfigured `PanelPeerPolicy` permits nobody, so it is refused. That case is narrow but real —
`ListenSocketGuard` runs at `ApplicationStarted`, which is after Kestrel is accepting, so a
socket-bound panel with no peer uid serves for the length of the shutdown it asks for. It serves
`403` for that window rather than standing aside in it, and a test pins that.

---

## 4. What this does NOT defend, stated plainly

- **A panel still on TCP.** An existing installation keeps the flaw until the installer is
  re-run. See §5. The boot warning is the only thing that says so.
- **Anything running as `root`, as `panel`, or as the web server user.** Root can do anything;
  `panel` is the panel; a compromised nginx is already the proxy. The boundary is
  "not a customer's process", which is what the machine's boundary actually is — it was never
  "not a local process".
- **A customer added to the web server's group by an operator.** Nothing in Maran does this, and
  nothing should; it would hand that account the panel's socket. Worth stating because it is the
  one configuration change on a host that quietly undoes §3.1 — and because it is exactly what an
  operator reaches for when the panel 502s, which is why §3.1's first implementation being wrong
  was a security problem and not only a broken build.
- **The panel's outbound surface.** `RestrictAddressFamilies` still admits `AF_INET`/`AF_INET6`,
  because the licence lease and ACME still dial out. The panel no longer *listens* on TCP; it
  still speaks it.
- **Any address the proxy itself is fooled by.** `X-Forwarded-For` is `$proxy_add_x_forwarded_for`
  with `ForwardLimit = 1`, so the panel reads the peer nginx observed. A client that sends its own
  chain gets it appended to, not believed — that property is unchanged and pinned by an existing
  test.

## 5. Upgrade — and this only fully applies on a re-run

**An existing installation is TCP-bound and stays that way until `install.sh` runs again.** The
flaw is live on it in the meantime. This is the loudest sentence in this note, and the panel says
it too: `ListenSocketGuard` logs a warning on every boot of a TCP-bound panel naming the
consequence.

One installer run rewrites all four halves — `panel.env` (60), the unit and the tmpfiles snippet
(70), and the vhost (80) — and `write_config` regenerates `panel.env` on every run, so no operator
edit is needed. Step 70 applies the snippet with `systemd-tmpfiles --create` rather than waiting
for a reboot, and then checks the directory it produced. There is no data migration and nothing to
roll forward.

**The window.** 70-services restarts the api before 80-nginx re-renders the vhost, so for a few
seconds nginx proxies to a TCP port nothing is listening on. Every API call answers `502`; the
SPA's static files still serve. It recovers when 80-nginx reloads. This is the outage the
investigation predicted for this option, and it is real.

**If it half-applies**, every state fails loud, and this was checked case by case:

| Stopped after | State | What the operator sees |
|---|---|---|
| 60 only | panel.env says socket, api still running on TCP, vhost on TCP | Nothing wrong — until the next restart, then `502` |
| 60 + 70 | api on the socket, vhost still on TCP | Every API call `502`; the SPA loads and does nothing |
| 70 with the old unit and no tmpfiles snippet | Socket directory `2710 panel:panel`; nginx cannot traverse | `502` — and step 70 now refuses to finish rather than reporting success |
| Unit updated, `panel.env` stale | api on TCP, vhost on the socket | `502` |

**There is no half-applied state that is silently insecure.** The worst case is a stale
`panel.env` leaving the panel exactly where it is today — the flaw present, no worse — and every
other partial state is a `502`. Re-running the installer fixes all of them; it is idempotent and
preserves the encryption and signing keys.

## 6. What a second reviewer should check

The first two items on this list were the ones a second reviewer found wrong. They are recorded
here as answered rather than deleted, because the answer is the substance of §3.1.

1. **The two host facts this rests on, on both families.** ANSWERED, on booted systemd (255 and
   252), with the real unit: `/run/maran-api` comes out `2710 panel:www-data` on the Debian family
   and `2710 panel:nginx` on the RHEL family, the socket inside inherits that group at
   `660 panel:<web group>`, the web server's uid connects and a customer's uid gets `EACCES` —
   through a start, a restart, a reload and a `SIGKILL`-and-restart. The first implementation of
   §3.1 failed this on both families and every text-level check passed anyway; the check that
   claimed to cover it is discussed under item 5.
2. **Which route applies the setgid bit.** ANSWERED and now moot: `RuntimeDirectoryMode=` was the
   route that applied it and `ExecStartPre=+` does escape `RestrictSUIDSGID=` (both measured by the
   reviewer), but the directory is no longer a `RuntimeDirectory=` and the unit sets no mode at
   all. `systemd-tmpfiles` applies mode, owner and group together, and re-applies them on demand.
3. **That nothing adds a customer account to the web server's group**, now or later.
4. **That `X-Original-For` is a stable enough framework behaviour to assert on.** It is the only
   observable that distinguishes "the known-proxy comparison ran and matched" from "there was no
   address so it was skipped", and one test's load-bearing assertion is built on it. Checked: it is
   `ForwardedHeadersDefaults.XOriginalForHeaderName`, public framework surface with a documented
   default.
5. **What the polygon can and cannot see about this boundary.** It builds the directory with the
   installer's own `install_units`/`build_api_socket_directory` and this family's real
   `systemd-tmpfiles`, then stats it, breaks it by hand and checks the installer's postcondition
   refuses, and repairs it. It **cannot** boot systemd — its `/usr/bin/systemctl` is a stand-in — so
   it never starts the unit and never watches the connect. It says so in its own output rather than
   implying otherwise; the connect is item 1's measurement. The version of this check that this
   change replaced grepped the unit's text and its failure message claimed nginx would be locked
   out, which is a proposition a grep cannot hold, and it is why item 1 reached review labelled
   verified when it was false.
6. **`80-nginx.sh` used to stage the rendered vhost as `maran.conf.staging` inside `conf.d/`,
   which `include conf.d/*.conf` does not match — so `nginx -t` validated the OLD file, not the
   new one.** Pre-existing, not introduced here, and FIXED IN THE WORKING TREE by the agent who
   owns that step: it now stages as `.candidate`, swaps, validates, and restores `.previous` on
   refusal, and the polygon drives all three cases against a real nginx. Recorded because this
   change's vhost was validated by hand against a real nginx while that was still open, and a
   reader of the two reports should know which state the tree is in.
