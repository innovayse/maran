# Threat note: per-site log retention and the traffic summary (issue #152)

`rules/security.md` requires a threat note and a second reviewer for a change to the agent's privileged surface. This change adds two agent rpcs that act as root on the site-log tree: one **reads and parses** log files (`SummariseSiteTraffic`), one **deletes** rotated archives (`PruneSiteLogs`). It is written BEFORE the code; every statement is a requirement the code must meet, and where the code lands and differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (#331).** The work is done by agent sessions, which cannot obtain one. The branch MUST NOT merge to `main` until a human has read it. The owner is told at hand-off that this surface carries an outstanding review.

**What the author cannot verify**, stated up front: (a) behaviour on a host with SELinux enforcing (the new reads and unlinks happen in `var_log_t`; reasoned from policy, not exercised, as for the earlier site-log note); (b) logrotate's timing on the supported families (when exactly `.1` appears relative to the hourly pass; the design tolerates any timing but it was not measured on AlmaLinux 9/10 or Debian 13); (c) the behaviour of the inode-and-fingerprint cursor on filesystems that recycle inode numbers aggressively (reasoned, with the head fingerprint as the guard, not measured on XFS).

## What is added

1. `rpc SummariseSiteTraffic` (read-only, unary): given an account, a domain and an optional cursor, reads the site's live access log and, if the cursor names it, the previous rotation `.1`, from the cursor onward, and returns per-UTC-day counters (requests, body bytes, status-code counts, top request paths), a new cursor and a `gap` flag. Bounded per call (64 MiB read, 8 KiB per line, 10,000 distinct paths, 64 status codes).
2. `rpc PruneSiteLogs` (mutating, unary): given an account, a domain and `retain_rotations` (1 to 14), removes the site's rotated archives numbered above it.
3. Panel: a per-site `LogRetentionDays` column, traffic day rows and a cursor row in the `sites` schema, an hourly pass (summarise first, prune second) and two customer endpoints (read the summary, change the retention).

No new daemon, port, outbound call or dependency. Rotation itself is unchanged (`installer/logrotate/maran-sites` keeps `rotate 14` and stays the hard bound).

## Owner decisions applied (the plan's recommended option each time)

- **D2, retention range:** 1 to 14 days, floor 1, the customer can only shorten. The validated type is `LogRetention` in `agent-core`.
- **D4, history before the feature:** the agent reads the live file and `.1` only (about two days at first). No gzip dependency; no archive is ever decompressed.

## What an attacker could do with each surface, and why it is safe

### 1. A visitor to a customer's site aims text at the root parser

Every access-log line embeds text a stranger chose (the request target, the referer, the user agent). `SummariseSiteTraffic` runs as root and reads it.
- nginx escapes `"`, `\` and bytes under 0x20 or over 0x7E in logged variables as `\xNN`, so one request cannot become two lines or inject a field. The parser does not rely on this: it is a hand-written single-pass tokenizer with no regular expression (no backtracking blow-up), refuses any line over 8 KiB (counted as `unparsed`, never buffered whole), and takes the five fields it needs by position.
- It is never evaluated, logged verbatim, passed to a program or used as a path: the output is integers and strings that the panel stores and a browser renders as text. The query string is cut at the first `?` BEFORE the path is kept, because it is where tokens live. A path is capped at 512 bytes (longer is counted under "other").
- Memory is bounded by construction, not by trust: the distinct-path map stops growing at 10,000 entries and further new paths add to one "other" counter. A scanner generating a million unique URLs costs the agent a constant. The test feeds more than the cap and asserts both the count and the totals.
- Control characters that survive in a path (an operator's custom `log_format` or an nginx change) are replaced, not stored.

### 2. A customer, or another local user, plants a path so root follows it

- The files are in `/var/log/maran/sites/<account>/`, a tree no unprivileged uid can write or traverse (the 2026-09-09 site-logs note). The names are derived from the validated `AccountName` and `Domain` types, never from a caller string.
- Both rpcs open the account directory by descriptor and every file through `open_in_directory` with `O_NOFOLLOW`, then require a regular file, owner root and `nlink == 1`, i.e. the existing `follow_log` discipline, extracted so the tail and the summary share ONE function (rule of two, rules/rust.md). A symlink, FIFO, device or hard link is a typed error, not "no data".
- `PruneSiteLogs` lists the directory, then unlinks through `remove_file_in_directory` (unlinkat, name only) on the held descriptor after `stat_in_directory` (no-follow) has shown the entry is a regular file. It removes only an entry whose whole name is `<domain>.access.log.<digits>` or `<domain>.access.log.<digits>.gz` (same for `error`), with the number above the retained count and never 0. **It cannot reach another site's files:** the matcher requires the whole entry name to equal `<domain>.access.log.` followed only by digits (and optionally `.gz`). Another site's entries start with its own domain followed by `.access.log.` or `.error.log.`, so they match only if that domain equals this one; a look-alike such as `a.com.access` (a different site, files `a.com.access.access.log...`) fails at the digits. The unit test plants `a.com`, `a.com.access` and `a.com.access.log` as three sites in one directory and asserts each prune touches only its own files. A file that does not match is left alone and is not an error (the account directory may hold an operator's own file).
- Never removes the live file, `.0`, or a directory; never follows into a subdirectory. A directory or a symlink whose name matches is left alone, not unlinked.
- The directory listing is read through `/proc/self/fd/<n>` of the already-pinned descriptor, so the listing and the unlinks describe the same inode. Even if a listing named something the pinned directory does not hold, the unlink is by name through the descriptor and the matcher has already refused anything but the exact pattern.

### 3. A customer makes the panel (and root) do unbounded work

- The customer's read endpoint reads PostgreSQL only. **A customer request never reaches the agent**, so there is nothing for a customer to amplify; the hourly pass is the only caller and runs one site at a time.
- Each summarise call stops after 64 MiB and returns a cursor at the stopping point, so a very busy site is caught up over several passes and progress is guaranteed; a tarpit that appends faster than the pass reads makes the site "catching up", never a hang.
- The agent holds a host-wide counting semaphore of 2 for the summarise rpc, taken without waiting and refused (`TrafficBusy`, a system-failure-class answer) when full, the same shape as `cron::run_now_slots` (rules/rust.md "What this agent serialises"). It is not a lock row: it orders nothing and adds no wait-for edge. It is process-local and says nothing about a second agent.
- The work runs inside `run_blocking` (`spawn_blocking`), read-only, with the cancellation class "Stop" stated in the rpc doc (a dropped client cannot cancel the blocking read, but the read is bounded by the 64 MiB budget, so it ends on its own). `PruneSiteLogs` is "Run to completion" (deleting is idempotent and has no unsafe midpoint).

### 4. Tenant isolation (IDOR)

- `GET /api/v1/sites/{id}/traffic` resolves the site through the Sites query filter; another tenant's site id is 404, never 403. The traffic rows are entities whose required relationship to `Site` puts them inside `TenantScopeCensus` (checked, not assumed: `TenantScopeTests` must pass without an exemption).
- The hourly pass has no principal; it pairs each agent answer with the SITE IT ASKED ABOUT. The agent response carries no account or site identifier the panel reads, so a bug or a hostile agent answer cannot write one tenant's figures into another's rows. The pass reads sites across all accounts with `IgnoreQueryFilters()` and each call carries its reason; the count in rules/README.md rises accordingly.
- Visitor addresses are never stored: the tokenizer skips the client address field, and no response field carries it. The cursor fingerprint is 64 bits of a hash of the first bytes of the file (at most 64), which begin with a visitor address, so it is a one-way digest, not the bytes.

### 5. The retention setting as a tool against the operator

A customer can shorten what the operator keeps for incident work on a shared host. The range is 1 to 14 days, shorter than the installer's 14 only on that customer's own site's files, and the pass writes an audit entry (`SiteLogsPruned`, system actor) whenever it removes files and an entry on every change (`SiteLogRetentionChanged`, actor and address). The agent also writes its own audit line for every `PruneSiteLogs` call (account and site, validated types only). The shipped default stays 14, so no site loses anything it has today until its owner asks.

### 6. The incremental cursor

The cursor is `(file id, offset)` where the file id is `device:inode:fingerprint`, the fingerprint being a hash of the first `min(64, offset)` bytes of the file the cursor names. A wrong cursor can lose or double-count lines but cannot read anything but the two named files. Double counting is prevented by the panel committing the day deltas and the new cursor in ONE transaction: if the agent call succeeds and the database write fails, the next pass starts from the old cursor and re-reads. A cursor naming a file that is neither live nor `.1` sets `gap` (the lost span is not guessed; the agent then reads the live file from its start and not `.1`, which may be partly counted already); the panel keeps the figure and records the gap time, and the UI says "may be incomplete since <date>".

## Known limits (not hidden)

- Top paths over several days are the sum of per-day top lists with a 50-entry stored cap, so ranks beyond the top few are approximate; the UI says "top paths" and the doc says so.
- A status code beyond the 64th distinct one in a day is counted in that day's requests but not in its status list; a path beyond the 10,000th distinct one is counted in `other_path_requests`.
- History before the first pass is only what is still in the live file and `.1` (about two days).
- `error.log` is not summarised, only the access log.
- The access log is read in the `combined` format; a line that does not parse is counted in `unparsed_lines` and contributes to no figure.

## What a reviewer must check

1. `open_site_log` is the ONE function both rpcs and the tail use, and every refusal in it is still a typed error.
2. The prune name matcher (`rotated_log_name`) accepts nothing but the exact pattern, with the three-site test above and planted look-alikes (`a.com.access.log.1.bak`, `a.com.access.log.x`, a subdirectory named `a.com.access.log.2`), plus an accept-side control (a real `.3.gz` is removed).
3. Summarise happens before prune in every pass (panel side), so a retention drop cannot delete a file the cursor still needs.
4. The 64 MiB, 8 KiB, 10,000 and 64 bounds are enforced in the code that reads (`traffic_budget`, `read_traffic_file`, `traffic_tally`), not asserted in a comment.
5. The semaphore is taken before any file is opened and is released by drop on every path.
6. `TenantScopeTests` and the IDOR test pass with no new exemption (panel side).
7. The three things the author could not verify, above.
