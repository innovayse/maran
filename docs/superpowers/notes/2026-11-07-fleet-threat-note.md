# Fleet mode: a network-reachable agent and a managed-server registry (issues #180, #319) - threat note

**Surface:** a TCP + mutual-TLS listener on the root agent, an anonymous enrolment endpoint on the
panel, a panel client identity that every enrolled node accepts as root, a registry of servers
(`Maran.Modules.Fleet`), and the routing that decides which server an agent call reaches
(`IAgentTarget`, `X-Maran-Server`, `Account.ServerId`). It touches the agent's privileged surface,
authentication by consequence (a new credential class: the enrolment token and the panel identity),
session-adjacent request handling (a header that selects where work lands) and a tenant boundary (a
customer must never choose or reach a server). rules/security.md "Sensitive change escalation" names
the agent's privs module and auth/token handling, so this change needs a second reviewer and a note
written before the change. This is that note.

**Status: written BEFORE the code.** At the time of writing nothing of Fleet exists in the tree: there
is no `Maran.Modules/Fleet`, no `agent/crates/agent/src/fleet/`, no `IAgentTarget`, no `Account.ServerId`
(`grep -rln ServerId backend/src` hits only Licensing's `IServerIdentitySource`, which is a different
thing: the host's identity for the licence). Statements about code that does not exist yet are
requirements the code must meet; where the code differs from this note, the note is amended in the same
change.

**Second review: OUTSTANDING (issue #331 is the standing item for these).** The work is done by agent
sessions, which cannot dispatch a reviewer and cannot be one. Per rules/security.md the change may
live on a branch and MUST NOT merge to `main` until a second reviewer has read this note and checked
every item under "What a reviewer must check". It is not covered by the beta pre-release exception
unless the owner records it for a named release in writing. The owner is to be told at hand-off that
three surfaces carry an outstanding review: the fleet listener, the enrolment endpoint, and the panel
client identity. The fleet note joins #331's list.

**Owner decisions applied (the plan's recommended options, NOT yet approved by the owner):** see "Owner
decisions this note assumes". A different answer to any of them changes this note, and several change
the design.

## The current code, as read for this note

These facts were read from the tree on 2026-11-07 and are what the plan was checked against.

1. **A selector placeholder exists.** `frontend/src/components/shell/ShellServerSelector.vue` draws
   nothing with one server, shows an icon and a name from two on, and says the switching menu "lands
   with #180". `stores/servers.ts` has `current = servers[0]` and `load` only. #319 is therefore
   "make the placeholder a real switch", not a new component.
2. **`GET /api/v1/servers` is in the wrong module.** `Maran.Modules/Monitoring/Controllers/ServersController.cs`
   (class-level `AdminOnly`, read-only) with `Queries/ListServers/` returns a fixed one-element list.
   A registry of servers is a facility several modules need (rules/architecture.md: a facility more
   than one module needs is its own module), so it becomes `Maran.Modules.Fleet`, and the Monitoring
   controller, query, DTO and enum move there on the same route with additive fields.
3. **Every agent call is bound to one unix socket for the life of the process.**
   `Maran.Agent.Client/DependencyInjection.cs` registers one `GrpcChannel` singleton from
   `AgentChannel.CreateUnixSocket(socketPath)` and 13 singleton clients built from it. 170 non-generated
   files under `backend/src` mention an `IAgent*Client`
   (`grep -rln "IAgent[A-Za-z]*Client" --include=*.cs backend/src | grep -v '/obj/\|/bin/\|Generated' | wc -l`).
   Routing is the dominant cost of this feature; the transport is the smaller part.
4. **The agent has one listener and one authorisation rule.** `agent/crates/agent/src/server.rs`
   (410 lines now, already over the 400-line review trigger) builds one `UnixListener`; every service
   carries `PeerGuard`, which reads `UdsConnectInfo.peer_cred` and admits one uid. A TCP peer has no
   uid, so the fleet listener needs its own guard rather than a loosened `PeerPolicy`; absent
   credentials are a denial today and must stay one.
5. **The unit already permits sockets of the right family.** `installer/systemd/maran-agent.service`
   has `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK`, so listening needs no hardening
   change. The `maran units` ceilings must be re-run, not assumed unchanged.
6. **No proto change is required.** `GetAgentInfo` already returns the version, distro, family and
   contract revision the registry shows. The contract baseline, `PROTO_VERSION` and the
   `AgentProtoVersionWindow` stay untouched; a node on another revision is `Incompatible`.
7. **Dependencies.** `ring` and `rustls` are in `agent/Cargo.lock` beneath other crates; `rcgen` is not
   (`grep -c rcgen agent/Cargo.lock` is 0); `sha2` is already a direct dependency. tonic is held at
   0.13.1 with no TLS feature enabled today.
8. **Spec and rules forbid this as written.** Spec section 3 lists Fleet as deliberately deferred
   (lines 36, 68); rules/architecture.md says "exactly three processes" and "Fleet mode exists only as
   the transport abstraction"; rules/security.md item 10 says new listening ports and outbound calls
   need a spec change first. This note does not edit them: the document edits are task T0 of the plan
   and need the owner's approval.
9. **The working tree is not clean** (`feature/146-425-security`): `agent/crates/agent/src/server.rs`
   and `Monitoring/Services/AlertObserver.cs` are modified there, among many others. The fleet work
   touches the same files and must start from `dev` after that branch merges, or rebase and say so.
10. **Account move (#127) does not exist.** The issue's "move accounts between servers" cannot be
    delivered by this work; see decision D-SPLIT.

## What is being added

| Piece | Where | Note |
|---|---|---|
| TCP + mutual-TLS listener | `agent/crates/agent/src/fleet/` | Off unless `--fleet-listen` and `--fleet-dir` are given. Same service registry, same contract, TLS 1.3 only. Serves nothing to a peer whose certificate is not the pinned panel certificate. |
| Node identity | `maran-agent --generate-node-identity <dir>`; `/etc/maran/fleet/{node.key,node.pem,panel.pem}` | Key generated on the node, `0600 root` inside a `0700 root` directory, never transmitted. |
| Fleet peer guard | `agent/crates/agent/src/fleet/fleet_peer_guard.rs` | Interceptor: SHA-256 of the presented certificate equals the pin, constant-time. |
| `maran node enroll / leave / status` | `installer/cli/lib/node.sh` | Fixed verbs, root-only, argv arrays. No daemon, no port beyond the agent's, no RPC. |
| Enrolment endpoint | `POST /api/v1/fleet/enrolment` (Fleet module) | Anonymous, token-authenticated, rate-limited, 16 KiB body cap. |
| Server registry | `Maran.Modules.Fleet`, schema `fleet` | `Servers`, `ServerEnrolments`, `FleetIdentity` (the panel's client key, encrypted at rest). |
| Panel to node channel | `Maran.Agent.Client` channel pool + mTLS channel | New OUTBOUND connections from the panel to approved nodes only. |
| Routing | `IAgentTarget`, `IAgentChannels`, `IAgentEndpointSource`, `X-Maran-Server` | Decides which server a call reaches. |
| Placement | `Accounts.Account.ServerId` | Which server an account lives on. |

Not added: no proto change, no new rpc, no new `AgentCapability` beyond `System` for the Fleet module,
no remote update trigger, no CA, no node-to-node traffic, no third daemon, no code loaded by the
agent. A managed node runs the agent only: no `maran-api`, no PostgreSQL.

## What an attacker could do, by position

**An internet scanner reaches the agent port.** The listener completes a TLS 1.3 handshake only with a
peer presenting a client certificate that is the pinned panel certificate; every other peer is refused
in the handshake, before HTTP/2, before any interceptor, before any service. There is no
unauthenticated rpc: `GetAgentInfo` sits behind the same guard. What remains is the cost of the
handshake (CPU) and the open port. Mitigation: enrolment requires `--allow-from` (the panel's
address) and opens the port only to it; `--allow-any` is accepted only with a printed warning.
Residual: a handshake flood from an allowed address.

**Someone holds the panel's client certificate but not its key.** Useless; the handshake proves
possession of the key.

**Someone holds a node's private key.** They can impersonate that node to the panel and receive the
commands meant for it (generated account passwords, backup instructions); they cannot reach any other
node or the panel. Remedy: remove the server and re-enrol the host with a new key. The key is `0600
root` in a `0700 root` directory; customers run as account uids and cannot read it.

**A rogue host presents itself as a node.** The panel dials no host that is not approved. Approval
requires the administrator to TYPE the SHA-256 fingerprint the genuine node printed on its own
terminal, with the enrolment's source address and time shown beside it; a mismatch is refused. A token
thief can enrol a host but cannot get it approved without the administrator comparing against a
fingerprint that came from the thief's host. The remaining risk is an administrator approving without
reading the fingerprint; the dialog requires it typed, not clicked.

**A token is stolen or guessed.** 256 bits from the CSPRNG, single use, 60-minute life, SHA-256 digest
at rest with a unique index, at most five outstanding. A consumed, expired and unknown token all answer
one identical refusal (400, one body) so the endpoint is not an oracle; the real cause is in the audit
entry. The endpoint is anonymous by necessity, so it carries a named rate-limit policy per source
address, the body cap, and bounded certificate parsing. Whether it also carries an API-key-style
failure limiter (temporary ban after N refusals) is an owner decision.

**A malformed certificate or PEM bomb.** Parsing is on bounded input (16 KiB), accepts exactly one PEM
block and one X.509 certificate, ECDSA P-256 only, validity at most 800 days, self-signature must
verify, the SAN must contain the declared address; everything else is the same refusal. The BCL
parses; no custom ASN.1.

**The panel is compromised.** The attacker has the encryption key and the database, hence the panel's
client key, and every node accepts that identity as root. This is the blast radius the panel already
has over its own server, now extended to every enrolled node. It is stated plainly rather than argued
away: one panel compromise is a fleet compromise. What limits it: the agent's command set is closed and
typed, nothing executes caller-supplied programs, and each node's own agent writes a `maran_audit` line
for every mutating rpc with `transport=fleet` into a journal the panel cannot rewrite. An operator who
suspects the panel runs `maran node leave` on each node.

**An attacker on the path.** TLS 1.3 only, mutual authentication, pinning on both sides, no downgrade.
Identity is the pinned fingerprint, not a host name, so DNS spoofing changes where the panel dials, not
whom it trusts.

**The enrolment HTTPS leg.** The node verifies the panel through the system trust store, or
`--panel-fingerprint` for a self-signed panel (the case #425/#426 creates). With neither, the CLI
refuses; it never falls back to an insecure fetch.

**A customer on a node.** Cannot read the key, cannot reach the listener as anything but a stranger,
and the account-scoped operations they could already trigger on that node are unchanged.

**A reseller or customer aims a request at another server.** `X-Maran-Server` is honoured for
administrators only and ignored for everyone else; a malformed value is refused (400), not defaulted
to Local. Every account-scoped handler selects the target from the account row, which the tenant query
filter already restricts to the caller's own accounts (404 otherwise). Resellers do not choose a
server; their accounts land on the default server.

**Confused deputy: an operation lands on the wrong server.** The worst functional failure: a vhost, a
database or a password written on a server that does not host the account, or the primary modified for
an account that lives on a node. Controls: (a) no default target, so a missing `Begin` throws
`AgentTargetNotSelectedException` in single-server mode too; (b) an architecture test that every
production type taking an `IAgent*Client` also takes `IAgentTarget`, with a planted violation and an
accepting control; (c) the account's `ServerId` is read from the row, never from the request; (d) a
two-server integration test per module family asserting the node, not the primary, saw the call, with a
control proving the assertion can fail; (e) an unknown, pending or disabled server id refuses with a
typed error instead of falling back to Local. `Begin` uses an AsyncLocal scope, which does not flow
back out of an awaited callee: it must be called in the method that makes the agent calls, and a unit
test says so.

**Account passwords and secrets cross the network.** They already pass to the local agent over a unix
socket; they now cross TCP inside TLS 1.3 with pinned peers. The only new at-rest secret is the panel
client key, encrypted with the existing `IEncryptionService`, never logged, never returned by any
endpoint, never in an audit subject.

**Denial of service.** A dead node must not take the panel down: the health probe has its own 5 s
timeout and pipeline (the existing agent pipelines are tuned for a unix socket); `/health` and
`/health/ready` keep reporting the LOCAL agent only, so a down node cannot turn readiness into 503;
unattended jobs skip an unreachable node, log once and continue. A node that cannot reach the panel is
unaffected (the agent never calls out).

**Version skew.** A node outside `AgentProtoVersionWindow` (exact today) is `Incompatible` and no
operation is routed to it. The panel does not trigger updates on nodes in phase 1.

## Why pinning instead of a CA

A CA adds a key that can mint trusted peers, a signing flow to harden and revocation to maintain, for a
problem that does not exist here: one client and a handful of servers an administrator named. Pinning
makes the trust list exactly the approved nodes and the panel identity exactly one certificate. The
cost: rotating the panel identity means re-enrolling every node, and a certificate (two years) is
renewed only by re-enrolment. Accepted for v1, stated in the operator guide.

## What a reviewer must check

1. `fleet_peer_guard.rs` and the TLS config: a client with NO certificate, a client with a DIFFERENT
   valid self-signed certificate, and a client with the right certificate but an expired validity are
   each refused; the right certificate is admitted. Confirm the tests can fail (invert the comparison).
2. That the root-store check really rejects a different self-signed leaf with the same subject when the
   pinned leaf is used as trust anchor, and that the interceptor, not the root store, is what refuses
   when the store check is bypassed in a test.
3. That the fleet listener does not start when the flags are absent, and that a removed pin file stops
   it from admitting anyone.
4. That the unix-socket listener keeps `PeerGuard` unchanged and that no fleet code path is reachable
   without the fleet guard on every service.
5. That the enrolment endpoint answers every token fault with one status and body and that the audit
   entry carries the cause; that it has the rate-limit policy, the body cap and the bounded parse.
6. That no code path dials a server whose state is `Pending` or `Disabled`.
7. That `Begin` is called in every agent-driving handler and job, that the architecture test would
   catch a new one that forgets, and that the remaining `Local` selections are exactly the documented
   ones (health probe, `maran status`, Licensing's server identity, the panel-certificate commands).
8. That `X-Maran-Server` cannot influence a non-administrator, an account-scoped operation, or a
   background job.
9. That the panel client private key never appears in a log line, audit subject, API response,
   exception message or diagnostic bundle (`maran collect-diagnostics`).
10. That the node's firewall rule restricts the agent port to the stated source, and that the firewall
    module refuses to delete or deny the fleet port rule (the same lock-out guard shape as the panel port).
11. That the agent audit line gained `transport=` without gaining any caller-controlled text.
12. That `/health` and `maran status` ignore remote nodes.

## What the author could not verify

- The behaviour of rustls/webpki with a self-signed end-entity certificate as the client trust anchor
  (item 2 above). The interceptor is the backstop and must be shown to be the thing that refuses.
- .NET `SslStream` presenting a client certificate on Linux when the private key was loaded from PEM
  (ephemeral keys often must be round-tripped through PKCS#12). Only the real-host test proves it.
- TLS 1.3 + P-256 behaviour under AlmaLinux 9's crypto policy: the agent uses ring, which ignores the
  system policy; the panel uses .NET, which does not. To be tested on both families.
- That a handshake flood from an allowed address cannot starve the unix-socket listener (one runtime,
  two listeners).
- That the process-wide locks (`config_tree_lock`, `firewall_lock`, `cron_lock`, `account_lock`) are
  `static` as the plan claims, so one process serving two listeners keeps rules/rust.md "What this
  agent serialises". The plan states it from a grep; this note did not re-read the lock files.
- That `maran units` ceilings do not move.
- That 170 files is the true routing surface: a module that resolves a client through
  `IServiceProvider` is invisible to the census (rules/security.md item 13, stated limit).
- Anything about a real second host. Nothing here has been run; the two-container polygon run on
  Ubuntu 24.04 and AlmaLinux 9 is the only proof of the closing criterion.

## Stated limits

Compromise of the panel is compromise of every node. Rotation of the panel identity means re-enrolling
every node. Certificates expire and the only renewal is re-enrolment; the panel shows expiry and alerts
30 days ahead. The absence of an agent audit line proves nothing (journald can rate-limit). The fake
agents in the integration fixture prove routing, not that a real node executed the call. The
architecture census proves a type takes `IAgentTarget`, not that it calls `Begin` before its first agent
call; the runtime throw and the per-module endpoint tests are the other half. Switching the selected
server is not audited (it changes no state); every mutation it enables is journalled with the server it
hit, but threading the server through the other module journals is a follow-up issue.

## Dependencies (rules/security.md item 11)

Rust: tonic with a TLS feature (rustls via ring, already in the lockfile), `rcgen` (generates a keypair
and a self-signed certificate; a hand-written X.509 encoder in a root process would be worse), `sha2`
(already direct). Pinned exactly like their neighbours. `cargo audit` is run by hand once and its output
recorded. C#: none (`SslStream`, `X509Certificate2`, `CertificateRequest` are in the BCL).

## Owner decisions this note assumes (not yet approved)

1. **Scope split:** phase 1 ships without "move accounts between servers" (blocked on #127) and without
   remote update execution; two follow-up issues are opened.
2. **Trust model:** mutual pinning, no CA; rotating the panel identity re-enrols every node.
3. **Approval step:** the administrator types the node's fingerprint before the panel trusts it.
4. **Node profile:** a managed node is agent-only; amends "exactly three processes" to "on the primary".
5. **Agent port exposure:** `--allow-from` required at enrolment, `--allow-any` only with a warning;
   default port 8444.
6. **Certificate validity:** two years, renewal by re-enrolment, alert 30 days ahead (no renewal rpc).
7. **Remote update trigger:** none in v1 (the largest single addition to the surface for a convenience
   the security-patch timer already covers).
8. **Switching the server is not audited.**
9. **Placement:** only administrators choose a server; resellers land on the default; account, site and
   database names stay unique panel-wide.
10. **Licensing:** one licence, checked on the primary only; per-server seats are a later business
    decision.
11. **Enrolment endpoint lockout:** an API-key-style failure limiter in addition to the rate limit.
12. **Language of the spec correction** (sections 3 and 9 are Russian; the rest of the repository is
    English).
13. **Rule edits** (rules/architecture.md lines 8 and 100, rules/security.md item 10, the csharp.md and
    rust.md maps) need approval before they merge; this note edits none of them.
14. **Second review** stays OUTSTANDING and the branch does not merge to `main`; the owner names the
    reviewer.

## Mutation targets (for the post-implementation harness)

Invert the fingerprint comparison; drop the `FleetPeerGuard` interceptor; accept a Pending server in
the endpoint source; make the enrolment token not single-use; skip the self-signature check; skip the
key-algorithm check; honour `X-Maran-Server` for non-administrators; make `Begin` default to Local;
drop the `transport` tag. Each must turn a NAMED test red.

Website docs needed (for the owner; agents do not touch `website/`): "Fleet: manage several servers",
"Enrol a server", "Approve and remove a server", "Fleet security model", "Server selector", en/ru/hy.
