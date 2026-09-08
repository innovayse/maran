# Threat note — a bounded shutdown path for the root daemon

**Written BEFORE the change** (rules/security.md: a note written afterwards is a justification for
a choice already made). Surface: `maran-agent`'s process lifecycle — the only root process on the
server — and its systemd unit. Second reviewer: **OUTSTANDING**.

## What is being changed

1. `agent/crates/agent/src/shutdown.rs` (new): a future that completes on the first `SIGTERM` or
   `SIGINT`, and a bounded drain wrapper.
2. `agent/crates/agent/src/server.rs`: `serve_with_incoming` becomes
   `serve_with_incoming_shutdown`, with the drain bounded by a deadline; the socket is unlinked
   after the server returns.
3. `installer/systemd/maran-agent.service`: `TimeoutStopSec=` stated explicitly, above the drain
   budget, instead of inheriting systemd's invisible 90 s default.

Nothing about privilege, authentication, peer credentials, path containment or the command set
changes. No rpc is added, removed or widened. `PeerGuard` is untouched: every service keeps the
same interceptor it has today.

## What the change is FOR (measured, not assumed)

With no handler installed, `SIGTERM`'s default action terminates the process instantly — measured
at 3.5 ms, exit 143, mid-syscall, with no drain. `systemctl stop` during `create_account` can
therefore land between `useradd --create-home` and the step that opens the home to the web
server's group, leaving a user whose sites answer 403 and which the agent deliberately refuses to
repair. A bounded drain removes that whole class for every operation shorter than the budget.

## What an attacker could do with this surface

- **Denial of service by holding the drain open.** The panel uid is the only uid the socket admits
  (`PeerGuard` + `SO_PEERCRED`), and it can open a `TailSiteLog` stream that never ends. If the
  drain were unbounded, one such stream would stall every `systemctl stop`/`restart` until
  `TimeoutStopSec`, i.e. would block panel upgrades. **This is why the drain is bounded and not
  graceful-until-done**; the bound is the mitigation, and it is the reason the naive form of this
  change was rejected.
- **Delaying a stop the operator wants.** After the deadline the process exits regardless, so the
  worst an in-flight request buys is the budget, once. `TimeoutStopSec` in the unit stays above the
  budget so systemd's own SIGKILL remains the outer backstop and a wedged agent is still stoppable.
- **A signal an unprivileged local user could send.** None: only root and the process's own uid
  (root) may signal it. The handler adds no new sender.
- **A window opened by the socket unlink.** The unlink happens after the server has stopped
  accepting, and `serve()` already removes a stale socket before binding, so a crash that skips the
  unlink is the state the code already handles. No caller can be tricked into connecting to a
  replacement: the directory is `0750 root:panel`, systemd-managed.

## What this change does NOT close, stated so a reviewer does not assume it does

- **A restore killed in its database phase stays unrecoverable.** A drain does not survive a power
  cut, an OOM kill, or `TimeoutStopSec` expiring on a multi-hour restore, and a restore is measured
  in hours. Worse, and independent of this change: the rollback dumps that are the only copy of the
  pre-restore data live under `/var/lib/maran-scratch`, which the unit's `ExecStartPre` deletes on
  the next start. That is an owner-level design question (a restore journal), it is reported and
  argued in `.superpowers/sdd/agent-shutdown-report.md`, and **it is deliberately not fixed here**:
  the two candidate fixes trade data loss against leaving a plaintext copy of a customer's
  databases unattended on disk, which is exactly the exposure that `ExecStartPre` line exists to
  prevent, and that trade is the owner's to make.
- The orphaned `<id>.tar.gz.partial` a killed backup leaves is not swept by anything.

## What the author could NOT verify

- The behaviour under a real systemd `systemctl stop` on a real server. Measured here: SIGTERM to
  a real `maran-agent` process on a host, and `docker stop` against the polygon image. systemd's
  cgroup kill of the agent's forked `setuid` children is asserted from `systemd.kill(5)` semantics
  (`KillMode=control-group`, left at its default) and not observed on a booted machine.
- That no consumer of the agent depends on the current instant-death behaviour. Nothing in
  `backend/` was read for this; `backend/` was outside the author's writable scope.

## Addendum, measured after the change (the note above is unedited)

The denial-of-service paragraph turned out to be understated, and the measurement is recorded here
rather than folded into the prediction it corrects.

`agent/crates/agent/tests/shutdown_signal.rs` was first written holding a bare `UnixStream` across
the stop. It FAILED: the agent did not exit within ten seconds. A peer that opens the socket and
never completes the HTTP/2 handshake gives hyper no connection to send a GOAWAY on, so it holds the
drain open for the **whole** `DRAIN_BUDGET` — no request, no stream, no work in flight, just an
open fd. An established gRPC connection (what the panel actually is) is closed at once; that case
is the assertion the suite now carries, and it passes.

Consequences for a reviewer to weigh:
- The budget is not merely a bound on real work; it is the ONLY thing bounding a peer that does
  nothing at all. Raising `DRAIN_BUDGET` raises this directly.
- The exposure is limited to uids the socket admits — the panel uid and root — so it is a self-DoS
  on panel restarts, not a path for an unprivileged local account. `PeerGuard` and the
  `0750 root:panel` runtime directory are what make that true, and neither was changed.
- A reviewer should decide whether a half-open connection ought to be dropped immediately at
  shutdown rather than waited on. It was NOT done here: it means reaching past tonic into the
  connection layer of the root daemon, which is more surface than the problem justifies while the
  budget bounds it.
