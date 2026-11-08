# Threat note: file manager, panel side (issues #169 and #170, part of #141)

`rules/security.md` requires a threat note, written first, for a change that opens a new way for an
authenticated caller to read, write and delete customer files through a root process, and that adds a
long-lived streaming surface behind the reverse proxy. The agent half (descriptor-walked browse,
mutations, archives, trash) is the subject of the notes written with #166, #167 and #168. This note
covers only what the PANEL adds. It is written BEFORE the handlers: every statement is a requirement the
code must meet; where the code lands and differs, the code is wrong or this note is amended in the same
change.

**Second review: OUTSTANDING - must not merge to main.** The work is carried out by agent sessions, which
cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human reviewer has read it
(rules/security.md). The owner is told at hand-off that this surface carries an outstanding review.
Nothing here discharges it.

## What is added

1. Module `Maran.Modules.Files` (manifest id `files`, capability `Files`, tier Included), no database table.
2. Plan feature `AccountFeature.Files` (bit 1024), so a plan can omit the file manager; an expand-only data
   migration sets the bit on every stored plan.
3. Routes under `api/v1/files`: list, search, info, read-text, save-text, upload (raw body), download
   (stream), create file or folder, rename, move, copy, permissions, trash, list trash, restore, empty
   trash, force delete, archive, extract, options.
4. `IAgentFilesClient` grows the streaming and file-manager methods.
5. A rate-limit bucket `files` (per hosting account) and a transfer-slot counter (`FilesTransferSlots`).
6. An nginx location for upload and download with its own body limit, unbuffered request body and longer
   timeouts, rendered from the same configured number the panel reads.
7. A scheduled trash-retention job driving the agent's purge for every account.
8. SPA: a file manager page, editor, upload queue, trash view, dialogs, batch actions.

Not added: no new listening port, daemon, outbound call or agent rpc beyond what #166-#168 define; no
API-key scope (`ApiKeyCaller` is refused by every role policy and sees no account); no third-party
editor, uploader or file-manager library (owner decision on #141).

## 1. Who can reach which account's files (IDOR and tenancy)

- Every request carries `accountId`. A handler resolves it ONLY through `FilesAccountGate`, which asks
  `IAccountDirectory.FindAsync` (tenant-filtered, fail-closed): another account's id is null, the answer is
  `AccountNotFound` with `ErrorType.NotFound` (404, never 403), nothing is sent to the agent, and a failure
  entry naming the probed id is journalled. The agent is addressed by the snapshot's `Username`, never by a
  name taken from the request.
- The plan feature and a sub-user's grant are asked by `[RequiresAccountFeature(AccountFeature.Files)]` on
  every controller, before a handler runs. One grant covers the whole file manager: this change does NOT
  distinguish deleting or emptying the trash from writing (owner decision O8, default applied). A reviewer
  should confirm that is acceptable for sub-users.
- **Suspended account:** a customer or sub-user caller is refused with `FilesAccountSuspended`
  (`ErrorType.Conflict`); an administrator may still act. The check lives in ONE service,
  `FilesAccountGate`, which every handler calls first; a test must walk every handler with a suspended
  account and an account the caller does not own.
- **View-as:** an operator viewing as a customer holds a customer token with an `act` claim. List, search
  and info are `ViewAsAccess.Read`. Reading content, download, save and every mutation are
  `ViewAsAccess.Refused`: copying a customer's file out, or changing it, is not "looking".
- **IDOR test:** customer A with customer B's `accountId` gets 404 on EVERY route (a route walk with a
  positive control that A's own `accountId` is accepted), and a mutating route additionally proves the
  agent fake received no call.

## 2. Paths

- The panel validates a path at the boundary with ONE rule, `FilesPathRule`: relative only; no leading `/`;
  no `\`; no control or line-separating character; no unpaired surrogate; no empty segment (other than the
  whole-home path); no segment `..` or `.`; at most 64 segments, 255 bytes per segment and 4096 bytes in all
  (UTF-8 bytes, as the agent counts). `FilesFileNameRule` is the same rule for one name (a rename, an
  archive name). A value failing a rule is `ErrorType.Validation`, and "traversal never reaches the agent"
  is a test: a fake agent client records calls and a corpus of hostile paths posted to every route that
  takes a path leaves it with zero calls.
- **This is the panel being a good citizen, not the control.** The control is the agent's descriptor walk
  (rules/security.md item 2). The panel never parses a path to decide containment and never touches the
  disk. A reviewer must not accept "the validator stops traversal" as the answer to a traversal finding.
- The trash directory name (`.maran-trash`, the agent's value) is reserved: the ordinary routes refuse a
  path whose first segment is that name (`FilesPathReserved`), and the trash is reached only through the
  trash routes.
- **Batch:** at most `Files:MaxBatchItems` paths (default 200) per request, decided before the first agent
  call; each item runs in order, a failure of one does not stop the others and is reported per item, and
  each item is audited on its own (`FilesBatchRunner`).
- A permission mode is octal digits, nine bits only; setuid, setgid and sticky are refused at the boundary
  and again by the agent. A recursive change is explicit and is one audit entry.

## 3. Streaming and size caps

- **Upload** is a raw `application/octet-stream` body, path in the query, never multipart (multipart buffers
  to disk in the API process, which would hold customer bytes outside the home). The action sets Kestrel's
  per-request body limit from `Files:MaxUploadBytes`, counts bytes itself as it forwards them to the
  agent's client stream, and aborts at the cap plus one byte with `FilesUploadTooLarge`. `Content-Length`
  is a hint, never trusted. A client that disconnects cancels the gRPC call; the agent writes a temp file
  and renames only on a completed stream. The agent also enforces its own ceiling (256 MiB), and
  `FilesOptionsValidator` refuses a configured value above it.
- **Download** is a server stream from the agent's read with `max_bytes = Files:MaxDownloadBytes`: the agent
  refuses a larger file before streaming, so a refused download is a clean JSON problem and not a truncated
  body. The response is ALWAYS `application/octet-stream` with `Content-Disposition: attachment` and a name
  cleaned by `FilesDownloadNamePolicy`, plus `X-Content-Type-Options: nosniff`. **A customer's HTML or SVG
  must never be served inline from the panel's origin: that is stored XSS in an administrator's session.**
  There is no preview route; the only route returning file text is `read-text`, a JSON string rendered as
  text.
- **Edit** is capped at `Files:MaxEditBytes` (1 MiB). A file over the cap, with a NUL byte or not valid
  UTF-8 is `FilesNotEditable`. Save preserves the existing mode and writes through the same atomic path as
  upload. A save carries the modification instant the editor loaded (`FilesEditConflict`); the compare is
  not atomic with the write, so a window remains and the doc says so.
- **Concurrency of transfers:** `FilesTransferSlots` bounds uploads and downloads per account and for the
  host; a transfer past either bound is refused at once (`FilesAccountBusy` / `FilesServerBusy`), never
  queued. A slot is held for the life of the stream and released by disposal, including on client abort.
- Extraction limits (entries, bytes, ratio) are configuration sent to the agent with each request and
  enforced there; the panel shows them and never claims to enforce them.
- **nginx:** a dedicated location for the upload and download routes sets the body limit from the configured
  upload size, `proxy_request_buffering off`, `proxy_buffering off` and timeouts for a slow link; the number
  is rendered from the same `panel.env` variable the panel reads. The check must read `nginx -T`, not the
  template (rules/testing.md). Unbuffered request bodies keep an upstream connection open for a slow
  client: bounded by the transfer slots and nginx's own `client_body_timeout`.

## 4. Audit

- Every mutating operation, and upload, save and download, writes one entry per item through
  `FilesAuditJournal` (only a module's own journal builds an `AuditEntry`). Actions are constants in
  `Maran.Sdk/Contracts/FileAuditActions.cs`. Failures are journalled as reliably as successes, including
  the cross-tenant probe, which also covers reads (`FileAccessRefused`).
- **Subject** is the account name plus the validated relative path, cleaned of control characters and
  capped. **File content is never audited, never logged, never in an error text.** Whether the path itself
  belongs in an append-only journal is owner decision O6; the default applied is yes, because an operator
  cannot investigate a deletion without it.
- Upload and download write an entry at stream OPEN and a second at completion with the byte count the
  panel counted; an aborted transfer writes the `...Aborted` entry. A transfer that dies leaves the open
  entry and no completion, which is the honest record.

## 5. Output and injection

- Names and paths shown in the SPA are rendered as text, never `v-html`.
- Nothing from the agent's error text reaches a customer: `FilesAgentErrorTranslator` re-codes every agent
  failure to a sentence the module owns and logs only the agent's code and the subject. Paths and tool
  output stay out of customer-facing errors (rules/security.md item 8).

## 6. Unattended work: trash retention

- The retention handler asks the agent to purge expired trash for each account. It runs with no principal,
  so it reads accounts through `IAccountDirectory.ListAsync` (already a sanctioned filter bypass: no new
  `IgnoreQueryFilters` call, the count in rules/README.md does not change). It purges suspended accounts too
  (the trash occupies quota) and writes nothing else. A failed purge of one account is logged and the next
  proceeds; the run reports counts, not paths.

## What a reviewer must check

1. Every route answers 404 for another account (walk test), and `FilesAccountGate` is the first call in
   every handler.
2. No handler touches `System.IO` or spawns a process; the module references no other module; the manifest
   declares only `Files`.
3. The upload action: limit set from options, byte counting independent of `Content-Length`, cancellation
   reaches the agent call, no retry after the first byte (the resilience pipeline must not replay a
   consumed stream).
4. The download response headers on EVERY path, including the error paths (a problem document must not
   carry `Content-Disposition`).
5. The nginx location against `nginx -T` output, not against the template.
6. View-as declarations; sub-user grants (one grant for the whole manager).
7. Batch: one audit entry per item, partial failure reported, cap enforced before the first agent call.
8. The migration `AddFilesToPlanFeatures` only sets a bit.

## What the author could not verify

- The agent's behaviour: whether its own caps hold, whether a cancelled client stream really leaves nothing
  behind (that is #166-#168's evidence).
- The behaviour of a real browser with a 100 MiB upload behind the real reverse proxy on both supported
  families (the end-to-end work in #171 must measure it).
- Whether a single sub-user grant for the whole file manager is what the owner wants.
- This note was written in the same session as the foundation code, in the same sitting as the first
  handlers' contract; it is the author's argument, not a second pair of eyes.
