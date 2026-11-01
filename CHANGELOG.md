# Changelog

Notable changes, newest first. Dates are the merge date. Until 1.0 the format is prose rather
than a strict category list: what changed and why it matters to somebody running a server.

## Unreleased

**A plan caps the CPU, memory and processes of each account on it (#133).** A plan now states three runtime ceilings beside its disk quota and counts: CPU as a share of one core (`cpuQuotaPercent`, 200 is two cores), memory in megabytes (`memoryMaxMb`) and processes (`maxProcesses`), each either a figure or "No limit". `POST /api/v1/plans`, `PUT /api/v1/plans/{id}` and every plan read carry them as `{"kind": "limited" | "unlimited", "maximum": n}`; an absent ceiling, a limited zero and an unlimited ceiling carrying a figure are refused with 400. The server agent writes them onto the account's systemd slice (`user-<uid>.slice`) through a new rpc, `SetAccountResourceLimits`: when an account is created, when a plan's ceilings change (to every account on it), and when an account moves to a plan with other ceilings. A lowered ceiling is applied rather than refused: CPU is throttled, the next process past the cap is refused, and memory above a lowered cap is reclaimed from the account and can end the account's own processes. Plans that existed before the update are unlimited on all three, which is what the server enforced for them (three pairs of columns on `accounts.Plans`, applied by the update on its own). What the slice reaches: the account's SSH and SFTP sessions and what runs under them; its PHP workers are still bounded only by the per-pool worker limit, and its cron jobs are outside the slice on the Debian family — both are stated in the threat note and left for a later change. The panel and the server agent move to contract revision 8 together, so both must be updated at once. Threat note: `docs/superpowers/notes/2026-11-01-account-runtime-limits-threat-note.md`.

**Monitoring and backup alerts take one shared mail path (#290).** Journalling an operator alert, finding the alert address, requesting the mail and journalling "Mail skipped: no SMTP settings" when no address is configured used to be written twice, once in the monitoring alert evaluator and once in the backup alert mailer. Both now call one implementation in the module SDK (`Maran.Sdk/Alerts/OperatorAlertMailer`), writing through each module's own audit journal, so the mails and the journal entries are exactly what they were. Nothing changes for an operator.

**Every CI job has a time limit, and a new push to a pull request cancels the run it replaces (#289, first part).** A hung job used to idle until GitHub's six-hour default or a manual cancel; each job now has a `timeout-minutes` sized from its measured duration on recent successful `dev` runs plus headroom (backend `ci` 60, `ci-pg13` 50, the agent polygon 40), with the measurement written beside it. Runs on pull requests are grouped per pull request and cancelled when superseded; pushes to `dev` and `main` are never cancelled. No behaviour of the panel changed.

**A scheduled backup that fails, cannot reach its destination or misses its hour now mails the administrator (#134, first part).** Until now each of these was silent: a failed nightly run showed only as a red row, a schedule pointing at a destination the panel could not use logged a line and started nothing, and a panel that was down at 03:00 caught up later without saying so. The scheduled sweep now sends one alert for each, to the same address and through the same outgoing-mail settings as the monitoring alerts, in English, Russian or Armenian, and journals it as "Alert raised" (with "Mail skipped: no SMTP settings" beside it when no address is configured). A run counts as missed when it starts more than 30 minutes after its named hour; a panel that was off for several nights sends one alert naming the first missed hour. Manual backups do not mail. Verifying each archive after it is written, which needs a new server-agent operation, is still to come. Details in `docs/backups/alerts.md`.

**The server refuses new work before its disk is full, and says so in advance (#148).** The server agent now keeps a free-space floor, 1 GiB unless you set `MARAN_AGENT_FREE_SPACE_FLOOR_MIB` in `/etc/maran/agent.env` (re-running the installer keeps your value). While less than that is available on the filesystem an operation would write to, creating an account, taking or restoring a backup and accepting an upload are refused before anything is written, and the refusal reads "The server is nearly out of disk space…" in your language, in the response and as the failure of a backup or restore task (`AgentBelowFreeSpaceFloor`, HTTP 503). Suspending, deleting and every read keep working, so space can be freed from the panel. A new alert mails the operator when the root filesystem has had less than twice the floor available for ten checks, so it arrives before the refusals begin. A backup whose description file cannot be written in full after the archive was published now removes both, so a disk that fills during a backup leaves no half-written backup behind. The panel and the server agent move to contract revision 7 together, so both must be updated at once. Details in `docs/monitoring/disk-figures.md`; threat note `docs/superpowers/notes/2026-11-01-free-space-floor-threat-note.md`.

**The panel itself can be backed up and restored onto a clean server (#129).** Two new root-only commands: `maran panel-backup --passphrase-file FILE [--out DIR]` writes one encrypted archive (gpg, AES-256, mode 0600) holding the panel database, the encryption and signing keys from `panel.env`, the licence and the whole of `/etc/maran` and `/var/lib/maran`; `maran panel-restore --passphrase-file FILE --replace-this-panel ARCHIVE` turns a fresh install into that panel again — database in one transaction, the two keys, the licence, then this release's migrations — so the same administrators sign in and every stored secret decrypts. A wrong passphrase or a damaged archive is refused before anything changes, and a refused restore starts the panel again as it was. Scheduling and S3 upload of these archives are not in this release. How to use them: `docs/operations/panel-disaster-recovery.md`. Threat note: `docs/superpowers/notes/2026-11-01-panel-disaster-recovery-threat-note.md`.

**The disk figure matches `df`, and the space kept for the system is shown on its own (#77).** The panel used to call capacity minus available space "in use", so on an ext4 server it read about 5% higher than `df`'s `Used` (12.7 GiB against 7.7 GiB on a 97.9 GiB disk): that 5% is the reserve the filesystem keeps for root, which no hosting account can write. The dashboard and the monitoring screen now show three figures in one stacked bar, "In use" (exactly `df`'s `Used`), "Reserved for the system" (0 on xfs) and "Available" (`df`'s `Avail`, what the accounts can still use), and the three always add up to the disk's size. `GET /api/v1/monitoring/metrics` and the dashboard's `resources` gain `diskReservedBytes` and `diskAvailableBytes`, and `diskUsedBytes` now means `df`'s `Used`. The disk chart plots the same "in use" figure; readings taken before the update stored the old figure and show as a gap until they age out of the seven-day history (a new nullable column, `DiskInUseBytes`, applied by the update on its own). The disk-full alert still fires when the accounts run out of room, reserve included. The panel and the server agent move to contract revision 6 together, so both must be updated at once. The definitions are in `docs/monitoring/disk-figures.md`.

**The SPA's sites store is split by concern (#280).** `stores/sites.ts` held the site list, detail, mutations, the host's PHP versions and the live log tail in 503 lines; it now keeps the list, detail, form bounds and mutations, while the installed PHP versions and their install (`phpVersions.ts`) and a site's log tail (`siteLogs.ts`) each have their own store. No store reaches into another, and no behaviour of the panel changed.

**Form fields are named after what you choose, not after the identifier sent for it (#80).** The new-account form's plan picker is labelled "Plan" ("Тариф", "Սակագին") and its required message reads "Choose a plan."; in Armenian the form now uses the same word for a plan as the plans page, the navigation and the change-plan dialog, where it had said "Փաթեթ". The account pickers of the create-site, database, SFTP, FTPS and backup forms keep their "Account" label, their locale keys no longer carry the request property's name (`fields.account`, not `fields.accountId`), and two Armenian prompts gained the article the other three had. `maran structure` now refuses a form field, placeholder, filter or table column whose key ends in `Id` or whose English text ends in "ID".

**A site carries at most 99 aliases, the names one certificate can cover (#275).** A site used to accept any number of aliases, so its vhost, its claimed names and the certificate the panel would request for it grew with whatever a request carried. One certificate may name at most 100 hosts (the Let's Encrypt limit), and the site's own domain is one of them, so creating a site with more than 99 aliases is now refused with 400 `SiteAliasLimitExceeded`, in English, Russian or Armenian; the server agent refuses the same on its side for every operation that renders a site's configuration, while switching off or winding down an existing site still works whatever it carries. The bound is the server's and the new `GET /api/v1/sites/limits` reports it (`{"maximumAliases": 99}`); the create-site form shows how many aliases are listed against it, says so as soon as the list is too long, and does not let you create the site until it fits. If the limit cannot be read, the form does not guess and the server decides.

**The header's running-tasks badge holds one connection, however much work is in flight (#274).** The shell used to read `GET /api/v1/tasks/running` and then open a live stream for every running task, so a busy host cost each signed-in browser one held connection per task. It now opens one stream, `GET /api/v1/tasks/running/stream`, which sends every running task you may see when it opens, each change to one of them (including the change that finishes it), and the set of running task ids whenever it changes; who may see which task is unchanged, and a customer's frames still carry no log. The stream is opened once per signed-in shell, closed on sign-out, reopened when you switch language so task names follow it, and reconnected after a dropped connection with a wait that doubles from one second up to thirty; a refusal is not retried. A task's live view keeps its own stream, because the task it shows may have finished long ago. `GET /api/v1/tasks/running` stays for API clients. Threat note: `docs/superpowers/notes/2026-10-31-running-tasks-stream-threat-note.md`.

**A failed task's reason is written by the panel, in your language, for every code (#272).** A background task that failed now carries its reason as a sentence the backend resolves from the stored code each time the task is read: `GET /api/v1/tasks`, `GET /api/v1/tasks/running`, `GET /api/v1/tasks/{id}` and every frame of `GET /api/v1/tasks/{id}/stream` send `failure: {"code": …, "message": …}` (or `null`) in place of `errorCode`, with the message in the reader's own language, so switching language re-reads an old failure in the new one and two people watching one task each read it in theirs. It is the same sentence the refusal itself carries as an error response, so a code raised anywhere in the panel, the certificate authority's and the backup outcomes' included, explains itself; a code no installed module describes reads as a general sentence with the code beneath it. The interface no longer keeps its own table of 51 task failure sentences, and `maran structure` no longer checks it; a backend test now asks the assembled panel for a sentence for every error code it ships, in English, Russian and Armenian.

**Every list holds up at more than a thousand accounts, and CI measures it (#196, #162).** The per-account disk view (`GET /api/v1/monitoring/accounts-disk`) is now paged like every other list (`page`, `pageSize`, `search` on the user name, `sort` by `username` or `quotaBytes`), and the server agent measures only the accounts on the page instead of walking every home directory on each request; the panel and the agent move to contract revision 5 together, so both must be updated at once. Sorting by used space is gone, because only the page on screen is measured, and the over-quota notice speaks for that page. The integration suite now seeds 1 200 accounts with their sites, certificates, databases, logins, backups, audit entries and tasks, and fails when any shape of any list (first page, last page, search, each filter, each sort) takes longer than 200 ms at the 95th percentile; the slowest measured was 25 ms on PostgreSQL 16 and 67 ms on 13. Two indexes arrive, `IX_Certificates_SiteId` and `IX_Backups_StartedAt`, and the update applies them on its own. Every list that stays unpaged, and why, is in `docs/performance/list-indexes.md`.

**The SPA's sign-in store is split by concern (#270).** `stores/auth.ts` held every account flow in 498 lines; it now keeps only the session (sign-in, second-factor code, renewal, sign-out), and first-run setup (`setup.ts`), the second factor's enrolment (`twoFactor.ts`), the signed-in devices (`sessions.ts`) and the e-mailed password links (`passwordLinks.ts`) each have their own store with their own message. The root component dismisses all of their messages on every screen change, as before, and clears the module catalogue and the devices list when the session ends, so no store reaches into another any more. No behaviour of the panel changed.

**The "no placeholder for a fast answer" browser tests hold under load (#265).** These tests timed the page by the wall clock, so on a busy machine a stubbed read that "answered in 50 ms" arrived much later, and the breadcrumb's placeholder, which had honestly waited its 150 ms, was reported as a flash. They now drive the page's own clock: the read is held until the test releases it after 50 ms of page time, then time runs well past the delay, and any placeholder seen at any point fails the test. A new control proves the recorder does see a placeholder once a wait reaches 150 ms. One licence-gating test also looked for a "Sites" link anywhere on the page, and the dashboard's Sites tile sometimes matched too; it now looks only in the sidebar. No behaviour of the panel changed.

**Failed tasks explain every refusal, and the panel confirms every create (#222).** A certificate task that failed because its site or account no longer existed, because the domain already had a certificate, or because the server agent refused (unreachable, out of date, a package mirror that refused or could not be reached, and the rest of the agent's refusals) now reads as a sentence in your language with the code beneath it, as do PHP installs whose outcome the server never reported; before, those showed only the generic sentence. `maran structure` now also follows codes that reach a task through a variable in the handler that closes it, and requires a sentence for every refusal the server agent can report. Banning an address and adding a never-banned range are now confirmed by a toast, like every other create, and every confirmation sits in the same corner. A task's progress is drawn by one rule in the list and in its live view: a failed task shows how far it got and no bar. In Armenian, the database grant screen has one name, "Իրավունքների վերականգնում", in the sidebar, on its button and as its title.

**The shell follows a language switch and the current page more closely (#221).** Switching the interface language live now renames the modules in the sidebar and the breadcrumb at once, and two quick switches can no longer leave the names in the language switched away from: only the latest read of the module catalogue is kept. A refusal shown on one screen no longer follows you to the next: a refused invitation or reset link followed by "Back to sign in" opens a clean sign-in screen, and the same holds between the signed-in devices and two-factor screens. A section's row (Accounts) is lit on every one of its pages, Plans included, in the collapsed rail as well as the sidebar, and its highlight now runs the full width of the row with the submenu toggle inside it, like its children's. The current row is kept in view when the module list arrives after the page, not only on a navigation, and moves the list only as far as needed. The content panel reserves its scrollbar's lane on every page, so moving between a short page and a long one (the dashboard and Firewall) no longer shifts the content sideways. The role under the user name, which was cut to "Ադմինիստրա…", and the focus ring left by a mouse click were already fixed earlier and are now covered in English, Russian and Armenian.

**Small inconsistencies from the full review are gone (#218).** Every confirmation's button now names its action — "Delete backup", "Remove task", "Lift ban", "Remove exemption", "Remove certificate", "End session", "Turn off two-step verification" — instead of "Yes, do it" or "Yes, delete it", and the confirmation component requires that label, so a new one cannot ship without it. The firewall's rules screen is laid out like its bans and never-banned screens: a refusal first, the form in a card of its own, then the list under its own heading ("Rules in force"); the firewall placeholders describe the field ("From 1 to 65535", "Empty for one port") instead of showing a value such as 8080 that looked already typed. Taking a backup now empties the account picker, as every other create form does after an accepted create. The way back from a site, an account, a plan and the upgrade screen is one link style, underlined in the accent colour. Every helper under a form field is the same size (the PHP version picker's was a step smaller), and the invitation sentence on an account is body text rather than a step larger. Points of the review already fixed earlier (one face for the tab labels, the second-factor placeholder, the dates with times on the firewall lists, the "Manual" backup reason, the upgrade screen's module name, the quiet signed-out console) are now held by end-to-end checks.

**On a phone, tables are cards, the theme has one labelled menu, and a dialog's hint is never under its buttons (#217).** Below 640 px every list (accounts, plans, sites, databases, SFTP and FTPS logins, scheduled tasks, firewall rules, bans and whitelist, backups and their destinations, the audit journal, background tasks, signed-in devices) draws each row as a card: its name as the title, its actions at the top right where they are always in view, and every other column as a line labelled with the column's heading. Nothing scrolls sideways any more, and a row's actions can no longer be scrolled off screen. Sorting stays available through a "Sort by" select of the sortable columns with a button that flips the order, and the loading placeholders take the card shape. From 640 px up the tables are unchanged. The theme is now one control, the mode's icon and name ("Theme: Light" to a screen reader), opening a menu of Light, Dark and System; it sits in the app bar on the desktop and in the navigation drawer on a phone, and replaces the old switch and the unlabelled sun in the drawer. System follows the operating system's light or dark setting and keeps following it while the panel is open; the choice is remembered as before. In a dialog, focusing a field now also brings the text under it into view, so the hint under the account deletion's confirmation field is no longer hidden beneath the dialog's buttons.

**The sidebar, the page eyebrows and the breadcrumb tell one story (#209, #72).** Every sidebar entry is now named for the screen it opens, and that name is the page's own title: "Users and access" is "Signed-in devices", "Notifications" is "Outgoing mail", and "Tasks" is "Background tasks", so it can no longer be confused with "Scheduled tasks" (cron). The security policy has its own entry under System, and PHP versions sit in the Sites submenu; both were reachable only by typing their address. The firewall's submenu (#196) follows the same rule: Rules, Bans and Never banned, the titles of the three pages. The small line over each page title is the sidebar group the page sits in, derived from the navigation instead of written per page, so Background tasks reads Overview, the audit journal System, Backups Security and Accounts Hosting, as the sidebar draws them. The breadcrumb goes one level further: a submenu page ends on its own name (Security / Backups / Schedule), and a site or an account ends on its domain or name, with a placeholder while it loads instead of the id from the address. No crumb is a URL segment any more. Only the current submenu child is highlighted, and the collapsed rail and the jump-to palette keep the expanded sidebar's order (both already fixed earlier and now covered by specs).

**The audit journal, background tasks, signed-in devices, firewall bans and the firewall whitelist are paged on the server (#196).** `GET /api/v1/audit`, `GET /api/v1/tasks`, `GET /api/v1/sessions`, `GET /api/v1/firewall/bans` and `GET /api/v1/firewall/whitelist` now answer one page (`{"items":[…],"total":…,"page":…,"pageSize":…}`) instead of a bare list, with `page`, `pageSize` (10, 25, 50 or 100), `search` and `sort` in the query string; the audit endpoint's `limit` parameter is gone. The journal searches the actor, the subject and the address, sorts by `occurredAt` (newest first by default), `actor` or `action`, and filters by `action`, `outcome` (`succeeded` or `failed`) and a `from`/`to` window; `GET /api/v1/audit/actions` lists the actions it holds, named in the caller's language, for the action filter. It has no account filter, because an entry records no account. The tasks list searches the subject, sorts by `startedAt` (newest first by default) or `subject`, and filters by `accountId`, `status`, `kind` and a `from`/`to` window on the start; who may see which task is unchanged. Three companions replace what screens used to read off the whole list: `GET /api/v1/tasks/running` (every running task, for the header badge), `GET /api/v1/tasks/failed-count` and `GET /api/v1/tasks/kinds` (the kinds the caller can see, for the kind filter). A window whose start is later than its end is refused (`AuditDateRangeInverted`, `TaskDateRangeInverted`). Devices search the address and the browser name and sort by `issuedAt` or `expiresAt`; the list still takes no user, because whose devices they are comes from the caller's own sign-in, and a `userId` in the address is ignored. Bans search the address and sort by `address`, `bannedAt` or `expiresAt`; the whitelist searches the range and its note and sorts by `cidr` or `createdAt`. The firewall is now three pages linked from one sidebar entry, rules (`/firewall`), bans (`/firewall/bans`) and never banned (`/firewall/whitelist`); the rules stay one unpaged list, because the confirmation before removing a rule has to see every rule the host runs. The dashboard counts the bans in force and the failed tasks, and reads its five newest audit entries, with queries of their own instead of reading whole lists. The audit screen no longer shows a count of failed entries among the hundred it held; the outcome filter shows them instead. Two indexes arrive with it, `IX_AuditEvents_Action` (the action filter and its list of choices) and `IX_PanelTasks_Status` (the running tasks, the failed count and the status filter); the update applies them on its own.

**A signed-in person can change their own password from the profile (#232).** Until now the only way to change a password was the e-mail reset link. `/profile` now has a password card for administrators and customers alike: current password, new password, and the new one again. It calls the new `POST /api/v1/auth/change-password`, which checks the current password, holds the new one to the same minimum length the security policy sets for resets and invitations, and on success ends every other signed-in device while keeping the one in use. A wrong current password answers 400 `CurrentPasswordIncorrect` and counts towards the account lockout exactly as a failed sign-in does, the endpoint shares the sign-in rate limit, and every change or refusal is written to the audit log as "Password changed" without either password. Threat note: `docs/superpowers/notes/2026-10-31-change-password-threat-note.md`.

**A refused sign-in burst answers as a problem, and the backend suite measures the shipped limits (#260).** The sixth sign-in attempt from one address within five minutes is refused with 429, code `HostRateLimited` and a `Retry-After` header, as before; its body now arrives as `application/problem+json` like every other refusal, instead of `application/json`. The limiter itself was never broken: a test host started from a shell that had sourced `scripts/dev` took the development `.env` value of 50 attempts, because environment variables outrank `appsettings.json`. `maran test backend` now runs the suite without the values `.env` applied, so a workstation measures the same panel CI does, and a new integration test checks the sixth attempt at the shipped settings.

**A request value the panel cannot read is refused with a code and a translated message (#245).** An address such as `?page=abc`, `?pageSize=x` or `?status=7` (a status that does not exist) used to be refused with a 400 that carried no `code` and an English developer sentence. Every endpoint now answers it like any other refused input: code `RequestParameterInvalid` and a message in the caller's language (English, Russian or Armenian) naming the value that could not be read. Refusals that already had their own code, such as a search text that is too long or an unknown sort, keep it.

**A PHP install that fails because of a package mirror says so (#251).** Installing a PHP version answered 500 for every failure of the package manager, as if this server had broken, even when the repository mirror had refused the download or could not be reached at all. The server agent now reads the failed run back and tells the cases apart: a mirror that answered with an HTTP error is **502** `AgentPackageSourceRefused`, a mirror nobody could reach is **503** `AgentPackageSourceUnreachable`, and a failure on the server itself (a package no configured repository carries, a held package lock, a failing install script) stays **500** `AgentSystemFailure`. Any other failure the agent reports during an install now keeps its own status as well, so an agent that cannot do the work answers 503 instead of 500. The panel and the server agent move to contract revision 4 together, so both must be updated at once.

**The dashboard no longer says "Nothing needs attention" about something it could not check (#248).** When a module could not be read, its part of the landing page used to arrive as an empty list or a zero: a failed backups count read as "0 backups failed", and the attention notice said nothing needed attention. Every dashboard section now says whether it was read: `GET /api/v1/dashboard` sends each one as `{"state":"loaded","data":…}` or `{"state":"unavailable"}`, the counts included (they used to be `{"state":"counted","count":…}`). The attention notice names each source it could not read ("Backups could not be checked") and shows the all-clear only when every source was read. The live reading, the services and the latest activity each say so in their own card instead of disappearing or showing "Nothing has been recorded yet.". For a customer every section is `null`, the list sections included (they used to be empty lists).

**The SPA's dependencies carry no known high-severity advisory (#250).** `vue-i18n` moves from 11.4.12 to 11.4.16 (with its `@intlify/*` packages) and `vite` from 8.3.0 to 8.3.5, both pinned exactly; `brace-expansion`, `source-map-js` and `postcss-selector-parser`, which arrive through other tools, are lifted to their fixed releases in the lock file. `npm audit` in `frontend/` now reports 0 vulnerabilities. Vite and the editor launcher it carries are development-server code and never ship in the built panel; `vue-i18n` does.

**Databases, backups, plans, certificates and file-transfer logins are paged on the server.** These lists now load one page at a time, like Accounts and Sites: 25 rows by default, with 10, 50 or 100 to choose from, search, sort and filters held in the address. Backups gain status, kind and date-range filters and a notice when an account's latest finished backup failed (`GET /api/v1/backups/attention`); `GET /api/v1/certificates` gains `status` and `expiring` filters. Plan pickers search the server (`GET /api/v1/plans/choices`) instead of loading every plan, an account page names its plan through `GET /api/v1/plans/{id}`, and `GET /api/v1/accounts/plans` is gone. File transfer is two pages under one sidebar entry, SFTP (`/file-transfer/sftp`) and FTPS (`/file-transfer/ftps`), each with its own table and create form. The dashboard counts databases, expiring certificates and failed backups through each module's own count instead of reading a list that now holds only one page.

**Screens hold their shape while they load.** Every screen that reads from the server used to show a spinner, or nothing at all (PHP versions, security policy, SMTP settings, the firewall tables), until the answer came. A table now keeps its real header over placeholder rows drawn from its own columns, a form or a detail list keeps its real labels over placeholder fields, and the figure cards keep their labels over placeholder figures, so the content lands in the space already held for it instead of pushing the page around. The placeholders appear only once a wait has lasted 150 ms, so a quick answer shows none; they shimmer gently, and stand still when the system asks for reduced motion. A screen reader hears "Loading…" once and then "Loaded", not each placeholder. A failed read always shows its error, never a placeholder that waits forever, including when the panel could not be reached at all, which now says so where some screens used to show an empty card. Other fixes on the way: a refused delete, drop, sign-out or repair is reported above the list instead of replacing it, an account address the panel does not know shows "Account not found", the running-tasks badge no longer makes the top bar taller, and the "Days left" figure on a certificate no longer moves its label.

**Every label reads in sentence case.** Field labels, status badges, table headers, page titles and their eyebrows, sidebar group labels, card and figure labels, the terms in detail lists, the command palette, the sign-in headings, the not-found page and the task pane were set in capitals by the stylesheet, so a task read `COMPLETED` / `ԱՎԱՐՏՎԱԾ` instead of `Completed` / `Ավարտված`. They now show their text as translated, without the wide letter-spacing capitals needed. Acronyms such as TCP, SFTP and PHP keep their capitals. `maran structure` now fails when `uppercase`, `tracking-caps` or `tracking-wide*` appears in the SPA's source.

**A refusal from outside the panel is no longer reported as a server error.** When the certificate authority refused this server's registration, an order, a validation or the certificate download, or when the mail server refused a test message, the panel answered 500, as if it had broken itself. It now answers **502** for an expected refusal from an outside party (`AcmeAccountRejected`, `AcmeOrderRejected`, `AcmeValidationFailed`, `AcmeCertificateUnreadable`, `AcmeChallengeTokenInvalid`, `MailDeliveryFailed`), and **503** with its own code, `MailServerUnreachable`, when no mail server answered at all, so a test message tells "your provider said no" apart from "nothing answered, try again later". A request that cannot run in the server's current state answers **409**: turning FTPS on before a TLS certificate exists (`FtpsCertificateMissing`) and running a backup with no destination configured (`BackupDestinationNotConfigured`). A server agent that lacks the feature asked for answers **503** (`AgentNotImplemented`). A real fault inside the panel, such as failing to write a challenge file (`AcmeChallengeWriteFailed`), stays 500. The other messages shown on screen are unchanged; `MailDeliveryFailed` now says only that the mail server refused the message.

**A signed-out page load is quiet.** Opening the panel while signed out used to log a 401 for `POST /api/v1/auth/refresh` in the browser console on every page load. When the browser sends no refresh cookie at all, the panel now answers 204 with no body: there is no session, and nothing was refused. A refresh cookie that is sent and is invalid, expired or already used is still refused with 401 and cleared, exactly as before, and the 204 tells a caller nothing it did not already know, since it depends only on what the caller's own request carried.

**An address that names no site shows "not found".** `/sites/php`, or any address whose last part is not a site id, used to show a bare "Not Found". It now opens the panel's "Page not found" screen without asking the server anything. A site id the server does not know shows the site's own "Site not found" state, and any other failure still shows the server's message as an error.

**The audit log names a dropped database in full.** "Database dropped" recorded only the part of the name the customer typed (`shop`) instead of the database's real name (`acct_shop`). It now records the full name, for a successful drop and for a refused one.

**A failed task explains four more failures.** A backup or restore the panel stopped following before the server answered, a scheduled backup that ended without a result, and a task cut short by a panel restart now explain themselves in your language instead of showing their code. `maran structure` now fails when a task failure code the backend names has no reason in the task translations, or when a translated reason names a code that no longer exists.

**`Database__Database=x maran dev` works in a shell that already ran `source scripts/dev`.** `scripts/dev` remembered only which names `.env` had set, so on the next source it put `.env`'s database back over the one typed on the command line, and `maran dev` used the wrong database without a word. It now remembers the value `.env` applied for each name, and a different current value is treated as the caller's override. Editing `.env` and sourcing again still picks the edit up. A shell that sourced the previous version should be reopened once. `maran env selftest` proves both directions.

**The two-step verification page shows whether it is on.** Settings → Two-step verification used to offer both "Set up" and "Turn off" to everyone, whatever their state. It now shows one thing at a time: "Set up" when two-step verification is off; while setting up, a QR code to scan with an authenticator app beside the secret and the code field; and "Two-step verification is on" with "Turn off" once it is on. Recovery codes stay on screen right after turning it on. The QR code is drawn in the browser from the setup link the panel returns, so the secret is not sent anywhere. An administrator who must set up two-step verification before using the panel sees only "Set up". For integrations: starting or confirming a setup when two-step verification is already on now answers 409 `TwoFactorAlreadyEnabled`, and turning it off (or completing a two-step sign-in) when it is not on answers 409 `TwoFactorNotEnabled`. These were 403 `TwoFactorAlreadyEnabledForbidden` and `TwoFactorNotEnabledForbidden`.

**The accounts and sites lists are paged on the server.** Accounts and Sites now load one page at a time instead of every row: 25 rows by default, with 10, 50 or 100 to choose from, and pages to step through at the foot of the table. Search, sort, filters and the page live in the address, so a list can be shared, reloaded and walked back with the browser's Back button. Search matches the account's name or primary domain, and the site's domain or its owner's name; filters (plan and status for accounts, account, backend, PHP version and status for sites) open in a sheet on a phone and a dialog on a wider screen, and each applied filter shows as a removable chip. The owner of a database, backup, SFTP or FTPS login is now named by the server, so it is right however many accounts the server holds, and every account picker searches the server as you type instead of offering the first hundred accounts. For integrations: `GET /api/v1/accounts` and `GET /api/v1/sites` now answer `{ items, total, page, pageSize }` and take `page`, `pageSize` (10, 25, 50 or 100), `search`, `sort` (`name`, `-createdAt`, …) and their filters (`planId`, `status` for accounts; `accountId`, `backendType`, `phpVersion`, `status` for sites), with enum values spelled as the JSON spells them (`suspended`, `reverseProxy`). An empty or blank `search` means no search. A page past the end answers no rows with the true total. A bad bound answers 400 with `ListPageOutOfRange`, `ListPageSizeNotOffered`, `ListSearchTooLong`, `ListSearchInvalid`, `ListSortUnknown`, `AccountStatusUnknown`, `SiteStatusUnknown`, `SiteBackendTypeUnknown` or `PhpVersionInvalidFormat`. `GET /api/v1/accounts/choices` (administrators) pages account ids and names for pickers, and the database, backup, SFTP and FTPS answers carry `ownerName`. On the landing page a count the panel could not take now reads "Unavailable" instead of 0, and so does the sites list's count of disabled sites; `GET /api/v1/dashboard` answers each count as `{ state, count }`, where `state` is `counted` or `unavailable`. Sorting sites by status puts serving sites before suspended ones.

**The SSL tab is three parts instead of one file.** `SiteSslTab.vue` had grown to 474 lines. The installed-certificate card and the install-your-own-certificate form are now components of their own (`components/ssl/`), fed by props and answering with events, while the tab keeps every call to the panel. Nothing on screen changes, and the SSL specs pass unchanged.

**`maran dev` no longer runs on a stale polygon image.** It used to warn that `maran-polygon-ubuntu24:latest` was "STILL NOT CURRENT" and start anyway, so the stack ran the previous tree's system stand-ins — an old `systemctl` stand-in that reported every unknown unit as active is what made the FTPS card look broken. `maran dev` and `maran dev --selfcheck` now refuse before starting anything when the image is missing or not built from this tree, and print the fix: `maran polygon build ubuntu24`, a new command that rebuilds a polygon image with its source fingerprint recorded and then checks it is current. `maran polygon selftest` proves the refusal and its inverse against a stubbed `docker`.

**An account page shows what the account uses.** The account page now has a Resources card listing the account's sites, databases, SFTP logins, FTPS logins, cron entries and backups, each beside its plan's limit with a bar that warns as the limit fills. Backups have no plan limit, so they show a count and "no limit". When the server cannot read an account's crontab, the cron row says "Unavailable" instead of showing zero. A row appears only for the parts of the panel installed on this server. Each row links to that list narrowed to the account (`/sites?account=<id>`, and likewise for databases, file transfer, backups and cron); the narrowed list says "Only <account>" with a "Show all" button. Customers see the same card on their own account page, beside their plan's limits. For integrations: `GET /api/v1/accounts/{id}/resource-summary` (administrators) and `GET /api/v1/accounts/me/resource-summary` (the customer's own account) answer `{ accountId, resources: [{ kind, state, used, limitKind, limit }] }`, where `state` is `counted` or `unavailable` and `limitKind` is `limited` or `unlimited`; `GET /api/v1/sites`, `/databases`, `/sftp-users`, `/ftp-users` and `/backups` take an optional `?accountId=`, and a customer naming another account gets an empty list.

**An account's plan and owner email can be changed.** The account page now shows the account's plan and its owner email, and "Change plan" and "Change owner email" open a dialog for each. A plan whose disk quota is smaller than what the account already stores is refused with a sentence saying so, and nothing is changed; otherwise the new plan's quota is applied to the account on the server at once. The other limits of a plan (sites, databases, logins, cron entries) are not reconciled: an account above a new limit keeps what it has and cannot create more. The owner email is the address the owner's login is mailed at, so the invitation and every password reset go to the new address from then on; an address another login already uses is refused. Both changes are administrator-only and recorded in the audit log ("Account plan changed", "Account owner email changed"; the addresses themselves are not logged). For integrations: `PUT /api/v1/accounts/{id}/plan` with `{ "planId" }` and `PUT /api/v1/accounts/{id}/owner-email` with `{ "ownerEmail" }`, each answering the account as it now stands; refusals are 409 `AccountPlanQuotaBelowUsage` and 409 `EmailTaken`, and `GET /api/v1/accounts/{id}` now also answers `planDisplayName` and `ownerEmail`. Renaming an account's primary domain is not offered yet.

**A plan that accounts are on cannot be deleted by mistake.** Plans → Delete on a plan that still has accounts used to say the panel would refuse and keep an enabled Delete button that answered with that refusal. The button is now disabled, the dialog says why, explains how to move each account ("Change plan" on the account's page), and links to the accounts list narrowed to that plan (`/accounts?plan=<id>`).

**A suspended account offers no invitation.** The invitation card of a suspended account no longer shows "Create invitation link" and "Resend invitation"; it says the account must be reactivated first. The panel refuses both for a suspended account as well: `POST /api/v1/invitations/{accountId}/link` and `/resend` answer 409 `InvitationAccountSuspended`.

**The FTPS service card tells one story, and every switch answers.** File transfer → FTPS service used to say "Turned off" beside "Running", keep "Turn FTPS off" enabled in every state, and answer a click with nothing. The panel setting now has three values — never configured, turned on, turned off — so a fresh server no longer reads as one an administrator switched off. "Running" is shown only when the service is active and its control port answered on the host; an active service that does not answer reads "Not answering". When the setting and the service disagree, the card says so in amber: a service running although FTPS is off (the off button is then labelled "Stop the service"), FTPS on but the service not running, or a service that runs but does not answer. When there is nothing to turn off, the button is disabled and the reason is printed beside it. Turning FTPS on or off now shows a message worded from what the server measured afterwards ("turned off, but the service is still running" is not reported as success), a failure is shown on the card in your language — including one that never reached the panel — and turning FTPS off re-reads the firewall as turning it on already did. For integrations: `GET /api/v1/ftps-server` and the two switches answer `desiredState` (`neverConfigured`, `on`, `off`) and `agreement` (`consistent`, `runningButOff`, `onButNotRunning`, `runningButNotAnswering`) in place of `enabled`. In the development polygon images, the panel's own units the installer never starts (`maran-ftps.service`, `maran-php-install@*.service`) now read inactive and disabled instead of active; an image built before this change still shows the old answer until it is rebuilt.

**Installing your own certificate says what is wrong with it.** Site → SSL → Install my own certificate used to empty both text areas when the panel refused the material, and then report the text you had just pasted as "required". The pasted certificate and key now stay in the form after a refusal, and are cleared only once the certificate is installed. The refusal is shown under the field it is about and says what is wrong: missing, not valid PEM, too large, unreadable by the server, or a private key that does not belong to the certificate. For integrations: `POST /api/v1/certificates/custom` answers 400 with one of `CertificatePemRequired`, `CertificatePemInvalid`, `CertificatePemTooLarge`, `PrivateKeyPemRequired`, `PrivateKeyPemInvalid`, `PrivateKeyPemTooLarge`, `AgentCertificateUnreadable`, `AgentPrivateKeyUnreadable` or `AgentPrivateKeyMismatch`; the single `CertificateMaterialInvalid` code is gone, and the server no longer answers `AgentInvalidInput` for a certificate pair it could not read. The panel and the server agent move to contract revision 3 together, so both must be updated at once.

**An empty backup schedule screen is no longer an error.** Opening Backups → Schedule on a server where no schedule has been saved, for the whole server or for one account, used to make a failed request that the browser logged as an error. The panel now answers normally that nothing is scheduled, and the screen says "Nothing is scheduled here" as before, with a clean console. For integrations: `GET /api/v1/backup-schedules` now always answers 200 with `{ "schedule": … }`, where `schedule` is `null` when nothing has been saved for that scope; it no longer answers 404 `BackupScheduleNotFound`. The "Applies to" card's description is now centred beside the picker instead of sitting low with empty space above it.

**Switching on a backup schedule no longer looks like a backup.** Saving a schedule with "Run this schedule" on used to show the save time as "Last run started", although nothing had run. The schedule now records when it was switched on separately from when it last ran. The "What the server holds" card shows the next run the server has planned and says "Never run" until a backup has actually started. The first run is the next named hour after the save. Saving an enabled schedule again does not push its next run back, and switching one off and on again does not start a backup at once. Existing schedules are corrected on upgrade: a switch-on time recorded as a run is cleared where no scheduled backup followed it.

**Everyone has a profile.** "Profile and security" in the account menu opens a new Profile page for administrators and customers alike. It shows your username, email and role, whether two-step verification is on, and how many devices are signed in, with links to manage each. A customer also gets a link to their hosting account there. An administrator is no longer sent to a sessions screen in its place, and is never sent to the customer's hosting-account page.

**Customers can use their own panel.** A customer creates databases, file-transfer logins, scheduled tasks, backups and sites for their own account. There is no account picker, and the list of every account is never requested. The screens and buttons meant for administrators are no longer shown to a customer. Opening such an address leads to a clear "not available to you" page, not to the licence upgrade page. A customer sees the background tasks about their own account, without the operator's log. A refused request now explains itself in the reader's language instead of a bare "Forbidden". Accepting an invitation shows the login name to sign in with.

**Sign-in screens and forms look and behave better.** The signed-out screens put the form on a glass card over the panel's glow, with full-height fields and button. Field labels are small capitals across the panel, and corners are a little rounder. An empty sign-in or reset request is caught on the spot, the error clears as soon as you type, and an old invitation message no longer follows you to the sign-in page. A list with nothing to choose from now says so instead of opening empty. A focused field shows one accent edge with a soft halo, buttons spring gently under the pointer, and the credit line links to Innovayse.

**Clearer feedback and wording.** Creating an account, site, plan, database, login, scheduled task or firewall rule confirms with a message. A failed background task explains itself in your language instead of showing an internal code. Sizes read as MiB, GiB and TiB. Counts use the right plural in every language. Scheduled tasks read as sentences ("Every day at 03:05"). Confirmation buttons name what they do ("Delete site"), and they never start focused, so a stray Enter cannot delete anything.

**The shell.** The jump-to search opens ready to type. The collapsed rail and search list modules in the sidebar's order. Menu names say where they lead. Switching the language renames the modules immediately. The current page is scrolled into view. The theme switch is on phones too and says what it will do. On phones, tables show that they scroll and keep their row actions in reach. Dialogs, menus and messages move gently, and not at all when you ask for reduced motion.

**The sidebar has submenus.** A module with more than one page (Accounts with its list, the new-account
form and Plans; Sites; Databases; Backups with its schedule and destinations) is now a section that
opens and closes. The section holding the page you are on opens by itself, even from a bookmark;
which sections you keep open is remembered in your browser. The chevron says whether it is open to a
screen reader, and the arrow keys move through the menu. With the sidebar collapsed, pointing at or
tabbing to an icon shows its pages beside the rail, and the jump-to search finds the pages too.
Only the page you are on is highlighted inside a submenu (the list entry no longer lights up beside
"New account" or "New site"). In Armenian and Russian the menu uses short names, so no entry wraps
onto a second line (Կարգավիճակ, Պլանավորիչ, Պահուստներ; Планировщик).

**The navigation is denser.** The sidebar is 216 px instead of 240, rows and icons are smaller and the
app bar is 48 px tall, so more of a work screen fits without scrolling. On a phone the menu rows stay
44 px tall for touch.

**The backend CI lane no longer dies of memory exhaustion.** The integration tests boot one panel per
test, and none of them was ever released: the framework's rate limiter keeps a timer alive after its
host stops, and timers started while a test panel booted held on to the panel through the test
harness's context. The test process grew past 6 GB and took the runner down mid-run, reported only as
a shutdown signal. A stopped panel now releases everything it held, and the CI log prints the
machine's memory every 30 seconds so a future failure of this kind shows its numbers.

**The break-glass `maran` commands work on a real install.** `maran admin-reset-password` exited
with a usage error on every call, because the command refused the `--` its own wrapper passes before
the identifier. `maran collect-diagnostics` refused every bundle, because it took the panel address
setting `PasswordReset__PanelUrl` for a secret (its name contains "password") and found the address
in the logs; and the reason was never printed, because an earlier step had sent the rest of the
command's error output to nowhere. A setting is now a secret only when its name ends in one of the
secret words, as the panel's own redaction rule says.

**Signing in with a second factor now works.** After a correct authenticator or recovery code the
panel sent the user back to the sign-in screen, and only a reload let them in: the screen read the
answer to the code step in the shape of the first step's answer and found no session in it. The
browser test that should have caught it answered with the same wrong shape, so both agreed with each
other and not with the server; it now answers exactly as the server does.

**The install test can now open the panel in a real browser.** `maran installer --browser` creates the
first administrator through the setup screen in Chromium, signs in and requires the dashboard, over
the panel's own TLS listener. Until now every check reached the panel with a command-line client that
accepted a certificate browsers refuse, which is how an installed panel that no browser could open
went unnoticed.

**Deleting an account left things behind that a new account could inherit.** After the processes were
ended, the user's number (uid) still keyed several places `userdel` does not clean: jobs queued with
`at`, files in `/tmp`, `/var/tmp` and `/dev/shm`, shared memory, message queues and semaphores, and a
lingering `systemd --user` session manager. The next account given that uid would own them. Deletion
now removes each before the system user goes, in this order: crontab, `at` jobs, user manager,
processes, SysV IPC, shared temporary files, mail spool, then `userdel`.

- `userdel --remove` was measured to leave a deleted account's mail spool behind on AlmaLinux 9, so the
  spool is now removed explicitly (only a regular file owned by the account; anything else refuses).
- Every step fails closed: anything left aborts the deletion with a system failure and running it again
  finishes the job. A temporary directory holding another owner's files is never emptied or chowned;
  deletion refuses instead.
- The agent's own private `/tmp` hid the host's, so a sweep would have found nothing and passed. The unit
  now bind-mounts the host's temporary directories under `/run/maran-host/` (`PrivateTmp` stays on).
- `at` is optional: where it is not installed the step is skipped. Group-owned leftovers and
  `/dev/mqueue` are not handled yet (follow-up).

Threat note: `docs/superpowers/notes/2026-10-29-account-deletion-leftovers-threat-note.md`; its second
review is recorded as outstanding.

**A published release can no longer ship without both architectures.** A build, sign or
verify run with `--publish` now refuses unless both x86_64 and aarch64 are present: `build --publish`
requires `--arch both`, and signing and verification refuse a manifest missing any of the api, agent or
frontend entries for either architecture. A local build without `--publish` still targets the host only.
An installer run on an architecture the release was not built for now says so plainly, naming the
architecture, instead of reporting only a missing manifest entry.

**Plans can be created, edited and deleted from the panel.** Administrators manage hosting plans under
Plans: a name is unique regardless of case, a plan with accounts on it cannot be deleted, and a changed
disk quota is pushed to the accounts already on it. A quota cannot be lowered below any account's usage;
every account is measured first and a refusal leaves all of them untouched. Count limits only gate new
sites, databases and logins, never what an account already has. Reference: the Hosting accounts page of
the documentation.

**An administrator locked out of the panel now has a supported way back in.** When the only administrator
lost a password or an authenticator and no mail was configured, the only options were editing the
database by hand, which leaves no audit row, or reinstalling. The installer now places a root-only
`maran` command at `/usr/local/sbin/maran` (it adds no daemon, port or network call):

- **`maran admin-reset-password <email|username>`** prints a one-hour, single-use password-reset link for an
  active administrator, spends any older link, clears the lockout, signs the account out everywhere and
  writes an audit row and an `authpriv` journal line. The link goes to the terminal only, never to a log;
  `--clear-two-factor` also removes the authenticator, and is never implied.
- **`maran status`** reports service health and the panel's own view of itself even when the database is down.
- **`maran collect-diagnostics`** writes a redacted 0600 support bundle from a fixed allow-list and deletes it
  if a second scan still finds a secret. Customer files, site logs, keys and backups are never read.

Operator-facing reference: the Recovery page of the documentation. Security review of this surface is
recorded as outstanding in `docs/superpowers/notes/2026-10-29-breakglass-cli-threat-note.md`.

**A hung program could freeze the root agent and block every other customer's writes.** Every program
the agent runs as root was started without a deadline, without an output limit and in the agent's own
process group. Measured with a validator that never exits, held under the host-wide configuration lock:
after 75 seconds the call was still running, another tenant's write was still blocked, and both the
program and its child were alive. A program that printed 5 MiB was held entirely in memory. The agent
now starts every program through one bounded path:

- **A deadline per kind of call.** Checks and reads get 60 seconds, ordinary changes 120, service
  reloads 240, database statements 300, package installs 2100, and backup work up to 12 hours. At the
  deadline the whole process group is killed, not only the leader, and then collected. Measured: the
  call returned a typed timeout at 60.0 seconds, the program and its child were gone, the previous
  file content was restored and the other tenant's write finished 3 ms later.
- **A cap on captured output**, 4 MiB per stream by default. Going over it kills the program and is an
  error that returns none of the output; exactly 4 MiB is still accepted.
- **Standard input is never inherited** and the cleared environment is kept.
- **Programs are named by a type that only the distro layer can create**, always by absolute path, so
  a bare string can no longer reach a spawn. `--` now ends the options before a user or unit operand
  wherever the tool supports it.
- **`tar --list` is bounded** (2,000,000 members, 16 KiB per name, 256 MiB of listing), and a backup is
  listed once as it is written, so one that restore would refuse is refused at creation. Database
  clients also ask the server to stop waiting on a lock after 240 seconds.

A timeout reaches the panel as the existing `SYSTEM_FAILURE` code with no tool output, inside a normal
response, not as a gRPC `DEADLINE_EXCEEDED`: the proto is unchanged and no new error code exists. The
agent sets no server-wide timeout or concurrency limit, because that would free nothing and would
report failure for a write that then succeeds. The systemd sandbox needed no syscall or capability
change.

Known residuals: a killed program's work already handed to the service manager or a database server
may still finish; backups written by earlier releases are not re-checked; a deadline-kill under the
real systemd sandbox and the agent-to-API handshake were not measured on the build machine. This
change alters how the only root process starts every program and needs a second human review before
it reaches `main` (#90).

**A signed-in customer could reach any API endpoint that forgot to declare its authorization.** The
fallback policy, which applies to every endpoint with no authorization metadata, only required a
signed-in user at customer level. Measured against a test-only endpoint with no metadata: a customer
got 200, and on an unknown path or a wrong HTTP verb a customer got 404 and 405 instead of a refusal.
The fallback is now administrator-only: anonymous callers get 401, customers get 403 (an empty body)
and administrators are admitted, so a future endpoint that omits its policy is closed to customers by
default. The default policy for a bare `[Authorize]` stays customer-level.

Every endpoint now names its policy instead of relying on either default: the dashboard, session
listing and revoking, sign-out-everywhere and the two-factor management actions are declared as
customer-level, and the setup controller marks its two anonymous actions individually rather than the
whole class. Customer behaviour is unchanged, as measured over the dashboard, session, two-factor,
account and site flows. New tests fail if an action or route-table endpoint names no known policy, if
the set of anonymous endpoints changes, or if the fallback is weakened. This is an authorization
change, so it needs a second human review before it reaches `main`.

**Reserved host names are refused for new sites, aliases, certificates and FTPS.** A tenant could
create a site named for an IP address, `localhost`, a `.local`/`.lan`/`.internal`/`.invalid` name or
the panel's own host name, and have nginx serve it and ACME try to issue for it. Measured before
the change: the agent's `Domain::parse` accepted `203.0.113.7`, `localhost`, `x.local` and the
rest, and the API created a site named `203.0.113.7` and one named for the panel (both 201). The
API and the agent now refuse the fixed set (a numeric final label, `localhost` and its
subdomains, the special-use suffixes `local`, `localdomain`, `internal`, `lan`, `home.arpa` and
`invalid`) on create site, aliases, issue and install certificate and enable FTPS. The API also
refuses the panel's own name, and its `www.` form, for sites, aliases and certificates (not for
FTPS), read from the new, optional `Panel__HostName` setting that the installer now writes into
`panel.env` (a downgrade ignores the extra line). The refusal says only that the name cannot be
used, in English, Russian and Armenian, and never echoes the name.

The panel vhost's `server_name` is now lower-cased and comes from the same function as the
certificate step; it falls back to `localhost` (it was `hostname -f`, or `_`), so re-running the
installer can change it on a host whose name is unusable. No database or proto change. Sites that
already exist under a reserved name are not removed: they can still be disabled, have their
certificate removed, be inspected and be deleted, because those paths check the name's shape only.

Not covered: the agent does not know the panel's name, so a compromised API could still ask it for
a vhost under that name; the primary domain of a new account is not checked; the nginx and ACME
behaviour was not observed on a real host. This change touches tenant isolation on a root-managed
host and still needs a second reviewer before it reaches `main`.

**The root agent's systemd unit is hardened further.** The unit that runs the agent as root kept
the kernel's control-group files writable, let it load kernel modules, accepted 32-bit system calls
and let it create setuid and setgid files. It now sets `ProtectControlGroups=true`,
`ProtectKernelModules=true`, `SystemCallArchitectures=native` and `RestrictSUIDSGID=true`;
`MemoryDenyWriteExecute=true` stays on. Measured on Ubuntu 24.04 (systemd 255) and AlmaLinux 9
(systemd 252): the agent starts and listens as before, and account, home-mode, site (`nginx -t`,
`php-fpm -t`, reload), certificate, cron, backup, restore, firewall and SFTP mount-unit operations
behave the same with the four directives on. The cgroup view becomes read-only, a 32-bit binary is
killed with SIGSYS, `chmod u+s` is refused, and `systemd-analyze security` exposure falls from 4.4
to 3.6.

`ProtectProc=invisible` and `ProcSubset=pid` are deliberately not set. `ProcSubset=pid` made `pkill`
fail on AlmaLinux 9, so a suspended account's processes stayed alive. The unit comments now say so
and mark what is still unmeasured (quota, other distribution versions, module autoload, cgroup v1).
Hosts must re-run the installer to receive the new unit. The change needs a second human review
before it reaches `main`.

**The root FTPS service now runs inside a systemd sandbox.** `maran-ftps.service`, the unit the
installer installs for the internet-facing, root vsftpd, restricted nothing but its runtime
directory: `systemd-analyze security` scored it 9.4 UNSAFE on Ubuntu 24.04 and 9.6 on AlmaLinux 9.
It now sets fourteen groups of directives (kernel tunables, modules, logs, control groups, clock and
hostname protection, native system calls only, no personality changes, no realtime scheduling, no
write-and-execute memory, no new setuid or setgid files, a short list of address families, a private
`/dev`, and a capability bounding set of ten capabilities), and scores 5.9 MEDIUM on both families.
Measured on Ubuntu 24.04 and AlmaLinux 9 under systemd as PID 1: a real FTPS session with TLS forced
(plaintext login refused, login, upload, passive listing, `SITE CHMOD`, download, logout) passes
unchanged, and the process reads back the new bounding set and a seccomp filter.

`ProtectHome`, `NoNewPrivileges`, `ProtectSystem=strict`, `PrivateTmp` and `RestrictNamespaces` stay
off, with the reasons in the threat note: `RestrictNamespaces` in particular makes vsftpd silently
drop its per-session isolation on Ubuntu 24.04 while logins still succeed. Hosts must re-run the
installer to receive the new unit. A new structure check keeps the rejected directives out of the
unit and the capability set within bounds.

Not shown by these measurements: the installer itself and the panel's enable-FTPS path were not run,
AlmaLinux's per-session isolation and the stock `/etc/shadow` mode 0000 case were not observable, and
`RestrictSUIDSGID` cannot be seen from a session because vsftpd masks the mode itself. This change
touches a root, internet-facing service and still needs a second reviewer before it reaches `main`.

**The panel refuses to start on a published or weak key outside Development and Testing.** The
repository's sample `.env.example` ships an encryption key and a token-signing key so a development
loop works out of the box, and nothing stopped an install from running on them: measured with the
published host and `ASPNETCORE_ENVIRONMENT=Production`, the panel booted on both sample keys, so
anyone who has read the source could sign an administrator's access token or decrypt the secrets the
panel stores. Outside Development and Testing the host now refuses to start when either key is the
published sample, decodes to bytes too structured to be random, or when the signing key equals or
contains the encryption key; the message names the setting and the problem and never prints the key.

Installs made by the installer are not affected: it generates two separate random keys. A hand-seeded
install that copied the sample stops at startup. Replacing the signing key costs nothing but a fresh
sign-in; replacing the encryption key makes the values already encrypted under it unreadable, and
there is no re-encryption tool yet.

**Installing a PHP version from the panel failed, and could leave the package database broken.** The
agent ran the package manager itself, inside a sandbox that keeps `/usr` read-only. On Ubuntu 24.04
the install died on "Read-only file system" part-way through and left fifteen packages half-configured
in dpkg, which also blocks unattended security updates; on RHEL-family hosts dnf reported the read-only
mount as a lack of disk space. The agent now asks systemd to start `maran-php-install@<package>.service`,
a one-shot unit that is never enabled and runs only the family's package manager (`--no-remove` on
Debian, no `--allowerasing` on RHEL) for that one package, outside the agent's sandbox, which is not
widened. The install also survives an agent restart or a dropped browser tab. Measured on Ubuntu 24.04
and Rocky Linux 9: the install now succeeds and the agent's sandbox is unchanged.

Hosts must re-run the installer to receive the unit: a host that took a new agent binary alone fails
PHP installs with a "unit not found" error, and there is no in-process fallback. The product still
provisions no Remi or Sury repository, so RHEL-family hosts and Debian releases without the version in
the distribution archive cannot install it regardless of this change.

**The root daemon no longer acts on accounts it did not create.** Until now the agent checked only
that an account name was spelled `[a-z][a-z0-9_]{2,29}` and that the user existed. Measured on real
Ubuntu 24.04 and AlmaLinux 9 containers, suspending, deleting, setting a quota on or rewriting the
crontab of `root`, `mysql` or `games` all went through to `usermod`, `userdel`, `setquota` and
`crontab -u`: root's shell was changed to nologin, `mysql` was removed from `passwd`, `shadow` and
`group`, and `CreateAccount` for `redis` ran `useradd`. Anyone able to reach the panel's API as an
administrator, or a compromised API, could do the same to the host's own users. The fix is in two
places, and the agent is the one that enforces it.

- **A managed-account check in the agent.** Every account lifecycle operation (suspend, unsuspend,
  delete, quota, usage, suspension state), every cron operation, and the SFTP and FTPS login
  creation and password changes now require proof that the user is a hosting account: the name is
  not on a reserved list of 103 names (system, daemon, database, web, mail and cloud-image login
  names, plus anything starting `maran_`), there is exactly one passwd row of that name, its home is
  exactly `/home/<name>`, and its uid and gid are at or above the host's `UID_MIN` and `GID_MIN`.
  A refused request runs nothing, not even `id -u`, and answers `INVALID_INPUT` with a fixed sentence
  that does not say which rule refused; an absent user is still `NOT_FOUND`. `CreateAccount` refuses a
  reserved name before asking the host anything, and still refuses a user that already exists.
- **Deletion resolves the account inside its lock and again before `userdel`.** If the uid or gid
  changed while the cascade ran, the deletion stops with a system failure instead of removing a
  different user.
- **The panel refuses the same names, in three languages.** Creating an account through the API with
  a reserved name now fails validation with `AccountNameReserved`, and the username alphabet is
  narrowed to exactly the agent's rule: no hyphen, 3 to 30 characters. Both messages exist in
  English, Russian and Armenian. The account form in the SPA still offers the old hyphen pattern;
  that is a follow-up.

What an operator will notice: a name such as `admin`, `mysql`, `git` or `ubuntu` can no longer be
given to a hosting account, and `acme-01` style names are no longer accepted. Existing accounts are
not touched. `RepairAccountHomeGroups` now skips users the panel does not manage.

Known residual: there is no marker on managed accounts. An ordinary hand-made user at or above the
id floor, with home `/home/<name>` and a name not on the list, is still manageable. The decision and
its reasoning are in `docs/superpowers/notes/2026-10-26-account-reserved-names-threat-note.md`.
This change touches root-run account operations and needs a second human review before it reaches
`main` (#83).

## 1.0.0-beta.3 — 2026-10-03

**Maran could not be installed on half the systems it claimed to support, and now it can.** The
supported matrix is Debian 12/13, Ubuntu 22.04/24.04/26.04 LTS, and RHEL, AlmaLinux, Rocky Linux and
Oracle Linux 8/9/10 — thirteen distributions, both families, every one of them now installed from
nothing to a working panel and verified by the panel's own health endpoint rather than by the
installer's exit code.

The reason so much was broken at once is that nothing had ever executed the documented install path
against a real init. Two polygon images run no init at all, which is why a fake `systemctl` had to
exist — and its own header admits it "starts or stops NOTHING". A fake `systemctl` cannot fail on a
unit directive, a mount namespace, or a binary that will not execute, and every defect below is
exactly one of those. `maran installer` closes that hole: a fresh server with real systemd as PID 1,
carrying nothing Maran needs, on both families and both architectures.

What an operator would have hit:

- **On AlmaLinux, Rocky or Oracle 8, the panel never started.** The distribution's default PostgreSQL
  is 10 and the installer checked no version, so the panel aborted on a system catalogue column that
  arrived in PostgreSQL 11. There is now a floor of 13 — the oldest version actually proven — and on
  EL8 the installer enables a newer module stream from the distribution's own AppStream, needing no
  third-party repository.
- **On those same systems no release could even be verified.** The manifest's signature was Ed25519
  through `openssl pkeyutl -rawin`, an option that arrived in OpenSSL 3.0; EL8 ships 1.1.1, whose
  command line cannot verify Ed25519 at all. The installer reported that as a signature mismatch and
  told the operator to suspect a compromised mirror. Releases are now signed with ECDSA P-384 and
  verified with `openssl dgst`, which every supported openssl has — one code path, no "verify
  whichever this host can manage" branch.
- **On Oracle Linux the root daemon refused to start on every version.** The installer resolved the
  family from `ID_LIKE` and accepted the host; the agent kept its own list and answered `unsupported
  distro: ol`. `rhel` was missing from that list too, which no test could have caught because RHEL's
  own images carry no nginx or PostgreSQL without a subscription. The two lists are now held equal by
  a structure check.
- **On Ubuntu 22.04, Debian 12 and EL8 the agent could not run at all.** It was built on the release
  host and linked whatever glibc that machine had. It is now built inside an AlmaLinux 8 container —
  the oldest glibc in the matrix — and the release refuses to ship an agent that needs anything
  newer. A static musl build would have sidestepped glibc and broken account lookups wherever SSSD
  or LDAP answers them, so it was rejected rather than taken.
- **An install could report success with the root daemon dead.** Step 70 waited sixty seconds for the
  panel's socket and did not look at the agent at all, so the one step that installs the privileged
  daemon could not observe whether it runs. It now waits for the agent's socket with the same
  specific refusal. That wait is what caught three of the defects above.
- **The agent died before running an instruction on a fresh host**, because its unit named
  `/var/lib/maran-ftps` as a mandatory writable path and step 89 created it nineteen steps after step
  70 started the service. systemd does not skip a missing writable path; it fails to build the mount
  namespace. The directory is created with the rest of the layout, and a structure check now requires
  every mandatory `ReadWritePaths=` target to exist before the step that starts the units.
- **Three dependencies were assumed rather than installed:** `openssh-server`, which step 86
  configures and did not install, failing at step 86 of 16 after PostgreSQL, nginx and MariaDB were
  already in place; `libicu`, without which the self-contained panel refuses to start and says so
  only in the journal; and the `hostname` binary, absent on Oracle Linux 8, where both halves of
  `hostname -f || hostname` called the same missing program and the install died at exit 127.
- **On EL8 the installer installed MariaDB and then refused to continue because of it.** That
  distribution's MariaDB leaves root with no password at all — a server where every local uid,
  including the accounts Maran creates for customers, is root on every customer database. The refusal
  was right and is kept for a server that pre-existed the install; a server the installer created
  seconds earlier is now secured instead, with the socket plugin loaded *before* root's login method
  is changed, because doing it the other way round locks root out entirely.

**A suspended login could still obtain working access tokens.** Refreshing a session minted a fresh
access token while consulting only whether the user still existed — not whether the login may be used
at all. Suspension writes the state and revokes the sessions as two separate operations, so anything
that stopped the second (a cancelled request, a failure in the session store, a process that died in
between) left a suspended login holding live sessions, and each refresh rotated one into a new token
indefinitely. The question now has a single definition on the domain entity, both paths that mint a
token ask it, and a structure check refuses any future file that issues one without asking. Measured:
the test for it fails without the check and the active-login control passes either way, and only two
places in the codebase mint a session or a token at all.

**Dependency updates are made deliberately again.** Dependabot is off, and the ten updates worth
taking from its last three batches are in — with the EntityFrameworkCore companions brought back
into step with the provider, which had drifted a patch version apart. Nine are refused, each with a
reason: the .NET 10 line (this tree pins the SDK to 9.0.317 with `rollForward` disabled), TypeScript
7 as a major compiler change, and the coupled tonic/prost/askama breaking releases, which touch the
generated proto contract and need their own work. The cost is stated rather than buried: security
updates no longer arrive on their own.

**The polygon's assertion script is twelve files instead of one.** It was 7381 lines holding 61
assertions — the largest violation in the tree of the rule that one file is one unit. The split is
by subject and preserves the original order exactly, verified by the parts concatenating
byte-identically to what was removed and by the assertion output being the same 433 lines before and
after.

**ARM64 is built and runs.** `maran release build --arch both` produces a manifest carrying all six
artifacts; the aarch64 agent and panel were verified by executing them on aarch64 with glibc 2.28.
`installer/lib/10-preflight.sh` has accepted `aarch64` from the beginning while the release built
only x86_64, so an ARM64 operator passed the gate and found nothing to install. One step, the
firewall, cannot complete under emulation — an aarch64 `nft` cannot drive an x86_64 kernel — so ARM64
is not yet claimed as verified end to end.

## 1.0.0-beta.2 — 2026-09-23

**The first beta could not install on a Debian-family host.** `30-postgresql.sh` searched
`/etc/postgresql` to a depth of two for `postgresql.conf`, and Debian and Ubuntu put it three levels
down at `/etc/postgresql/<major>/<cluster>/postgresql.conf`. The packages installed, `initdb`
created the cluster, and the installer then refused to find its own configuration. Both resolvers
had it — `postgresql.conf` and `pg_hba.conf`.

It was found by the first real install anybody ran, on Ubuntu 24.04, and it survived because the
polygon copies that step into its image without ever executing it: the image installs MariaDB and
sshd, not PostgreSQL. An assertion now exercises the resolver against the layout Debian creates,
with the inverse control that the old depth must fail on the same tree.

The refusal also prints what it searched for and lists what `/etc/postgresql` holds. Diagnosing the
original needed a photograph of a console.

Nothing else changed: the panel, the agent and the SPA are the same software as beta.1.

## 1.0.0-beta.1 — 2026-09-23

**A beta, and the README says what that means: do not put somebody else's customers on it.** The
software installs, runs and does what its screens say. Three things a paid hosting product must be
trusted about have been exercised only in this repository's own containers — no certificate has
been issued by a real authority, no disk limit has been enforced by a real filesystem, and no
restore has been run on real hardware.

The threat notes covering the privileged parts were reviewed and accepted on 2026-09-23; the
per-note verdict is in `docs/superpowers/notes/2026-09-22-second-review-packet.md`. That discharges
`rules/security.md`'s second-reviewer requirement. It is not an external audit.

What is absent by design: DNS, mail, and a web database manager.


### Added

- **Panel authentication.** First-run setup from the installer's one-time token, sign-in with
  Argon2id, JWT access tokens of fifteen minutes, refresh rotation in an httpOnly SameSite=Strict
  cookie where a reused token revokes the whole family, TOTP with recovery codes, and sessions you
  can list and revoke — including "sign out everywhere".
- **Account lockout.** Ten consecutive failures lock an account for fifteen minutes. A locked
  account is refused before its password is checked and with the same error a wrong password
  gets, so the response is not an oracle.
- **Hosting accounts as real Linux users.** Creating an account provisions its system user, home
  directory and the plan's disk quota through the agent; suspend, reactivate and delete reach the
  host too. The agent acts first and the database row follows, so the panel never claims an
  account the server does not have.
- **An append-only audit journal** of every sign-in and every mutation, with an
  administrators-only screen.
- **The `maran` CLI** — one entry point to the whole toolbox (`maran` lists it).
- **Websites.** Static, PHP and reverse-proxy sites, with domain aliases, a document root inside
  the account's home, and enable/disable. The agent renders the vhost, writes it beside the
  running configuration, makes the server's own `nginx -t` validate the result, and rolls the
  file back if either the validation or the reload refuses — so a bad render cannot take a
  server's other sites down with it. Every value written into a configuration file is validated
  rather than escaped: a domain that is not letters, digits, hyphens and dots is not a domain, so
  there is no newline to end a directive and start one of the caller's choosing.
- **Several PHP versions side by side.** Each account gets its own php-fpm pool per version,
  running as that account's Linux user, with the worker budget taken from its plan. Customers may
  change a whitelisted subset of settings; a name outside the list is refused rather than quietly
  dropped, and the hardening lines above them are written as `php_admin_value`, which a customer
  setting cannot countermand.
- **SSL.** Certificates ordered over ACME with an HTTP-01 challenge served from the site's own
  document root, custom material an operator can upload, and a renewal pass thirty days before
  expiry. Key material is written to a root-only store outside every account's home, because a
  site's PHP runs as that customer and a key inside the home is a key the site could read.
- **Live log tailing.** A site's access and error logs stream into the interface. The stream
  names a site and a log kind, never a path, and is bounded: a per-account limit on how many may
  be open at once, a heartbeat so a quiet log is not mistaken for a dead connection, and the
  reverse-proxy timeouts the installer writes to match.
- **Databases.** MySQL/MariaDB databases with a dedicated user each, per-plan limits, a size
  reading per database, and a password reset. The password is generated by the panel, shown once
  and never stored — not even encrypted — so recovery is a reset rather than a retrieval. The
  server has no idea what a tenant is, so isolation is built rather than assumed: every name is
  prefixed with the account, the grant is scoped to that one database, and a real server refuses
  a neighbouring account's data to the user the panel created. Creating the same database twice
  reports that it exists and leaves the first password working, because a retry after a lost
  response must not invalidate a credential the customer has already been shown once.
- **SFTP logins.** Per-account file transfer credentials served by the host's own OpenSSH, with
  per-plan limits and a password reset. Each login is chrooted into a root-owned jail with the
  account's real home bind-mounted inside it, so nothing about the home's ownership had to change
  and no other account becomes visible. The login carries the account's own uid, so an uploaded
  file belongs to the account exactly as one it created itself, and it gets `internal-sftp` and no
  shell — asserted as a real session being refused a command, not as a directive being present in
  a file. Port forwarding is off: a file transfer credential that can forward ports is a tunnel
  into the server's private network.
- **Deleting an account now removes what it left on the host.** Its databases, its SFTP logins,
  its jail and the jail's mount unit go before the system user does, and a cleanup failure aborts
  the deletion rather than leaving an orphan. A re-created account of the same name inherits
  nothing — not a database, not a login, not a password.
- **Container polygons for the agent.** Ubuntu 24.04 and AlmaLinux 9 images that run the agent's
  privileged suites against real `nginx -t`, real `php-fpm -t` and real system accounts, so the
  validation the whole design rests on is exercised against the software it will meet on a
  customer's server rather than against a fake that always agrees.

### Known limitations shipping with this

Named here rather than in a footnote, because each of them is something an operator would
otherwise find out the hard way.

- **The ACME client has never spoken to a real certificate authority through to an issued
  certificate.** It reaches Let's Encrypt staging's directory and nonce endpoints and is refused
  at account registration until a contact address is configured; ordering, challenge validation,
  finalising and downloading have been exercised only against a fake authority in tests.
- **HTTP-01 only.** No wildcard certificates and no DNS-01; those arrive with DNS management.
- **A reverse-proxy upstream is checked for shape and not for destination**, so a site can be
  pointed at a loopback service on the same machine.
- **`Acme:CertificateStorePath` is read by nothing**; the store path is fixed in the agent.
- **No web database manager.** There is no phpMyAdmin or equivalent; it is planned as a separate
  deployable with its own vhost and authentication rather than partially done here, so an operator
  who needs one does not have it.
- **The database-grant escape is forward-only.** A database-level `GRANT`'s name is a LIKE pattern,
  not an identifier, and an unescaped `_` in it matches any single character — so a build before
  this fix could grant an account access it never issued, to a same-length account whose name
  collided at the right position. The fix escapes every grant `create_database` writes from now on;
  it does **not** touch a row a previous build already wrote, because rewriting a live customer's
  access with nobody watching is worse than leaving it as found. A host that created databases
  before this change carries unescaped rows until an administrator runs the repair from the panel's
  Databases screen (`RepairDatabaseGrants`, `agent/crates/ops/src/db/repair_grants.rs`), which
  reports what it would change before changing anything. See
  `docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md` and
  `docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md`.
- **FTPS ships, and this entry used to say it did not.** The line above read "No FTPS … File access
  is SFTP only — there is no FTP daemon installed and no second listening port … FTPS is tracked as
  issue #20", and every clause of it was false by the time it was read. The correction is stated
  rather than made quietly, because an operator who believes no FTP daemon is installed does not go
  looking for one on their own host. What is true: the installer installs vsftpd
  (`installer/lib/89-ftps.sh`), and FTPS ships end to end — `agent/crates/ops/src/ftps/`,
  `backend/src/Maran.Modules/Ftp/`, `backend/src/Maran.Agent.Client/Services/FtpsService/` and
  `frontend/src/locales/{en,ru,hy}/ftp.json`. It is installed **switched off**: nothing listens, no
  configuration is rendered, and the distribution's own `vsftpd.service` is masked until an
  administrator turns FTPS on from the panel. `README.md` describes what "on" then means.
- **A dropped database is not backed up first.** There is no snapshot, no recycle bin and no
  delay: dropping is final. The Backups module ships (`backend/src/Maran.Modules/Backups/`) and
  this clause used to say it was "not here yet" — but it takes scheduled account backups, and
  nothing in the drop path consults it: `grep -rni backup backend/src/Maran.Modules/Databases/Commands/DropDatabase/`
  finds nothing. A database is restorable only from whatever backup already existed.
- **Nothing re-checks sshd's configuration after the install.** The installer writes the one
  `Match Group` block that turns an SFTP account into a jailed file transfer login and is
  idempotent about it, but nothing looks again afterwards. A hand-edited or upgrade-reset
  `sshd_config` that loses the block would give every SFTP customer a full shell session and the
  panel would report every login as healthy.
- ~~**Account deletion writes no audit entry.**~~ Corrected: it does, and this entry was wrong.
  `DeleteAccountCommandHandler.cs:288` records `AuditActions.AccountDeleted` on success and
  `:423` records the failure, alongside the final-backup outcome at `:343`, `:353` and `:366`
  (`grep -n AuditActions backend/src/Maran.Modules/Accounts/Commands/DeleteAccount/DeleteAccountCommandHandler.cs`).
- **An account created before this release cannot serve a site.** Creating an account now
  group-owns its home by the web server's group, at mode `0750`, so nginx can traverse into the
  document root without the home being opened to every other local user. Homes created earlier do
  not have it and there is no repair command; `chgrp www-data /home/<account>` (`nginx` on the
  RHEL family) fixes one by hand.

### Fixed

- **The installer's panel vhost was never the file `nginx -t` checked, and the vhost it shipped
  did not load.** Step 80 wrote the rendered vhost to `maran.conf.staging` and then ran
  `nginx -t`; nginx includes `conf.d/*.conf`, which `.staging` does not match, so the test parsed
  a tree the new file was not in, printed "test is successful", and the installer moved a file
  nothing had read over the served path. It hid a second defect for as long as it existed: the
  shipped vhost asked for HTTP/2 with `http2 on;`, a directive that arrived in nginx 1.25.1, while
  the supported families ship older ones — so on Ubuntu 24.04 and AlmaLinux 9 the panel's own
  vhost was an "unknown directive" and nginx would not start with it. The vhost now reaches disk
  through the same render-swap-validate-roll-back protocol the agent uses for customer configs, so
  what is validated is the file that will be served; a vhost nginx refuses now stops the install
  with the previous configuration restored and nothing reloaded, instead of surfacing at the next
  reload with a successful `nginx -t` in the log as evidence that it was fine. Validating after the
  swap means there is one `nginx -t` during which a file nobody has approved sits on the served
  path, so an install interrupted inside it — Ctrl-C, a dropped SSH session, a stopped container —
  now puts the previous vhost back before the installer dies, and re-raises the signal. After a
  `SIGKILL`, which nothing can catch, the next run prefers the rollback copy the installer left
  behind to the file on the served path — and asks `nginx -t` about that copy before serving it,
  because a copy this step did not write may be a fragment a hard reset left behind. One it cannot
  load is left where it is, named, with the panel vhost removed rather than replaced by it: the
  panel is then unreachable until an install finishes, which costs the panel, where an nginx that
  will not start costs every site on the host.
- **Deleting a customer armed a trap for every other customer.** A php-fpm pool names the account
  it runs as and php-fpm resolves that name at startup, so a pool left behind by a deleted account
  made the next reload — for any reason, days later — fail with `cannot get uid for user` and take
  PHP down for every tenant on the server. Nothing in the agent removed a pool at all. Deleting an
  account now removes every pool it owns, before `userdel` makes their user unresolvable; deleting
  a site or switching its PHP version removes the pool left behind when, and only when, no other
  site of that account still uses it.
- **Certificate renewal never ran.** The handler was named `…Job`, which Wolverine does not
  discover, so every daily trigger was dropped with "No routes can be determined" and every
  certificate the panel issued would have expired unwatched. Renamed to the convention, and a test
  now asks the running panel whether the message has a handler rather than reading a registration.
- **A fresh installation had no plans, so no account could be created at all.** The plan seeder
  existed, was unit-tested and had no caller anywhere in the product; it now runs at startup,
  inserting only what is absent so an operator's edited plan survives a restart.
- **A newly created PHP site had no php-fpm pool**, so its vhost pointed at a socket nothing had
  bound and the site answered 502 until somebody changed its PHP version — the only operation that
  wrote a pool. Creation and the version switch now write it through one shared function.
- **No web server could read any document root.** Every account home was `0750` owned by the
  account, and nginx refused every request with `stat() … (13: Permission denied)`.
- **The SSL tab could not be used.** Its API composable was still a seam that rejected every call,
  so a site whose certificate nginx was serving was shown "Certificates are not available on this
  server yet". The four endpoints are wired.
- **The third plan on the create-account form could not be clicked**, clipped by the card the form
  is drawn in, and two sidebar entries linked to an upgrade wall for modules the licence includes.
- The login rate limiter partitioned on a `username` **query** value while the endpoint
  authenticates the request **body**, so an attacker could get a fresh partition per request and
  guess passwords without limit. It now keys on the caller's address alone.
- Nothing read `X-Forwarded-For`, so behind the installer's own nginx every request appeared to
  come from `127.0.0.1`: one rate-limit budget for the entire internet, and an audit journal
  whose "from where" was always loopback. The header is now honoured for one hop, and only from
  loopback — it is written by the client, and trusting it from anywhere would hand the limiter a
  key the caller picks.
- Agent operations ran with no timeout at all: the resilience pipeline was registered and
  resolved by nobody. The health probe's timeout was hard-coded, which made
  `Agent:ProbeTimeoutSeconds` configuration nothing read.
- Command validators were registered but never invoked, so a weak password reached the database.
- `.env.example` told developers to copy it to `.env`, and nothing read the result.

## 0.1.0 — foundation

The contract, the Rust agent skeleton with `SO_PEERCRED` on its unix socket, the C# modular
monolith, the Vue shell, the development environment and CI.
