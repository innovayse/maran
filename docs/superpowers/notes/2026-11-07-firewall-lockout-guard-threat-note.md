# Threat note — the firewall refuses a rule that would lock the operator out (issue #159, tasks F-1 and F-2)

Required by `rules/security.md` ("Sensitive change escalation"): the change decides, in the panel,
whether a firewall mutation reaches the root agent, and the firewall is what keeps the operator's
SSH session and the panel reachable. Written BEFORE the change.

**Second review: OUTSTANDING** (second-review tracking: #331). The guard removes a lock-out risk
and adds no listening port, no daemon, no outbound call and no agent rpc, so this is the lighter
kind of note, but the author of a privileged change is the person least able to see what they
assumed (rules/security.md). The branch may land on a branch and MUST NOT merge to `main` until a
second reviewer has read this note and checked every item under "What a reviewer must check".

This note covers only the lock-out guard. The per-site request limit that shares the issue is a
separate change with its own note.

## What is added

`POST /api/v1/firewall/rules` (the administrator's "open a port" call) is refused with
`FirewallRuleLocksOutCaller` (kind `Conflict`, HTTP 409) when installing the rule would stop the
address the request arrived from from reaching an SSH port. The refusal is a hard one: there is no
override flag, no force parameter and no second confirmation that turns it into an allow. The
agent is not called for the allow. The refusal is journalled as a failed `FirewallRuleAllowed`
entry.

The SPA's confirmation dialog stays, but its text no longer claims to be the only guard.

## What changes about the threat model

- Before: the lock-out guard was a browser confirmation. The panel accepted every allow, because
  "the agent is fail-open by design" for SSH (see below). A scripted caller, an API client or a
  mistyped CIDR confirmed through a dialog could still install the rule that cut the operator off,
  and there is no remote recovery from a lost SSH path.
- After: the panel itself refuses the one operation that can cost the operator their way in, from
  the host's actual ruleset, whatever client sent it.
- Nothing becomes reachable and nothing becomes privileged: the change only adds a refusal in front
  of an existing call. The worst failure of the guard is a wrongly REFUSED allow (an annoyance with
  a stated way out) or a wrongly PERMITTED one (the state before this change).

## What can and cannot lock an operator out (verified in code, not assumed)

The agent renders an `inet` table with `policy drop`. Read in `agent/crates/templates/templates/nftables/ruleset.nft.j2`
and `agent/crates/ops/src/firewall/model/ruleset_state.rs` (`render`):

1. **The panel port** is rendered as an unconditional `tcp dport <panel port> accept` with no
   override in v1 (`ruleset_ports.rs`). No rule can close it, so the guard does not consider it.
2. **An SSH port** gets an unconditional accept only while no operator-authored rule for that port
   exists. The first TCP rule for exactly that port REPLACES the unconditional accept: from then
   on only the rules for that port admit anybody.
3. **A range never replaces it.** `render` routes a rule into an SSH port's group only when it is
   TCP and names a SINGLE port (`rule.ports.upper().is_none()`); a range, whatever its lower
   bound, is an ordinary allow and leaves that port's unconditional accept exactly where it was.
   A UDP rule for the same number likewise never displaces it.
4. **A removal can never close anything.** Removing the last rule for an SSH port brings the
   unconditional accept back (fail-open), so the only operation that can cost the operator their
   way in is an allow.
5. **Adding a rule to a port that already has rules only widens it**: the admitted set becomes the
   union. So the single dangerous shape is: a TCP allow for ONE port that is an SSH port, whose
   source is not "everyone", when no operator rule for that port exists yet.

The issue text and the plan describe the unsafe set as "a rule whose span covers an SSH port".
Item 3 shows a range cannot displace the fallback, so a range is never refused; the guard is
narrower than the sentence and exactly as wide as the mechanism (the plan's wording was refined
against the code, and the refinement is the reason this section exists).

"Everyone" is the source `0.0.0.0/0` and only that: it renders with no `saddr` clause, so it also
covers IPv6 because the table is in the `inet` family. `::/0` is an IPv6 restriction, not a second
spelling of everyone (`firewall_rule.rs`).

## The rule the guard enforces

Inputs: the new rule, the rules the host is actually running (read from the agent with
`ListRulesAsync` in the same handler, so the verdict reads the machine and not the panel's
memory), the SSH ports from `FirewallOptions` (`Firewall__SshPorts`, never from the request), and
the caller's address (`AllowPortCommand.IpAddress`, written by the controller from the connection
and never bindable from the request).

The allow is refused when ALL of these hold:
- the protocol is TCP and the rule names a single port (no upper bound);
- that port is one of the SSH ports;
- the host's rules contain no TCP single-port rule for that port (the listing leaves out the
  unconditional accept and a lone any-source rule, which are the same thing, so "none listed" means
  "everyone is admitted today");
- the new rule's source is not `0.0.0.0/0` and does not contain the caller's address.

If the caller's address is empty or unreadable the last condition cannot be evaluated; the guard
treats the caller as outside the range and refuses (fail closed: see below).

## Argument, by concern

1. *Is the verdict taken from the right facts?* The SSH ports come from the options the panel
   refuses to boot without; the rules come from the agent at decision time; the caller's address is
   stamped by the server. None is taken from the request body. A caller cannot name a different SSH
   port to slip a rule past the guard, nor claim another address.
2. *Fail closed or open?* The existing whitelist guard (`RemoveWhitelistEntryCommandHandler`) fails
   OPEN on an address it cannot parse, because it protects a convenience (a ban exemption) and must
   not block an edit for a session that may not exist. This guard protects reachability, and the
   cost asymmetry is the other way round: a wrongly refused allow costs a retry from another
   address, a wrongly permitted one can cost the server. So an unreadable caller is refused whenever
   the rule would narrow an SSH port at all. If the agent cannot list the rules the handler returns
   that failure and installs nothing: it cannot tell, and it does not guess.
3. *No override, and why that is not a trap.* A soft "confirm to proceed" would be the state before
   this change. The way out is mechanical: open the SSH port to a range that includes the address
   you are connected from (or `0.0.0.0/0`), or perform the change from an address inside the range.
   The refusal message says so.
4. *Time of check to time of use.* The listing and the allow are two agent calls. Another
   administrator may change the ruleset between them. The agent serialises its own mutations (a
   firewall lock) but the pair is not atomic. Consequence: a rule installed by someone else in the
   gap can make the guard's picture stale. One direction is safe: the guard saw no rule, refused,
   and a concurrent allow had meanwhile added one (a wrongly conservative refusal). The other is a
   residual hole: the guard saw rules for the port (so it permitted, because adding a rule only
   widens), and a concurrent deny by a second administrator removed them all before this allow
   landed, so this allow replaced the returned fallback. Closing it needs the listing and the
   allow to be one agent operation, which is a proto change and is not done here. It needs two
   administrators mutating the same SSH port within one request's latency; it is stated, not
   solved.
5. *Information disclosure.* The refusal text names no port, no ruleset and no address, only the
   fact. The SSH port that would have been lost is logged for the operator (English, not
   localized) and is not placed in the problem payload. The endpoint is administrator-only.
6. *Injection.* No new value reaches a template, path or shell: the policy compares parsed
   addresses and integers. The caller's address is parsed with the shared `CidrRule` (strict,
   IPv4-mapped unwrapped); no re-implementation of containment.
7. *Audit.* A refusal is journalled as a failed `FirewallRuleAllowed` entry carrying the rule's
   subject, so an administrator reviewing the journal sees an attempt that was stopped.

## Known limits, stated

- **The guard compares the BROWSER's address, not the SSH client's.** The panel can only know the
  address of the HTTP request. An operator who connects to the panel from one network and to SSH
  from another (a VPN split, a jump host, a different machine) will be refused when restricting SSH
  to the SSH network, and permitted when restricting it to the panel network even though SSH comes
  from elsewhere. It protects the common case (one workstation) and cannot protect the rest. The
  message and the docs say what it actually checks.
- **A host serving SSH on several ports** is protected per port, conservatively: restricting any
  one of them to a range that excludes the caller is refused, although the caller may be connected
  through another. The guard cannot know which port the session uses.
- **An explicit any-source rule for an SSH port is invisible to the listing** (the agent hides a
  block of exactly one any-source rule because it is byte-identical to the fallback). A narrower
  rule added next to it would not lock anybody out, but the guard sees "no rule listed" and
  refuses it. That is a false refusal in the safe direction.
- **Proxies.** Behind a reverse proxy the request address is whatever the forwarded-header pipeline
  yields (`ClientAddress.Of`). A misconfigured proxy makes the guard compare the proxy's address.
- **The panel port is not guarded**, because it cannot be closed (see above), not because it was
  forgotten.

## What a reviewer must check

- `backend/src/Maran.Modules/Firewall/Domain/Policies/FirewallLockoutPolicy.cs`: the five
  conditions above are exactly the code, nothing wider (a range, a UDP rule, a non-SSH port, a
  source containing the caller and `0.0.0.0/0` are all permitted) and nothing narrower (an IPv6
  caller against an IPv4 range is refused; the unreadable caller is refused).
- `AllowPortCommandHandler`: the guard runs BEFORE the agent allow and a refusal sends no allow; a
  listing failure sends no allow; the SSH ports come from `FirewallOptions`.
- That the claim "a range never replaces the fallback" still matches `ruleset_state.rs` `render`
  when this is reviewed. If `render` ever routes ranges into an SSH group the guard has a hole;
  the policy's tests name this dependency.
- That `0.0.0.0/0` is still the only spelling of "everyone" in `firewall_rule.rs`.
- The error code is registered with its kind (`Conflict`) in three languages and in the Sdk census.

## What the author could not verify

- Behaviour on a real host: the guard's tests use the agent double; the polygon suite
  (`firewall_on_a_real_host.rs`) was not run. The facts about `render` above were read in source,
  not observed in `nft list ruleset`.
- How the forwarded-header pipeline reports an IPv6 operator behind the supported nginx versions
  (the policy handles IPv4-mapped and IPv6 text; the pipeline's output for each was read, not
  measured end to end on a server).
- Whether any real deployment sits behind a proxy that makes the panel see a shared address, which
  would make the guard permit a rule that excludes the operator's real address.

## Honest ordering

The note is the first file of this change. The policy, the handler change, the three message
files, the Sdk census row, the tests, the dialog text and the operations doc follow it, and the
second review stays OUTSTANDING after all of them.
