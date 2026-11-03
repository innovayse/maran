# Cron run-now: concurrency bound and audit ordering: threat note (issues #333, #335)

**Second review: OUTSTANDING.** This changes the one panel endpoint that makes the root agent run a
customer's program (`POST /api/v1/cron-entries/{entryId}/runs`) and the audit record of it, so
rules/security.md "Sensitive change escalation" applies. It may land on a branch and MUST NOT merge to
`main` until a second reviewer has read it.

## What changes

- **#333, a bound on runs in flight, enforced in the panel before the agent is called.** `CronRunSlots`
  (a singleton in `Maran.Modules.Cron/Services`) counts the runs this panel process has handed to the agent
  and not yet seen end, per account and in total. `RunCronEntryCommandHandler` takes a slot after the
  account is resolved and found not suspended, and before `IAgentCronClient.RunEntryAsync`; a `using` over
  the returned `CronRunLease` gives it back on every way out (answer, refusal, exception, cancellation).
  Past the account's bound the answer is `CronRunAccountLimitReached` (409); past the panel-wide bound it
  is `CronRunServerBusy` (503). The bounds are `Cron__Runs__MaxConcurrentPerAccount` (default 2) and
  `Cron__Runs__MaxConcurrentGlobal` (default 16), validated at start-up (`[Range]`, `ValidateOnStart`),
  documented in `.env.example` and `installer/panel.env.example`. The agent's own semaphore is a separate
  change by another author; this note covers only the panel half.
- **#335, every run is audited even when the caller disconnects.** After the agent answers, the audit entry
  is written BEFORE the history row, and neither write takes the request's cancellation token. A cancelled
  request that was inside the agent call leaves a failure entry and the cancellation then propagates. The
  refusal entries (account not found, suspended, over a bound, agent refusal) are written with no token
  too. Separately, `CronRun` stores output with NUL characters removed (`CronRunOutputPolicy`), because
  PostgreSQL's text type refuses U+0000 and the insert used to fail, taking the row and, before the
  reorder, the audit entry with it.

## What an attacker could try, and why it fails

| Attempt | Why it fails |
|---|---|
| A customer clicks "run" in a loop to hold every agent worker thread. | At most `MaxConcurrentPerAccount` runs of that account are in the agent at once; the rest are answered by the panel and never queue on the root process. |
| Many accounts do it together. | `MaxConcurrentGlobal` bounds the total below the agent's pool, so cron runs alone cannot starve the rest of the panel's agent calls. |
| A customer probes how busy the server is. | The account bound is checked first, so an account over its own limit sees only its own code; `CronRunServerBusy` is shown only to an account under its limit. Neither reveals another account. |
| A program prints a NUL to erase its own history and audit entry. | The NUL is dropped before the row is built, and the audit entry is written first in any case. |
| The caller closes the connection mid-run to avoid an audit entry. | The entry is written with no token after the agent answers; a disconnect while the agent still holds the run leaves a failure entry. |
| A script forces the panel to leak slots (cancel, throw). | The lease is disposed in a `using`; tests cover the agent refusing, throwing, and the caller cancelling. |

## What a reviewer must check (OUTSTANDING)

1. That no path from `TryAcquire` to the agent call can skip the `using`, and that no second code path
   starts a run without a slot (`/usr/bin/grep -rn "RunEntryAsync" backend/src --include=*.cs` outside `obj/` and `bin/`
   lists the contract, the client, the Host's resilience decorator and the handler's one call site; the
   pattern was a plain literal and matched the handler, which serves as its control).
2. That the panel-wide counter is correct under a restart: it starts at zero, which is true only while a
   restart ends every call the old process was waiting on. A run the agent kept going after the panel died
   is not counted, so right after a restart the agent can briefly hold more than the bound.
   Both slot tables are process-local and nothing persists them: the panel's `CronRunSlots` lives in the
   panel process's memory and the agent's semaphore table (`run_now_slots.rs`) in the agent's. After a
   panel restart the panel counts from zero; after an agent restart the agent's counts are full again and
   a run its previous process left going is not counted until it ends. With a second panel instance, each
   instance holds its own `CronRunSlots`, so the per-account and panel-wide bounds are per instance (N
   instances admit up to N times the figures) and only the agent's single table bounds the host as a
   whole, answering `AGENT_SATURATED` past it. The bounds therefore assume one panel instance per agent;
   running two is outside what this note argues.
3. Whether a 503 for the global bound (the closest `ErrorType`; the enum has no rate-limited kind) should be
   a 429 with `Retry-After`, which would need a new `ErrorType` value and mapper arm in `SharedKernel`.
4. That writing the audit entry before the row is right when the row then fails: the journal says the run
   happened, which is true, and the history misses it, which is the smaller gap.

## What the author could not verify

No build or test was run (the owner's order is code first, one gate pass later), so the compile state, the
new tests, the resx key parity test and the status census are all unrun. The behaviour under a real
disconnect through Kestrel (as opposed to a cancelled token handed to the handler) was not exercised, and the
claim that PostgreSQL refuses U+0000 in a `text` column is from its documentation, not measured here.
