# Clock synchronisation reading (issue #150) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change adds an rpc through which the panel makes root start one more program, and it changes what
the two-factor sign-in answers. Written before the code, per that rule; the branch may land on
`dev` but must not merge to `main` until a second reviewer has read this note and checked the items
under "What a reviewer must check".

## The surface

1. A new rpc, `MonitorService.GetClockSync()`, served by the agent as root. It takes no input and
   runs, with an argv array and no shell:

   ```
   /usr/bin/timedatectl show --property=NTP --property=NTPSynchronized
   ```

   The path is an `AdapterBinary` from the `distro` crate, on the process allow-list beside
   `quotaon`. The run is in `CallClass::Query`, so it is bounded by that class's deadline and
   output caps like every other monitoring read.
2. The panel's `MetricsSampler` asks it once per sampling round and records the answer against a
   new alert row (`AlertKind.ClockUnsynchronized`); the operator is mailed through the shared
   `OperatorAlertMailer` once an episode opens and once it closes.
3. `GET /api/v1/monitoring/clock`, an administrator-only read (`AuthorizationPolicies.AdminOnly`), asks
   the agent live and answers the state with a localized sentence.
4. A rejected two-factor code — at sign-in (`VerifyTwoFactor`), at enrolment
   (`ConfirmTotpEnrolment`) and when switching the factor off (`DisableTotp`) — answers a different
   code, `InvalidTwoFactorCodeClockUnsynchronizedUnauthorized`,
   when the latest monitoring round found the clock not synchronised. The read is the Sdk window
   `IClockSyncStatus`, implemented by Monitoring from its own alert row: no agent call happens on a
   sign-in.

## What an attacker could try, and why it does not work

1. **Make root run something else.** The rpc carries no field at all; the program and every
   argument are constants. `show` is timedatectl's reporting verb — nothing in this change can
   reach `set-time`, `set-ntp` or `set-timezone`, and no argument is built from anything a caller
   sent (rules/security.md items 3 and 12).
2. **Use sign-in failures to hammer the root agent.** A rejected code reads one database row in
   the panel's own schema; it never calls the agent. The agent is asked once per sampling round,
   whatever the sign-in traffic.
3. **Learn something from the new rejection code.** It is returned only after the password has
   been verified and the user found `Active` — the same point at which the existing
   `InvalidTwoFactorCodeUnauthorized` is returned — so it tells nobody without the password
   anything. What it tells the password holder is one host-wide fact: the server's clock is not
   synchronised. That discloses no account, no row and no secret. It is still counted by the
   brute-force detector and journalled as `LoginFailed`, exactly as the old answer was, so it is
   not a cheaper guess.
4. **Turn the hint into a bypass.** The code is still rejected. Nothing widens the TOTP window, and
   the replay window is unchanged; the hint changes only the sentence a person reads.
5. **Fake a clean clock to suppress the alert.** Only root on the host can change what timedated
   answers, and root on the host is already past every boundary the panel has.

## What could go wrong, and what was chosen

- **A reading that could not be taken** — timedated unreachable over the system bus, a deadline, an
  answer without both properties — is a typed error (`ClockStateUnavailable`, `Bound`,
  `ServiceManagerUnavailable`), never "not synchronised". The sampler passes `null`, which advances
  nothing and resolves nothing, like every other unanswered reading.
- **The kernel flag is an estimate, not a measurement.** It clears when the kernel's maximum-error
  estimate passes sixteen seconds. A clock off by a few seconds while still flagged synchronised is
  not seen. Measuring an offset would need an outbound call to a time server; rules/security.md
  item 10 forbids new outbound calls, so it is out of scope by rule, not by oversight.
- **The hint can be stale.** It reflects the latest monitoring round, so it can lag by one
  sampling interval, or longer while the agent is down. A stale "not synchronised" only changes a
  sentence on a code that was rejected anyway.

## What a reviewer must check

- `agent/crates/ops/src/monitor/get_clock_sync.rs`: the argv is constant, the run is
  `CallClass::Query`, a non-zero exit or an unreadable answer is an error.
- `agent/crates/distro/src/{debian,rhel}/*_services.rs`: `/usr/bin/timedatectl` on both families
  (the polygon `binary_paths_on_a_real_host` suite asserts it exists).
- `VerifyTwoFactorCommandHandler`, `ConfirmTotpEnrolmentCommandHandler` and `DisableTotpCommandHandler`
  (through `TwoFactorCodeRejection`): the new code is reached
  only on the branch the old one was, after the password check, and the failure is still counted
  and journalled.

## What the author could not verify

- That timedated answers over the system bus from inside the agent's systemd sandbox on every
  supported family. It was not run on a real host in this change; if it does not, the reading is
  an error and nothing alerts, which is the safe direction.
