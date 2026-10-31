# Threat note: `GET /api/v1/tasks/running/stream` (issue #274)

`rules/security.md` ("Sensitive change escalation") asks for a threat note when a change adds to the
authenticated surface. This change adds one streaming endpoint. The note is written before the code.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/274-task-stream-fanout` MUST NOT merge to `main` until a human
second reviewer has read the change against this note. The owner is told so at hand-off.

## What changes

Until now the shell read `GET /api/v1/tasks/running` once and then opened
`GET /api/v1/tasks/{id}/stream` for every running task (capped at eight in the SPA, uncapped on the
server). A busy host therefore cost every signed-in browser one held connection per task in flight.

The change adds one endpoint on the existing `TasksController`:

- `GET /api/v1/tasks/running/stream`, `[Authorize(AnyAuthenticated)]` (inherited from the
  controller), the `api` rate-limit policy (inherited), written by the shared Sdk
  `EventStreamWriter` like every other stream. It takes no parameter at all.
- It sends a `task` event for each running task the caller may see when it opens, a `task` event
  each time one of them changes revision (including the change that finishes it), and a `running`
  event carrying the ids of the running tasks the caller may see whenever that set changes. It ends
  only when the caller disconnects.

The shell opens exactly one of these per signed-in browser tab. The per-task stream stays, used only
by the task's live pane.

## What an attacker could do with it, and why it is safe

1. **Read another tenant's tasks (IDOR).** Every read the stream makes goes through
   `TasksDbContext.PanelTasks`, whose global query filter is the same one the list, the single read
   and the per-task stream use: an administrator sees every row, a customer only rows recorded
   against their own account with the `AccountOwner` audience. No `IgnoreQueryFilters` is added
   (`grep -n "IgnoreQueryFilters" backend/src/Maran.Modules/Tasks/Services/RunningTasksStreamService.cs`
   returns nothing). A finished task is re-read by id through the same filter, so a row that stopped
   being visible while it ran is not sent; its id simply leaves the `running` set. The integration
   test `RunningTasksStreamTests` opens the stream as a customer with running tasks of three
   audiences and asserts it receives its own and none of the others, with the administrator's
   stream as the positive control.
2. **Read a task's log as a customer.** Every frame is mapped with `includeLog` set from the
   subscriber's own role, the same decision the list and the per-task stream take, so a customer's
   frames carry an empty log.
3. **Exhaust the server with streams.** Each open stream holds one database poll per poll interval
   (500 ms default, `TaskStreamOptions`) and nothing on the host — no agent call, no file handle. It
   is rate-limited by the `api` policy on how fast it can be opened, like the per-task stream (whose
   remarks in `TasksController` say why no concurrency limiter is added). The change strictly
   reduces the load a normal browser puts on the server: one poll per tab instead of one per
   running task.
4. **Smuggle a frame through a task's subject or log.** Payloads go through `EventStreamFrame`,
   which serializes JSON rather than concatenating text, so a newline in a subject cannot end a frame.
5. **Keep a signed-out stream alive.** The bearer token is checked when the request is accepted, as
   for every stream; a token that expires later does not close an open stream, which is the
   existing behaviour of the per-task stream too. The SPA closes the stream when the shell unmounts
   (sign-out) and does not reopen it after a 401/403/404 refusal.

## What a reviewer must check

- That `RunningTasksStreamService` reads only `_dbContext.PanelTasks` and never bypasses the filter.
- That `includeLog` comes from `ICurrentUser.IsAdmin` on every frame.
- That `RunningTasksStreamTests` asserts both sides (own task present, the other tenant's and the
  administrator-only task absent) and that its administrator control receives all three.

## What the author could not verify

- Behaviour behind a real reverse proxy with buffering; the tests run on `TestServer` (the
  limitation `EventStreamWriter` already states for every stream).
- A token revoked while a stream is open keeps that stream until the browser drops it; this is
  shared with every existing stream and is not changed here.
