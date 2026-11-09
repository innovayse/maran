# File manager

The file manager lets a hosting customer, a sub-user with the file grant, or an administrator act on
the files in an account's home directory from the panel. This page describes what the code in the
tree does today. Anything that is declared but not finished is marked **Not yet implemented**.

Every file operation runs in the agent, in a child process that has dropped to the account's own user
id, and reaches its target by walking descriptors (`openat` with `O_NOFOLLOW`). Nothing the panel
checks replaces that walk: the panel validates a path to give a clear error early, the agent decides
containment.

## What is built, and what is not

| Part | State |
|---|---|
| Agent: list, search, read, write, create folder, move, copy, permissions, delete, trash (trash, list, restore, empty, expire) | Implemented |
| Agent: create archive, extract archive | **Not yet implemented.** Both answer `UNIMPLEMENTED`; issue #168 |
| Panel module `Maran.Modules.Files`: list, search, info, create, rename, move, copy, permissions, trash, restore, empty trash, delete, archive and extract commands, limits, audit | Implemented as commands and queries |
| Panel HTTP routes under `api/v1/files`: list (`entries`), `entries/info`, `search`, read-text and save-text (`content`), raw-body `upload`, streamed `download`, `options` | Implemented (`FilesBrowseController`, `FilesTransferController`, `FilesOptionsController`). The routes that change the tree (create, rename, move, copy, permissions, trash, delete, archive, extract) are **not yet implemented** |
| Daily trash-retention job that calls the agent's purge | **Not yet implemented** (the `Jobs` folder of the module is empty) |
| Single-page app: browser, editor, trash page, dialogs | Pages and components exist; they work end to end only once the routes above exist |

Because archives, the HTTP routes and the retention job are open, the archive and trash-expiry
sections below describe the intended limits and the code that is already there, not a feature a
customer can use yet.

## Who can use it

- The plan must include the file manager (`AccountFeature.Files`), and a sub-user needs the grant.
  One grant covers the whole manager: it does not separate deleting or emptying the trash from
  writing.
- A customer reaches only their own account. Another account's id answers **404**, never 403, and
  nothing is sent to the agent.
- A suspended account refuses customers and sub-users with a "suspended" error; an administrator may
  still act.
- An operator viewing as a customer may list, search and read info. Reading content, download, save
  and every change are refused in view-as.
- API keys do not reach the file manager.

## Browsing

- A directory is listed one page at a time. The agent filters hidden entries (names starting with
  `.`, off by default), orders them (name, size or modified time; directories always come first;
  descending is optional) and pages them. Default page 200 entries, at most 1000.
- A symlink is shown as a symlink with its own size, mode and time, never its target's. A FIFO,
  device or socket is listed and never opened.
- A name that is not valid UTF-8 or holds a control character is left out of the listing and counted
  as a skipped entry.
- A directory with more than 100,000 names is refused with `TooLarge`.
- Paths are relative to the home: at most 64 segments, 255 bytes per segment, 4096 bytes in all; no
  `..`, `.`, leading `/`, backslash, empty segment or control character.
- The name `.maran-trash` is reserved. Ordinary routes refuse a path that begins with it; the trash
  is reached only through the trash operations.

## Search

- Searches entry **names** only: a case-insensitive substring of 1 to 255 bytes, no pattern, no
  content.
- Depth below the starting folder: default 8, at most 16. Matches: default 100, at most 500.
- Never follows a symlink, never enters a symlinked folder or another filesystem, skips hidden
  entries unless asked.
- A search also stops after 200,000 entries examined. A stopped search is a success with a
  "truncated" flag, never an error: there may be more matches than shown.

## Reading and editing

- The agent will stream a file only up to 512 MiB (`max_bytes` of 0 or above 512 MiB is invalid). A
  file larger than the caller's `max_bytes` is refused with `TooLarge` before the first byte; a file
  that grows past it while being read ends the stream with the same error and the partial body must
  be discarded.
- The entry must be a regular file the account owns with a single hard link. A symlink, FIFO,
  device, socket or hardlinked file is refused, never read.
- The panel's own limits are configuration (`Files__*`, validated at start-up; shown to the SPA by
  `GET api/v1/files/options`):

  | Setting | Default | Notes |
  |---|---|---|
  | `Files__MaxEditBytes` | 1 MiB | Largest file the text editor opens. Must not exceed the download limit |
  | `Files__MaxDownloadBytes` | 256 MiB | Largest file a download serves |
  | `Files__MaxUploadBytes` | 100 MiB | Largest upload; at most 256 MiB, the agent's ceiling |

  The 512 MiB figure is therefore the agent's hard ceiling for any read; the editor and download are
  held lower by the panel.
- A file the editor cannot open (over the edit limit, a NUL byte, not valid UTF-8) is reported as
  "not editable" and should be downloaded instead.
- Saving keeps the file's mode (a new file gets 0644), writes atomically and carries the modification
  time the editor loaded, so a file changed meanwhile is refused as an edit conflict
  (`FilesEditConflict`, 409); so is a file the editor loaded that has since been removed. The check
  is not atomic with the write; a small window remains. The text is held in memory and limited to
  `Files__MaxEditBytes`, counted in UTF-8 bytes.
- The agent has no call that describes a single entry, so the panel finds one (for info, for the
  edit checks and for an upload's overwrite check) by reading its folder name-ordered, 1000 names a
  page, until the name is found: one call for a folder under 1000 entries, at most 100 for the
  largest folder the agent lists. The owner and group are shown as the numeric ids the agent reports,
  and a symlink's target is not shown, because the agent does not report it.

## Upload and download

- Upload: a raw `application/octet-stream` body with the path in the query, never multipart. The
  panel counts bytes itself and stops at the cap; `Content-Length` is only a hint. The agent writes
  a temporary file beside the target and renames it into place only after the stream ended cleanly,
  so a failed or cancelled upload leaves no partial file and an existing file untouched. Missing
  parent folders are created with mode 0755. An existing file is replaced only when the request
  says so (`overwrite`), and keeps its mode; a folder at the path is never replaced. A declared
  length over the limit is refused before a slot is taken or a byte is read, and the server's own
  body limit is set one byte past the cap so the refusal is a readable `FilesUploadTooLarge` and not a
  bare 413. A client that disconnects mid-upload is journalled as an aborted upload.
- Download: always `application/octet-stream` with `Content-Disposition: attachment` and
  `X-Content-Type-Options: nosniff`; there is no inline preview, so a customer's HTML or SVG is never
  rendered from the panel's origin. Every refusal (account, slot, a path that is not a regular file,
  a file over `Files__MaxDownloadBytes`) is a JSON problem document answered before any download
  header is set. The response declares the file's length; a stream that ends with another length
  closes the connection instead of finishing, and journals the download as aborted
  (`FileDownloadAborted`, a failure entry) instead of completed. A folder is downloaded by archiving it first.
- The bundled nginx configuration already has a dedicated location for `upload` and `download`:
  body limit rendered from `Files__MaxUploadBytes` (plus an allowance), no request or response
  buffering, 300 s body timeout and 900 s proxy timeouts. A second location gives archive, extract,
  copy, trash and delete 900 s timeouts.
- Transfers are limited by slots, refused at once and never queued: **3 per account** and **16 for
  the whole panel** by default (`Files__MaxConcurrentTransfersPerAccount`,
  `Files__MaxConcurrentTransfersGlobal`). A refusal reads "one of your transfers is still running"
  (account) or "the server is busy" (host).

## Create, rename, move, copy

- Create folder: a new folder has mode 0755. Existing names answer "already exists".
- Rename and move are a single `rename` that never replaces anything: an existing destination is
  "already exists". There is no copy-and-delete fallback, so a move never ends half done. A
  destination on another filesystem, a folder moved into itself, or a symlink at either path is
  refused. The home and the trash cannot be moved.
- Copy is staged inside the trash's staging area and renamed into place, so a failed copy leaves
  nothing at the destination. The tree is measured first and refused as a whole when it holds more
  than **1,000,000 entries** or **20 GiB** (`LimitReached`), is deeper than 128 levels, or holds
  anything other than regular files and folders (a symlink, FIFO, socket or device anywhere refuses
  the whole copy). Permission bits are kept; owner and times are not. A copy refuses to start when
  the disk is below the agent's free-space floor.
- Batch requests name at most `Files__MaxBatchItems` paths (default 200), checked before the first
  agent call. Items run in order, one failure does not stop the others, and each item is reported
  and audited on its own.

## Permissions

- A mode is one to three octal digits (0 to 777). setuid, setgid and the sticky bit are refused at
  the panel and again at the agent, never masked away.
- The change is made on an opened descriptor; a symlink as the path is refused and its target is
  never changed.
- A recursive change walks the tree the same way: symlinks inside it and entries the account does
  not own are skipped and counted, no folder on another filesystem is entered, and a tree deeper
  than 128 levels is refused as a whole. The result reports how many entries changed and how many
  were skipped.
- A recursive change carries two modes: `mode` for files and the optional `directoryMode` for
  folders (the agent's `directory_mode`), both validated the same way. When `directoryMode` is
  absent the panel gives folders the file mode with execute added wherever read is set, so a
  recursive `644` leaves folders at `755` and never makes them untraversable. A folder mode of
  zero is refused, because the agent reads zero as "same as the file mode". The dialog shows the
  folder-mode field for a recursive change of a folder, prefilled with that default; the prefill is
  advice and the panel stays the authority on what is accepted.

## Delete and trash

- The normal delete moves the entry into the trash: `<home>/.maran-trash`, a folder of the
  account's own, mode 0700. It is a rename inside the same filesystem, so it is instant whatever the
  size, and trashed data **still counts toward the account's disk quota**.
- Restore puts an entry back at its original path (or a path the caller gives), creating missing
  parent folders. A destination that exists is "already exists" and the entry stays in the trash.
- The listing shows the newest 10,000 entries. Anything in the trash folder that does not have the
  shape the agent writes is left out and never trusted, because the customer can edit their own
  trash.
- Retention: `Files__TrashRetentionDays`, default **30** (1 to 3650). The agent can remove entries
  older than a given number of days and also sweeps leftover copy staging older than one day; it
  keeps no schedule of its own. The panel is meant to call this once a day for every account,
  suspended ones included. **Not yet implemented:** no retention job exists in the tree, so
  nothing is expired automatically yet.
- Empty trash removes everything in it permanently; an empty trash is a success with zero removed.
- Permanent ("force") delete removes a file, link or whole tree without the trash. It must be
  confirmed. Symlinks are unlinked and never followed; a hardlinked file is unlinked by name only; a
  tree deeper than 128 levels is refused rather than half deleted. The home and the trash cannot be
  deleted.

## Archives

- Formats: `.zip` and `.tar.gz` (also `.tgz` on extraction). The format is chosen from the name; any
  other name is refused.
- The panel sends these extraction limits with every request (the agent also applies its own
  ceiling): `Files__ExtractMaxEntries` 100,000 members, `Files__ExtractMaxBytes` 2 GiB written,
  `Files__ExtractMaxRatio` 100 (expanded size to archive size, 100:1). Bytes are counted as they are
  written, not read from the archive's headers.
- Extraction is designed to refuse, as a whole, an archive holding a member it will not write (for
  example a path escaping the destination), reported as `ArchiveUnsafe`; reaching a limit is
  `LimitReached`. Both reach the customer as "this archive was refused: it is unsafe or exceeds the
  extraction limits".
- Creating an archive is bounded by `max_entries` and `max_source_bytes`.
- **Not yet implemented:** the agent answers `UNIMPLEMENTED` for both operations until issue #168, so
  the exact set of members extraction refuses cannot be documented from code yet.

## Concurrency inside the agent

Browse operations (list, search, read) are limited per account and for the host: **4 per account and
16 for the host**, taken without waiting. Past either bound the call fails at once with
`AgentSaturated` and the caller retries later; the panel shows it as "the server is busy". Bulk
operations (copy, force delete, empty trash, recursive permissions) have their own bound and refuse
the same way.

## Errors a customer sees

The panel never forwards the agent's text; it maps each failure to a sentence of its own (English,
Armenian, Russian) without paths or tool output.

| Agent code | Meaning | Customer sees |
|---|---|---|
| `TooLarge` | Directory over 100,000 names, or file over the read cap | "This file is larger than the download limit." |
| `QuotaExceeded` | Account's disk quota ended the operation (nothing left behind) | "There is not enough disk space left in your plan for this." |
| `BelowFreeSpaceFloor` | Host disk below the floor | Same quota sentence |
| `ArchiveUnsafe`, `LimitReached` | Archive refused, or a tree/extraction limit reached | "This archive was refused: it is unsafe or exceeds the extraction limits." |
| `AgentSaturated` | Agent's concurrency bound reached | "The server is busy with other transfers. Try again in a moment." |
| Panel transfer slots | Account or host transfer slot taken | "One of your transfers is still running..." / "The server is busy..." |
| Other | Anything else | "The file operation failed. Try again." |

Other messages: path not valid, reserved name, not found, already exists, not a folder, not
editable, upload over the limit, batch too large or empty, delete not confirmed, restore target
exists, folder into itself, unknown archive format, edit conflict, account suspended.

## Audit

- The panel writes one audit entry per item for every change and, once the routes exist, for upload,
  save and download, failures included. The subject is the account name plus the validated path,
  cleaned and capped. File content is never audited or logged. A probe of another account's id is
  journalled as a failure.
- The agent keeps its own journal, independent of the panel's database: one line per change with the
  account and outcome, **and no path**. A read of a file's contents (`ReadFile`) is also journalled
  there, again without the path, because it hands a customer's data out of the only root process.
  Listing and search disclose names only and write no agent line.

## Operator checklist

- Set `Files__*` in `panel.env`; a value that would disable a protection refuses the boot.
- After changing `Files__MaxUploadBytes`, re-render nginx so the proxy limit matches (the installer
  does this from the same variable).
- Trash occupies the customer's quota; a customer who "deleted" a lot sees no space back until the
  retention window passes or the trash is emptied.
- Second review of the privileged parts is outstanding (see the threat notes in
  `docs/superpowers/notes/2026-11-07-file-manager-*`).

## Where things are

- Contract: `proto/agent/v1/files.proto`.
- Agent: `agent/crates/ops/src/files`, `agent/crates/agent/src/services/files`.
- Panel: `backend/src/Maran.Modules/Files`.
- SPA: `frontend/src/pages/files`, `frontend/src/components/files`.
- Threat notes: `docs/superpowers/notes/2026-11-07-file-manager-browse-threat-note.md`,
  `docs/superpowers/notes/2026-11-07-file-manager-panel-threat-note.md`.
