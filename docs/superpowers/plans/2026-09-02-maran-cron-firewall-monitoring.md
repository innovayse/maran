# Cron + Firewall + Monitoring + Tasks + SMTP (Plan 5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-account crontabs, an nftables firewall with brute-force auto-bans, host monitoring with charts and e-mail alerts, a panel-wide background-task UI, SMTP settings — and the four Identity items carried from Plan 2: configurable password policy, forced 2FA for administrators, escalating lockout, and password reset by e-mail.

**Architecture:** Same shape as Plans 3 and 4. The agent gains three areas (`ops::cron`, `ops::firewall`, `ops::monitor`) behind the three proto services that already exist as stubs; every value that reaches a config file is a validated type from `agent-core`; nftables text is rendered by `templates` with golden tests. The backend fills four prepared module homes (Cron, Firewall, Monitoring, Tasks) plus Identity changes; modules talk only through Wolverine messages and `Maran.Sdk`. The SPA gains four screens and two settings screens.

**Tech Stack:** Existing stack plus THREE new dependencies, all named here so the licence pass expects them: **MailKit** (backend — `System.Net.Mail.SmtpClient` is documented obsolete), **serde_json** (agent `maran-ops` — parsing `nft -j` output; a hand-rolled JSON parser in a root daemon is worse than a vetted one), and **rustix** (agent `maran-ops` — `statvfs` for the root filesystem's used/total bytes; added during Task 6 because the only alternatives were `unsafe` in `ops`, which the rules forbid, or a `DistroAdapter` method for something that does not differ per family). `maran licenses` runs in Task 17 and must cover all three.

**Spec:** docs/superpowers/specs/2026-08-29-maran-design.md — §11 (Cron, Firewall, Monitoring, Tasks), §15, §10/§16. Issue #5.

**Plan v6.** Six adversarial review rounds shaped this document: the first two DISPROVED parts of it empirically on the polygon images (nft additivity, cron `%`, the `( )` wrapper, a boot without a firewall), the third caught an unwired parameter chain whose literal reading firewalled the panel off its own port, the fourth and fifth caught the quota data path crossing a module boundary and the Sdk widening filed in the wrong phase, and the sixth confirmed closure. Where a ruling exists because an earlier version was wrong, it says so, so nobody re-introduces the original idea as an "improvement".

## Global Constraints

- NEVER `git commit`, `git add` or push. The owner commits. No AI attribution anywhere.
- No shell-string execution by the agent. Processes are argv arrays against absolute paths from the distro adapter; `maran structure` rule 17b refuses bare-name spawns.
- Doc comments on every item, private included. One file = one public unit, named after it. Errors in `*_error.rs`.
- `ops` never names a platform literal (rule 17); facts come from `maran_distro` or `AgentPaths`.
- Validated, not escaped. No caller-supplied byte ever reaches nftables text or a crontab line (the cron design below makes that structural, not conventional).
- Every agent operation is idempotent; `AlreadyExists`/`NotFound` are outcomes.
- IDOR answers 404, never 403 (one deliberate exception in Task 13, stated there). Admin-only surfaces answer 404 to customers.
- DoD per feature: unit + integration + IDOR test + audit success AND failure events + i18n en/ru/hy.
- Secrets: SMTP password via `EncryptedStringConverter`; reset tokens stored hashed; nothing secret in any GET, log line, or persisted message envelope.
- Frontend laws: const arrows, UI kit only, api composables (`use<Feature>Api.ts` — the nine existing files all follow this name shape) called from stores only.
- Verification: `source scripts/dev` FIRST, always. Then `maran agent check`, `maran structure`, `maran proto`, `maran handshake`, backend `dotnet test`, frontend lint+typecheck+build+`npx playwright test`.
- **Rules changes need the owner.** This plan itself PROPOSES three rules-file amendments (listed in "Rules changes this plan carries" below). The owner approving this plan approves those diffs; implementers apply them verbatim in the named tasks and may not add others.

## Rules changes this plan carries (owner approval = plan approval)

1. `rules/rust.md` + `agent/CLAUDE.md`: the `validation/` map row for `web/` already names `port · ip_address`, and `system/` names `cron_expression`. This plan PLACES the new types there — `web/port.rs`, `web/source_cidr.rs` (the map's `ip_address` row is renamed to `source_cidr · ban_address` to match what actually ships), `system/cron_schedule.rs` (the map's `cron_expression` renamed to `cron_schedule`) — so the amendment is a RENAME of two planned rows to the shipped names, not a new folder. No `net/` folder is created (v1 of this plan invented one; the map is law).
2. `rules/vue.md`: the icon rule gains one sentence — "icon SVG comes only from lucide via `UiIcon`; `UiChart` is the single non-icon SVG site" (Task 15).
3. `agent/CLAUDE.md` crate map: `ops` gains its `cron/`, `firewall/`, `monitor/` rows as shipped (they are `(planned)` today).
4. `rules/rust.md` + `agent/CLAUDE.md`, agent-core `utils/` rows: add `system_accounts.rs` (parse a passwd database into account rows — a host question with no feature knowledge, which is `utils/`' own definition). `QuotaBlocks` does NOT move: Task 6 no longer needs it (quota is panel data), and a domain parser has no place in `utils/`.

## Rulings (R1–R14). Each exists so executors do not re-litigate; several exist because v1 was empirically wrong.

- **R1 — default-drop input, split across two tables, and the order is load-bearing.** Bans live in `table inet maran_bans` whose input chain hooks at priority -5: `iif "lo" accept`, then the two ban-set drops, policy accept. Rules live in `table inet maran` at priority 0: loopback accept, `ct state invalid` drop, `ct state established,related` accept, ICMP/ICMPv6, SSH, the panel port, the API-managed allows, policy drop. Consequences, all intended and empirically verified on the polygon: a ban can never sever loopback (the -5 chain accepts `lo` before its drops, and an accept verdict in one chain only ends THAT chain — the packet still traverses priority 0, which accepts `lo` again; ping from a banned loopback alias was confirmed answered); a ban DOES kill the attacker's already-open sessions (priority -5 runs before the rules table's established-accept); a ban applies to SSH and the panel — that is what an anti-brute-force ban is FOR, and self-lockout is guarded one layer up (R8's whitelist, seeded by the installer). `inet` family means the port allows cover IPv6 (verified).
- **R2 — SSH's hard allow is a fallback, not a cage, and neither port is a literal.** The template takes `ssh_port` and `panel_port` as parameters, and BOTH are host facts only the INSTALLER knows, delivered the same way: `Firewall__SshPort` (first `Port` directive of the host's real `sshd_config`, default 22 — a host running sshd on 2222 must not be locked out by a template that only knows 22) and `Firewall__PanelPort` (nginx's public vhost port, 8443, written by the same installer that writes the vhost) into `panel.env`, bound by the Firewall module's options and sent on every firewall mutation as two proto fields. The panel port is emphatically NOT the backend's own listen configuration: Kestrel listens on loopback 5080 behind nginx, and a literal reading of "its own listen port" renders `tcp dport 5080 accept` under policy drop — the panel then survives the installer's seed and dies on its first mutation, with no remote recovery. This paragraph exists because a review caught exactly that reading. `tcp dport {{ ssh_port }} accept` renders UNCONDITIONALLY ONLY while no admin-authored **TCP** rule for that port exists; the moment one does, the explicit rules render instead — so an admin CAN source-restrict SSH. A UDP rule for the same port number is an ordinary allow and does NOT displace the fallback (a UDP rule deleting TCP SSH access was a reviewed lockout hole). Removing the last TCP ssh-port rule returns the fallback: fail-open for SSH, by design. The panel port's hard allow has no override in v1 — a panel lockout has no remote recovery path at all.
- **R3 — the customer's command NEVER appears in the crontab.** v1 wrapped the command in `( … )` inline and was disproved twice: cron rewrites the first unescaped `%` into a newline (breaking v1's own `date +%s` suffix), and `echo hi # comment` parses standalone but not inside `( )`. The corrected design: the command is written VERBATIM (plus trailing newline) to a per-entry file `~/.maran/cron/<id>.cmd`, and the installed crontab line is one hundred percent agent constants plus the entry id: `<schedule> /bin/sh /home/<acc>/.maran/cron/<id>.cmd > /home/<acc>/.maran/cron/<id>.log 2>&1; echo $? > /home/<acc>/.maran/cron/<id>.exit`. Output is truncated per run (`>` — the spec wants the LAST run); the exit file's CONTENT is the code and its MTIME is the run timestamp, so no `date`, no `%`, no second file. `/bin/sh` is named explicitly so a crontab `SHELL` override cannot change the interpreter. The command alphabet accordingly RELAXES from v1: `%` and `#` are legal (they live in a file now); control characters (`\n`, `\r`, `\0`, all of `char::is_control`) and the 4096-byte ceiling remain refused — a `.cmd` is one command line, not a script editor.
- **R4 (as corrected during Task 4) — writes and removals under an account's home fork; reads that must RETURN data use the hardened root-side open.** Creating `~/.maran/cron`, writing `.cmd` and removing entry files run inside `fork_as_account`, with paths derived through `AgentPaths` + the entry id, never from input. There is no chown branch anywhere.
  READS are different, and the original wording was wrong about them: `fork_as_account` returns `Result<(), PrivError>` with no channel back from the child, and `close_inherited_descriptors()` closes every descriptor ≥3 before the child's work runs, so no pipe or handoff file can carry a file's contents out. A rule that cannot be implemented is a rule that gets quietly broken. So `.cmd`, `.log` and `.exit` are read root-side through the repository's OWN hardened pattern — the one `ops::sites::follow_log` documents — which closes every attack the fork was meant to close: `O_NOFOLLOW` on the directory and the file, `O_DIRECTORY` on the first, `O_NONBLOCK` plus `is_file()` against a FIFO, `nlink == 1` against a hardlink, `uid` ownership checked on BOTH, the file reached through the directory descriptor via `open_in_directory` so no rename can redirect it between opens, and a byte budget enforced DURING the read — a budget checked beforehand is one the account chooses the moment to exceed.
  The `privs` gap is real and is filed separately: giving `fork_as_account` a channel back from the child touches the one crate where `unsafe` is allowed, and `rules/security.md` requires a second reviewer and a threat note for that.
- **R5 — the ruleset file is REPLACED atomically; bans live in a second table.** `nft -f` is ADDITIVE — proven on the alma9 polygon: re-loading a re-rendered file left a removed rule live and duplicated the rest, so v1's DenyPort reported success while the port stayed open. The rendered file therefore begins with the canonical replace idiom — `table inet maran {}` (no-op create) followed by `delete table inet maran` followed by the full declaration — and bans CANNOT live in that table (delete would erase them): they live in `table inet maran_bans` with its own input chain at hook priority -5. Order across tables is decided by hook priority alone, so the bans chain runs before the rules chain, and it therefore carries its own `iif "lo" accept` ahead of the set drops. Both chains are golden-tested (Task 3) and the whole arrangement was verified live on the polygon (a ban survives a rules re-apply; loopback survives a ban).
- **R6 — a ban is `nft add element` with a timeout; the panel is the durable store.** Both families' nftables units flush on stop/reload (verified: Debian's config file opens with `flush ruleset`; RHEL's unit `ExecStop`/`ExecReload` flush), so runtime bans die with a service restart or reboot. That is fine BECAUSE the Firewall module records every ban (address, expiry, reason) in `firewall.BanEpisodes` and a startup reconciler re-applies the unexpired ones. The agent stores no reason: v1 put `reason` on the wire and into an nft `comment`, which was an injection primitive (nft parses its argument in its own grammar) — the reason is panel metadata only; the proto fields STAY on the wire, deprecated and never read or written (the additive law — Task 7 Step 0 is normative).
- **R7 — network metrics are counters since boot**; the chart endpoint derives rates by dividing the delta by the ACTUAL elapsed seconds between the two samples (the sampler is allowed gaps), clamping a negative delta (reboot, interface churn) to zero.
- **R8 — brute-force escalation is per source IP as Kestrel reports it, and the forwarding that makes that the CLIENT's address already ships**: `ForwardedHeadersExtensions` sets `ForwardLimit=1` with loopback as the only trusted proxy, and nginx appends the real peer via `$proxy_add_x_forwarded_for`, so the rightmost entry is authoritative and client header-stuffing is inert. Task 13 does not reconfigure it — it VERIFIES it with two integration tests this tree lacks: the forwarded address is the one recorded, and the same header from a non-loopback peer is ignored. (v2 of this plan claimed forwarding was absent; the review corrected the record.) Thresholds: K=25 failures / W=10min across any usernames (policy-configurable); TTL escalates 15m → 1h → 24h by episode count in 24h; the whitelist is checked panel-side before every ban, and the INSTALLER SEEDS it with the installing operator's SSH client address when `SSH_CLIENT` is present — an empty whitelist on day one was v1's self-lockout hole.
- **R9 — the tasks stream is SSE framing over authenticated fetch — the SAME framing site logs use.** v1 said "NDJSON, not SSE" on a false premise: the existing transport IS `text/event-stream` frames written by `SiteLogStreamWriter` and parsed by `useApi`'s stream helper (which splits on `\n\n` and decodes SSE fields). Tasks mirror that exactly: a `TaskStreamWriter` in the Tasks module (same framing constants, its own file — modules do not import each other's internals) and the SPA reuses the existing helper untouched.
- **R10 — monitoring keeps raw 60-second samples for 7 days** (≈10,080 rows — v1 said 600k, wrong by 60×), buckets on read with `date_bin` (5min/24h, 30min/7d), one nightly retention delete. No rollup table.
- **R11 — cross-module mail is `SendMailRequested` on a LOCAL, NON-DURABLE queue.** Two constraints, and only this shape satisfies both. (a) The reset mail carries a live token, so its envelope must never be persisted — durable local queues are NOT enabled for this message type (`PersistMessagesWithPostgresql` governs durable endpoints; plain local publishes are in-memory buffered in this codebase's configuration, which the review verified). (b) The send must not run inline in the request: v2 said `InvokeAsync`, and that REGRESSED the enumeration channel — a known address would have cost a full SMTP round trip while an unknown one returned instantly, a seconds-scale oracle. So the handler generates the token, publishes to the local queue, and answers; the Monitoring handler sends in the background. A crash between publish and send loses the mail — acceptable, the user re-requests. Send failure → `MailSendFailed` audit, no retry. No SMTP → `MailSkippedNoSmtp`. Task 13's test pins the decoupling: with a mailer faked to take 5 seconds, the request answers in milliseconds for BOTH known and unknown addresses.
- **R12 — SMTP settings live in Monitoring; the security policy lives in Identity.** Both one-row singletons, cached, invalidated on write.
- **R13 — cron environment is edited against a reserved-name denylist.** `MAILTO` and `SHELL` are refused (`MAILTO` is an outbound-relay primitive through the host MTA; `SHELL` changes the interpreter under every entry). The agent ALWAYS renders `MAILTO=""` and `SHELL=/bin/sh` as its own first two lines — output already goes to our capture, and cron mailing it anywhere is pure spam surface. `PATH`, `TZ`, `CRON_TZ` and arbitrary `KEY` otherwise pass the Task 1 alphabet.
- **R14 — tasks are admin-only in v1** (every instrumented kind is an admin operation); the customer-facing task feed arrives with Files/Backups.

## What Plan 4 (and this plan's own review) learned — binding on every executor

1. **The stale dev server manufactures false verdicts both ways.** Before trusting any e2e mutation verdict, CONFIRM THE MUTANT IS SERVED (curl the module URL for a string that survives compilation; a test failure is also positive evidence). Three verdicts went wrong in Plan 4 before this discipline.
2. **A test that cannot see the code it guards is theatre.** The bare-`id` spawn survived a sweep test that ran against a fake. Every "sweeping" test in this plan names WHAT REAL OBJECT it exercises and what it cannot see; gaps get a structure-gate rule or a polygon test.
3. **`subject_named` in check-structure.sh is a SKIP list.** Never "register" a file there.
4. **Quiet tree before any measurement.**
5. **Mutation-test protections against the WHOLE suite; restore byte-identical with fresh mtime (`cmp`), then rebuild.**
6. **Counters, not deltas, across a stateless boundary; divide by measured elapsed time.**
7. **Silence is not success.** A verifier must positively confirm it saw the thing it verifies (empty CI run list ≠ green; `*) exit 0` in a stand-in ≠ a service manager).
8. **Score a mutation on its NAMED failures, never on a count.** Three separate runs in Task 4's
   re-review each reported exactly `1057 passed / 2 failed` — a count-scoring harness would have
   called two genuine kills SURVIVED, because two unrelated tests from a concurrent agent were red
   throughout. Only the named list separates a kill from a coincidence. Cross-checking the count
   against the names is not enough on a shared tree: the NAMES are the score.
9. **A SURVIVED verdict owes a witness.** A mutation that changes nothing survives accurately and
   means nothing — `filter_map(|f| Some(x))` where the original was `map` compiles to identical IR,
   and no "did the file change" guard can see it. So a survivor is only reportable with one
   throwaway assertion that is RED under the mutant and GREEN under the original, both results
   pasted. If no such assertion can be written, the mutation changed no behaviour and must not
   appear in the table at all — and it belongs under the existing NOT MEASURED verdict rather than
   as a new category, because KILLED already carries its own proof of observability. Only SURVIVED
   bears the burden.
   **Three mechanisms, in the order they cost:** (a) log `diff pristine mutant` into every run —
   free, and it makes a three-line no-op visible on sight; (b) the **escalation probe** — re-mutate
   the SAME expression to something maximally destructive and read the pair: both survive means the
   region is genuinely untested and the verdict stands, while the destructive one dying means the
   tests do reach it and the survivor must not be scored. Measured on the real case: the no-op
   survived all 1060 tests while `Some(0)` on the same expression killed 8. It is a screen, not a
   proof, but it needs no author judgement; (c) the deciding check is the harness-verified witness
   above — one input and the two differing outputs, run on pristine and mutant, refuse to score if
   they are equal. An author cannot bluff it, and writing the witness IS writing the killing test.
   **A measured dead end, recorded so nobody spends the afternoon on it:** codegen hashing does not
   work. `rustc -O --emit=llvm-ir` gave 843 differing IR lines for the no-op against 1588 for the
   faithful mutation — no threshold separates them. An earlier draft of this rule claimed identical
   IR would prove a no-op; it was wrong, and it was corrected by a reviewer who ran it. The REASON
   it cannot work is worth keeping beside the result: a no-op rewrite still changes WHICH iterator
   adapters are instantiated, so it perturbs the IR heavily while perturbing behaviour not at all.
   IR distance measures how much code moved; a mutation harness needs to know whether MEANING
   moved. The escalation probe works precisely because it asks a behavioural question instead of a
   structural one.
10. **Mutate the SHAPE of the original bug, not merely the absence of its fix.** Deleting a call
   proves the call is reached; only mis-ordering it proves the ORDER is what the test checks. Task
   5's durability fix needed both mutants before the test meant anything.
11. **A filtered test run manufactures survivors — re-measure every SURVIVED verdict unfiltered
   before tabling it.** Observed live in Task 3: a `-p`-filtered target reported SURVIVED for two
   template lines that the whole workspace showed KILLED, by four tests in `ops::firewall`. The
   guard for a line can live in another crate entirely — Task 3's line 23 turned out to be half
   guarded by `templates` goldens and half by firewall unit tests — so a crate-scoped run is blind
   to exactly the coverage that matters most, the kind that crosses a boundary.
12. **No fixture may give a parameter a value equal to what a literal-substitution mutant would
   render.** A golden whose `ssh_port` is 22 cannot see `{{ ssh_port }}` → `22`; a golden whose
   `panel_port` is 8443 cannot see `{{ panel_port }}` → `8443`. Both survived a full workspace run
   in Task 3, on the two lines where a wrong value costs remote access to the host. The realistic
   default is precisely the value that blinds the test, and an implementer reaches for it by
   instinct — so the rule is stated where fixtures are built, not left to judgement.
13. **Before declaring a fix round done, sweep the docs for identifiers that no longer exist.**
   Grepping for the name you just removed only finds the removal you already remembered — which is
   why a stale doc outlived its mechanism three separate times in this plan. The check that works
   asks the opposite question: take every backticked identifier in every comment in the area and
   confirm each still resolves to something in `agent/crates`. Task 5 ran it over 28 files and got
   zero unresolved; it is cheap, it needs no memory of what changed, and it is what would have
   caught the stale comment that survived the round claiming to close it.
14. **When one mutation could be killed by either of two guards, mutate them separately — and if
   no existing test separates them, that is the missing test.** Task 5's first attempt at the
   malformed-set finding mutated both guards at once, which scores the pair rather than each check.
   Splitting them revealed that the obvious test does NOT separate them: with the per-set refusal
   downgraded, the document-level guard still catches a document whose ONLY set is junk. What
   separates them is a readable set BESIDE an unreadable one — the readable one satisfies the
   document guard while the junk one is skipped in silence. The distinguishing case is the test
   that was missing.
15. **A shell gate must be run to completion in the real image, and that run is the last step of
   verification rather than an afterthought.** `bash -n` parses without evaluating, so it cannot see
   an unbound variable; and a local run of `assert-installer-steps.sh` dies at the MariaDB gate
   ~300 lines before the line that was broken. A `readonly` dropped while both its uses remained
   therefore passed every local check and killed BOTH image builds. Nothing short of the complete
   script inside the real image proves a shell gate works.
16. **When an oracle compares two sources of truth, decide deliberately whether it demands equality
   or a superset — and say which.** Task 16a's detector answers `2300` for
   `ListenAddress 0.0.0.0:2300` while `sshd -T` reports that port under `listenaddress` rather than
   `port`, so an equality assertion would have failed the build on CORRECT behaviour. The oracle
   allows a superset on purpose. An oracle whose strictness was never chosen will eventually fail
   the thing it was built to protect.
17. **When two sources disagree, go to the ground truth rather than to a third opinion.** Task
   16a's detector answered 2300 and its oracle answered 22 for the same host. Both were readings of
   `sshd_config` and of `sshd -T`; neither settles which port sshd actually binds. The implementer
   started a real sshd on both families and looked at the listening sockets: 2300. That turned
   "two views of the host" into "one of them is wrong about it" — `sshd -T`'s `port` lines are the
   `Port` OPTION, printed always and defaulting to 22 whether or not a socket uses it, while
   `listenaddress` is the socket list. A disagreement between two derived readings is not resolved
   by reasoning about either.
18. **When a field is retained for compatibility and a new field supersedes it, every test must
   set the two to DISAGREE.** Task 8's tri-state mapping consulted the deprecated `running` boolean
   in one arm and no test noticed, because every existing case set the boolean to AGREE with the
   state field — so reading either gave the same answer and the mutant survived. The fix is a
   theory whose every row sets the old field to the value that flips the result if it is consulted:
   `(Running,false)`, `(Stopped,true)`, `(Unknown,true)`. A compatibility field is retained
   precisely so new code stops reading it; a test that lets the two agree cannot tell whether it
   did.
19. **When a mutant dies, confirm it died from the check under test and not from an earlier
   guard.** Task 16b's unbounded-`sed` mutant survived twice, and the second time was the
   instructive one: the case had been added AFTER an earlier step deleted the payload, so the
   refusal came from a missing include rather than from the marker check the test names. A green
   assertion that refuses for the wrong reason is indistinguishable from one that refuses for the
   right one — set the precondition immediately before the case, and take the expected text from
   the production constant rather than restating it.
20. **A test must run the code path production runs, not an isolated helper.** Task 16b's `exit 1`
   fired inside a process substitution, so a malformed port list truncated instead of aborting and
   the step returned rc 0 — while its assertion ran the same helper in an EXPLICIT subshell, where
   `exit` is observable, and passed. Not a missing case: a case run in a context the real caller
   does not have. Assert on the CALLER's outcome — what it returned, what reached the outside world
   — and the isolated-helper test becomes unnecessary rather than misleading.
21. **Verify rulings empirically before building on them.** Two v1 rulings died on the polygon in review (`nft -f` additivity, `%` in the capture suffix). When a task says "verify on the polygon and record the answer", that verification is part of the task's deliverable, and the recorded answer goes in the code's doc comment.

## File structure

New files only; `(+)` = modified. Prepared empty homes are filled, not created.

```
proto/agent/v1/cron.proto (+)      command moves to a file: doc rewrite; +GetCronEntryOutput,
                                   +SetCronEntryEnabled, +Get/SetCronEnvironment
proto/agent/v1/firewall.proto (+)  ADDITIVE ONLY (Task 7 Step 0 is the law): +ssh_port and +panel_port
                                   fields on mutations; reason fields stay, deprecated by comment
proto/agent/v1/monitor.proto (+)   ADDITIVE ONLY: +ServiceState state=4 beside the kept bool running=2;
                                   uptime_seconds kept, written 0; +MANAGED_SERVICE_SSH; counter comments;
                                   quota_bytes kept, written 0 — quota is the PANEL's own data (Task 6)

agent-core validation:
  system/cron_schedule.rs (+_error)     five validated fields   [map row renamed from cron_expression]
  system/cron_command.rs  (+_error)     control-chars/length only (R3)
  system/env_var_name.rs  (+_error)     KEY grammar + R13 denylist
  system/env_var_value.rs (+_error)
  web/port.rs (+_error)                 1..=65535               [map row already says port]
  web/source_cidr.rs (+_error)          CIDR, canonicalising    [map row ip_address → source_cidr·ban_address]
  web/ban_address.rs (+_error)
agent-core (+): agent_paths.rs         + ACCOUNT_CRON_DIR (".maran/cron") helpers + agent_scratch_dir
agent-core (+): utils/system_accounts.rs  NEW unit: parse a passwd database into account rows
                                          (extracted from ProcessSftpHost's method body — a refactor
                                          with a trait-signature change, NOT a file move; QuotaBlocks
                                          does not move anywhere — see Task 6)

distro (+): crontab/nft/sh binaries; nftables persistence facts per family (Task 2, VERIFIED);
            firewall_service, cron_service, managed_units
templates: nftables/{nftables_ruleset.rs, nftables_bans_table.rs, nftables_allow.rs,
           nftables_protocol.rs} + .j2 ×2 + golden ×3   [names corrected during Task 3: structure
           check 16 names a file after its public item, and `ruleset.rs` holding `NftablesRuleset`
           fails it. `subject_named` is a SKIP list and must not be extended to keep a wrong name.]
ops: cron/ …entry files, cron_host.rs, process_cron_host.rs (privs!), model/…
     firewall/ …, apply_ruleset.rs (replace idiom), ensure_bans_table.rs, model/…
     monitor/ …, fixtures under tests/fixtures/proc/{ubuntu24,alma9}/
agent services ×3 + tests; polygon suites ×3; systemctl stand-in gains unit-state files (Task 7)
agent (+): config/invocation.rs gains the render subcommands the installer seeds from (Task 7)

backend Agent.Client ×3 + Resilience ×3
backend modules: Cron (no persistence), Firewall (Whitelist, BanEpisodes + StartupBanReconciler),
                 Monitoring (Samples, SmtpSettings, AlertStates, jobs, SmtpMailer/MailKit),
                 Tasks (PanelTask, TaskRecorder, TaskStreamWriter, stream controller)
Sdk: Contracts/{SendMailRequested, BruteForceDetected}.cs (new), AccountSnapshot.cs (+DiskQuotaMb),
     Interfaces/ITaskRecorder.cs (new), IAccountDirectory.cs (+ListAsync)
Identity (+): SecurityPolicy + cache, PasswordResetToken, FailedLoginByIp, ForwardedHeaders,
              reset rate limit, forced-2FA steering filter
Plan entity (+): MaxCronEntries + migration + seeder (5/20/200)

frontend: pages/{cron,firewall,monitoring,tasks}/…Page.vue, settings/{SmtpSettingsPage,SecurityPolicyPage}.vue,
          auth/{ForgotPasswordPage,ResetPasswordPage}.vue, components/ui/UiChart.vue,
          components/<area>/…, stores ×6, composables/apis/use{Cron,Firewall,Monitoring,Tasks,Smtp,SecurityPolicy}Api.ts,
          types, locales ×3 per area, e2e per area + stubs

installer/lib/87-firewall.sh (seed BOTH nft files via the agent's render subcommands; family include
                              wiring per Task 2 facts; sshd-port detection; whitelist seed from SSH_CLIENT),
                              88-cron.sh; install.sh (+MARAN_PANEL_PORT authority); 10-preflight.sh (+derived
                              ports); panel.env.example (+3 Firewall__ keys); uninstall (+): delete both
                              tables, unwire includes, state marker
docker/polygon (+): nftables + cron packages; systemctl stand-in state support
```

---

## Phase A — the agent

### Task 1: The validated inputs

**Files:** the seven type+error pairs above; `validation/{system,web}/mod.rs` (+); `agent_paths.rs` (+); the rules-map renames from "Rules changes" §1 applied to `rules/rust.md` + `agent/CLAUDE.md`; tests mirrored under `src/tests/validation/{system,web}/`.

**Interfaces:**
- `CronSchedule::parse(minute, hour, day_of_month, month, day_of_week: &str) -> Result<Self, CronScheduleError>`; `Display` = five fields space-separated; per-field accessors.
- Field grammar: comma list of items; item = `*` | `*/step` | `N` | `N-M` | `N-M/step`; decimal only; bounds 0-59 / 0-23 / 1-31 / 1-12 / 0-6; `N<=M`; `step>=1`; no names, no `@shortcuts`, no whitespace inside a field.
- `CronCommand::parse(&str)`: 1..=4096 bytes UTF-8, refuses every `char::is_control` (tab included), refuses leading/trailing whitespace. `%` and `#` are LEGAL (R3 — the command lives in a file). The error enum's doc says WHY the alphabet is this small a list now, citing R3, so a reviewer meeting it without the plan understands.
- `EnvVarName::parse`: `^[A-Z_][A-Z0-9_]{0,63}$` AND not in `RESERVED_NAMES = ["MAILTO", "SHELL"]` (R13; `ReservedName` is its own error variant naming the reason). `EnvVarValue::parse`: 0..=1024 bytes, no control chars, no `%` (env lines DO live in the crontab).
- `Port::parse(u32) -> Result<Port, PortError>` (1..=65535; `value() -> u16`). No reserved-port logic here — R2 moved that decision into the ruleset builder, which knows the panel port from the request.
- `SourceCidr::parse(&str)`: v4/v6 via `std::net`, prefix bounds, canonical re-render (leading-zero octets refused), `any_v4()`, `is_v4()`. `BanAddress::parse(&str)`: one IP, canonicalised.
- `AgentPaths`: `account_cron_dir(&AccountName) -> PathBuf` (= home + ".maran/cron"), `cron_cmd_path/cron_log_path/cron_exit_path(&AccountName, &CronEntryId)`.

- [ ] **Step 1 — failing tests.** Every proposition below by name (line comments, not block comments — a `*/` inside a block comment does not compile):
```rust
// cron_schedule: every_conventional_form_parses_and_renders_itself (covers "*", "*/5", "1",
//   "1-5", "1-5/2", "0,30", "1-5/2,10"); a_field_outside_its_bounds_is_refused;
//   a_reversed_range_is_refused; a_zero_step_is_refused; names_and_at_shortcuts_are_refused;
//   whitespace_anywhere_in_a_field_is_refused; display_is_exactly_five_fields_space_separated
// cron_command: control_characters_are_refused_one_by_one; percent_and_hash_are_legal_because_the_command_lives_in_a_file;
//   the_length_ceiling_is_enforced; surrounding_whitespace_is_refused
// env_var_name: mailto_and_shell_are_refused_as_reserved; the_grammar_is_enforced
// source_cidr: v4_and_v6_parse_and_canonicalise; leading_zero_octets_are_refused;
//   an_overlong_prefix_is_refused; a_hostname_is_refused_without_dns
// port: zero_and_65536_are_refused
```
- [ ] **Step 2:** red (types absent) → implement in the idiom of `validation/system/name.rs` (read first; no regex crate).
- [ ] **Step 3:** `maran agent check` + `maran structure` green (the renamed map rows keep structure's docs-vs-tree checks honest).
- [ ] **Step 4:** mutation per type (admit minute 60; admit `\n` in command; admit MAILTO; admit /33) → named test red → `cmp`-restore.

### Task 2: Distro facts — verified, not guessed

**Files:** adapter + both services/adapters + both family test tables (+).

**Interfaces and the facts to VERIFY on both polygon images (the verification transcript goes into the task report and the answers into doc comments):**
- `crontab_binary()` `/usr/bin/crontab`; `sh_binary()` `/bin/sh`; `nft_binary()` `/usr/sbin/nft` — verify each with `command -v`.
- Persistence (review-corrected facts, re-verify): Ubuntu 24 `/etc/nftables.conf` starts with `flush ruleset` and has NO include; Alma 9 unit reads `/etc/sysconfig/nftables.conf` with includes commented and `/etc/nftables/` samples at 0700. Therefore: `nftables_ruleset_path()` = `/etc/maran/firewall.nft` and `nftables_bans_path()` = `/etc/maran/firewall-bans.nft` — an AGENT-OWNED location identical on both families, so they live in `AgentPaths`, NOT the adapter. What differs per family is WHERE the installer wires the include, so the adapter answers `nftables_include_target() -> &'static str` (`/etc/nftables.conf` vs `/etc/sysconfig/nftables.conf`) and Task 16 does the wiring. The boot-order consequence is stated in the doc comment: Debian's `flush ruleset` in the distro file runs BEFORE our include, which is correct (flush, then load ours).
- `firewall_service()` = `nftables` both; `cron_service()` = `cron` / `crond`; `managed_units()` closed set: nginx, mariadb, cron-per-family, `ssh`/`sshd`.
- [ ] Steps: extend `EXPECTED_BINARIES` + service-name literal tests (red) → implement → verify on both images → green; mutation: move one path → family test red.

### Task 3: The nftables templates — rules table and bans table

**Files:** `templates/src/nftables/{mod.rs, nftables_ruleset.rs, nftables_bans_table.rs, nftables_allow.rs, nftables_protocol.rs}`, `templates/nftables/{ruleset.nft.j2,bans_table.nft.j2}`, three goldens, registrations (+). One file per public item — structure check 16 derives the expected filename from the item, so a type named `NftablesRuleset` lives in `nftables_ruleset.rs`. Never extend `subject_named` in `check-structure.sh` to keep a different name: it is a SKIP list, and an entry there EXEMPTS the file from the check entirely.

**Render structs:** `NftablesRuleset { ssh_port: u16, panel_port: u16, ssh_rules: Vec<NftablesAllow>, allows: Vec<NftablesAllow> }` — `ssh_rules` holds ONLY the TCP rules whose port equals `ssh_port` (the builder in Task 5 routes them; a UDP rule for the same number is an ordinary allow and never displaces the fallback); `NftablesAllow { port: u16, protocol: NftablesProtocol, source_cidr: String, source_is_any: bool, family_keyword: &'static str }`. `NftablesBansTable {}` (constant text, but rendered like every other template so the golden regime covers it).

`ruleset.nft.j2`, verbatim (no shebang — `nft -f` needs none, and a path literal does not belong in `templates`):

```
# Rendered by the Maran agent. Hand edits are overwritten on the next apply.
# The replace idiom: create-if-absent, delete, redeclare — nft -f is ADDITIVE
# without it, and an apply that merely appended left a removed rule live.
table inet maran {}
delete table inet maran

table inet maran {
    chain input {
        type filter hook input priority 0; policy drop;

        # Loopback first: the panel's own nginx→Kestrel hop lives here and
        # nothing — not even a ban — may sever it. Bans hook at priority -5
        # in table inet maran_bans and repeat this exemption for the same
        # reason.
        iif "lo" accept

        ct state invalid drop
        ct state established,related accept

        ip protocol icmp accept
        meta l4proto ipv6-icmp accept

{% if ssh_rules.is_empty() %}        tcp dport {{ ssh_port }} accept
{% else %}{% for rule in ssh_rules %}{% if rule.source_is_any %}        tcp dport {{ ssh_port }} accept
{% else %}        tcp dport {{ ssh_port }} {{ rule.family_keyword }} saddr {{ rule.source_cidr }} accept
{% endif %}{% endfor %}{% endif %}        tcp dport {{ panel_port }} accept
{% for allow in allows %}{% if allow.source_is_any %}        {{ allow.protocol }} dport {{ allow.port }} accept
{% else %}        {{ allow.protocol }} dport {{ allow.port }} {{ allow.family_keyword }} saddr {{ allow.source_cidr }} accept
{% endif %}{% endfor %}    }
}
```

`bans_table.nft.j2`, verbatim (idempotent to re-apply — same idiom; ELEMENTS are added at runtime and this file is only applied when the table is ABSENT, see Task 5):

```
# Rendered by the Maran agent. Runtime ban elements live here so replacing
# the rules table cannot erase them; this file itself is applied only when
# the table does not yet exist, or bans WOULD be erased by the idiom below.
table inet maran_bans {}
delete table inet maran_bans

table inet maran_bans {
    set banned_v4 {
        type ipv4_addr
        flags timeout
    }

    set banned_v6 {
        type ipv6_addr
        flags timeout
    }

    chain input {
        type filter hook input priority -5; policy accept;

        iif "lo" accept
        ip saddr @banned_v4 drop
        ip6 saddr @banned_v6 drop
    }
}
```

Goldens — THREE for the ruleset, and the third exists because a mutation survived the first two.
1. `ruleset.nft`: ssh_port 22, panel_port 8443, no ssh_rules, allows [80/tcp any, 443/tcp any, 3306/tcp 10.0.0.0/8].
2. `ruleset_ssh_restricted.nft`: ssh_port 2222 with restricted ssh rules whose `port` is deliberately 22 — NOT equal to `ssh_port` — plus a second ssh rule on `ip6`, and a source-restricted UDP allow. Every one of those parameters pins a mutant that survived without it: with the ports equal, `{{ ssh_port }}` → `{{ rule.port }}` renders identically; with every restricted allow TCP, a hard-coded `tcp` renders identically and would leave a requested UDP port closed while opening an unrequested TCP one under `policy drop`; with one family only, `{{ rule.family_keyword }}` → `ip` renders identically. A comment in the test data says why the ports disagree, so nobody "simplifies" them back.
3. `ruleset_ssh_any_source.nft`: ssh_port 2222, one ssh rule with `source_is_any: true` and `port: 22`. Covers the `source_is_any` ssh branch alone, which survives both goldens above; it cannot live in golden 2 because a bare `tcp dport 2222 accept` there would be byte-identical to the fallback and destroy the very property golden 2 exists to show.
Bans table golden as-is.

- [ ] Steps: goldens first (red) → implement → byte-flip check on each template → restore.

### Task 4: `ops::cron`

**Files:** `ops/src/cron/{mod.rs, cron_error.rs, list_cron_entries.rs, create_cron_entry.rs, update_cron_entry.rs, delete_cron_entry.rs, set_cron_entry_enabled.rs, get_cron_entry_output.rs, get_cron_environment.rs, set_cron_environment.rs, cron_host.rs, process_cron_host.rs, model/{crontab_document.rs, cron_entry.rs, cron_entry_id.rs, cron_run_record.rs, cron_environment.rs}}`; tests mirrored; `ops/src/lib.rs` (+).

**Interfaces:**
- `CronHost`: `read_crontab(&AccountName) -> Result<Option<String>, CronError>` (`crontab -u <name> -l`; "no crontab for" on a rc=1 → `Ok(None)`; any OTHER failure is an error); `install_crontab(&AccountName, &str)` (temp file 0600 under the agent-owned run directory `AgentPaths::agent_scratch_dir()` — root-owned, 0700, so no account can pre-plant a symlink where root writes → `crontab -u <name> <path>` → remove); `write_command_file(&AccountName, &CronEntryId, &CronCommand)`, `read_command_file(&AccountName, &CronEntryId) -> Result<Option<String>, CronError>` (the command is no longer in the crontab, so LISTING reads it back from the `.cmd`, and the duplicate check compares against it — without this method the central mechanism has no read side), `remove_entry_files(&AccountName, &CronEntryId)`, `read_run_record(&AccountName, &CronEntryId) -> Result<Option<CronRunRecord>, CronError>` (exit code = file content parsed, ran-at = file mtime), `read_output_tail(&AccountName, &CronEntryId, max_bytes) -> Result<Option<String>, CronError>` (last ≤64KiB, lossy UTF-8).
- `ProcessCronHost` holds the distro adapter AND performs every home-side file operation **inside `fork_as_account`** (R4). Its doc comment states the symlink reasoning. The crontab install itself runs as root (crontab(1) is the correct writer of the spool) — only home-dir I/O drops.
- `CrontabDocument::parse(text) -> Self` — infallible; foreign lines preserved verbatim; managed block = `# maran-entry: <uuid>` + following line; disabled = command line prefixed `#off# `. `render()` round-trips foreign text byte-identically. Layout, fixed and reasoned: the foreign region first, byte-identical and IN ITS ORIGINAL ORDER (positions included — cron env assignments apply to the lines below them, so relocating a foreign `PATH=` would change which foreign entries it governs); then the agent's banner comment; then `MAILTO=""` and `SHELL=/bin/sh` (R13); then the customer env lines; then the managed entries. Our env block sits BELOW every foreign line on purpose: whatever a foreign assignment set, ours re-set it for the managed region, so managed entries run under OUR interpreter and mail policy regardless of what a hand-edited preamble says. The round-trip law covers the foreign region verbatim-in-place, and the test asserts position, not just bytes: `a_foreign_env_assignment_stays_above_the_managed_region_and_below_nothing_new`.
- The installed entry line (R3, exactly): `{schedule} {sh} {cmd_path} > {log_path} 2>&1; echo $? > {exit_path}` — `{sh}` from the adapter, three paths from `AgentPaths`. Every byte is agent-derived; the TEST `the_installed_line_contains_no_caller_supplied_byte` walks the rendered line and asserts each byte's provenance (schedule renders from validated fields; the uuid is ours; paths are ours) — and unlike v1's suffix test, there is no `( … )` span to exempt, which is the point.
- `create`: id = new uuid → write `.cmd` (as account) → parse → append block → render → install → on install failure, remove the `.cmd` (no orphan). Duplicate (same schedule+command among managed, enabled or not) → `AlreadyExists` BEFORE any write. `update`: rewrite `.cmd` and/or schedule. `delete`: remove block + files; unknown id → `NotFound`. `set_enabled` toggles the `#off# ` prefix. All installs go through the whole-document render.
- `get_cron_entry_output` returns log tail + `CronRunRecord`.

- [ ] **Step 1 — failing tests**, the load-bearing ones by name: `a_foreign_crontab_line_survives_every_mutation_byte_for_byte`, `the_installed_line_contains_no_caller_supplied_byte`, `the_command_file_holds_the_command_verbatim_with_one_trailing_newline`, `a_disabled_entry_keeps_its_files_but_cron_cannot_run_it`, `creating_an_identical_entry_reports_already_exists_and_writes_nothing` (the comparison reads the `.cmd` files back — `read_command_file` — because the crontab no longer carries commands), `listing_returns_each_entrys_command_from_its_file`, `a_failed_install_leaves_no_orphan_command_file`, `deleting_removes_the_block_and_both_run_files`, `an_absent_crontab_lists_as_empty`, `mailto_and_shell_are_always_rendered_empty_and_bin_sh`.
- [ ] **Step 2:** implement over `RecordingCronHost`.
- [ ] **Step 3:** gates green. **Step 4:** mutations — skip the `#off# ` prefix; treat foreign lines as managed; skip the orphan cleanup — each kills its named test; scores recorded.

### Task 5: `ops::firewall`

**Files:** `ops/src/firewall/{mod.rs, firewall_error.rs, list_rules.rs, allow_port.rs, deny_port.rs, apply_ruleset.rs, ensure_bans_table.rs, ban_address.rs, unban_address.rs, list_bans.rs, firewall_host.rs, process_firewall_host.rs, model/{firewall_rule.rs, ruleset_state.rs, active_ban.rs}}`; tests; `lib.rs` (+); `maran-ops` Cargo.toml (+serde_json).

**Interfaces:**
- The rule store is the rendered file at `AgentPaths::nftables_ruleset_path()`. `RulesetState::parse` reads back ONLY a file matching our render (marker first line); anything else → `ForeignRuleset`, nothing overwritten.
- `apply_ruleset` (private engine): render (Task 3 — TCP rules whose port equals the request's `ssh_port` go into `ssh_rules`, everything else into `allows`; `ssh_port` and `panel_port` both arrive on the request per R2) → temp file → `nft --check -f temp` → fsync → atomic rename onto the path → `nft -f path`. `--check` failure → `RuleRefusedByNft{stderr}`, live state untouched. **The replace idiom in the file (R5) makes re-apply converge — the polygon proves deny actually removes (Task 7), because v1's design passed every fake-host test while leaving the port open.**
- `ensure_bans_table`: `nft list table inet maran_bans` — on NotFound, apply the bans template via the same check→rename→load engine against `nftables_bans_path()`; on present, do NOTHING (elements live there — R5/R6). Re-applying over an existing table ERASES its elements (verified), so this check-then-apply must never race: **every mutating `ops::firewall` operation holds one module-level `tokio::sync::Mutex`** (a root daemon has exactly one instance; a mutex is the honest fix and its doc comment cites the verified element-loss). The installer seeds the bans FILE at install time (Task 16), so on a healthy host this path is a no-op belt.
- `ban_address(address: &BanAddress, ttl: Option<Duration>)`: ensure table → delete element ignoring NotFound → `add element inet maran_bans banned_v4 { A timeout Ns }` (v6 set for v6; no timeout clause when permanent). Argv as separate items — VERIFIED working during review (rc=0 on alma9); the doc comment records it. No reason parameter (R6).
- `unban_address`: delete element; absent → `NotFound`. `list_bans`: `nft -j list set …` ×2, serde_json parse → `ActiveBan { address, expires_in: Option<Duration> }`.

- [ ] **Step 1 — failing tests:** `an_identical_allow_reports_already_exists`, `a_deny_for_an_absent_rule_reports_not_found`, `a_foreign_ruleset_is_never_overwritten`, `a_rule_nft_refuses_leaves_the_live_ruleset_untouched`, `the_apply_order_is_check_then_rename_then_load` (recording-host call log), `the_rendered_file_opens_with_the_replace_idiom` (string assertion on the first three effective lines — the unit-level tripwire for F1-class regressions), `a_ban_targets_the_family_matching_its_address`, `banning_twice_extends_rather_than_erroring`, `ensure_bans_table_never_reapplies_over_an_existing_table` (elements!), `a_tcp_rule_for_the_ssh_port_displaces_the_fallback`, `a_udp_rule_for_the_ssh_port_does_not_displace_the_fallback` (the reviewed lockout hole, pinned), `concurrent_mutations_serialise_on_the_module_lock` (two tasks racing ensure_bans_table against a recording host that counts applies — exactly one).
- [ ] **Steps 2–4:** implement; gates; mutations (drop the idiom preamble → its test red; reapply bans table when present → red; swap check/rename order → red; REMOVE the mutex → the serialisation test red).

### Task 6: `ops::monitor`

`get_service_statuses` returns `ServiceState::{Running, Stopped, Unknown}` + detail (the proto changes in Task 7 make it representable); `get_accounts_disk_usage` reports USED BYTES ONLY, and `quota_bytes` on the wire is written 0 and deprecated: the panel already owns every account's quota (the plan assigned it; the Accounts module stores it), and the Sdk-window widening that lets Monitoring read it is BACKEND work — it lives in Task 11, not here (an earlier draft filed it in this agent task, breaking Phase A's disjointness). This task's whole obligation is: report used bytes and the agent never parses `repquota` for this at all — the earlier draft's plan to reuse `ops::accounts`' `QuotaBlocks` was a forbidden cross-area import, and moving a domain parser into `utils/` (which rules/rust.md defines as domain-free) would have been a second violation to paper over the first. What the monitor DOES need is account enumeration, which today is a method body inside `ProcessSftpHost` reading the passwd database — so this task EXTRACTS it: a new `agent-core/src/utils/system_accounts.rs` (pure function: passwd text → account rows; tests move with the logic), `SftpHost`'s implementation delegates to it (a trait-shape-preserving refactor, and the full agent suite is measured before and after with equal totals), and `ops::monitor` calls the same unit. Rules-change §4 adds exactly that one map row; `/proc` parsers are pure functions over committed fixture text captured FROM both polygon images into `ops/tests/fixtures/proc/{ubuntu24,alma9}/`; CPU is two `/proc/stat` reads 250ms apart (the one permitted in-call wait, doc-commented); network sums physical interfaces (skip `lo`), counters per R7.

**Socket-activated units, measured on the polygons during Task 2 — read `DistroAdapter::managed_units`'s
doc before writing the status mapping.** On ubuntu24 `ssh.socket` is the enabled unit and
`ssh.service` is NOT in `multi-user.target.wants/`, so `systemctl is-active ssh` reads inactive on a
perfectly healthy host. `Accept=no` means the service STAYS ACTIVE once a first connection triggers
it, so the false-outage window is **boot until the first connection** — it does not reopen between
logins. Map that window to `Unknown` (or a "not yet started" state), never to `Stopped`: a monitor
that calls it stopped invents an outage on every Debian-family host at every reboot, and alerting
(Task 11) would mail about it. Alma9 enables `sshd.service` directly and has no such window. A named
test pins it: `a_socket_activated_unit_that_has_never_been_triggered_is_not_an_outage`.

- [ ] Steps: failing parser tests on fixtures + `a_stopped_service_is_an_answer_not_an_error`, `a_socket_activated_unit_that_has_never_been_triggered_is_not_an_outage`, `loopback_traffic_is_not_network_traffic`, `cpu_percent_is_bounded_0_to_100`; implement; gates; meminfo swap mutation → red.

### Task 7: Proto deltas, three services, and a polygon that can say no

**Files:** three proto files (+) per Step 0 below (the File-structure block summarises it; Step 0 is normative); `agent/src/services/{cron,firewall,monitor}/…` (three-line rule); `server.rs` (+); `agent/src/config/invocation.rs` (+) — TWO render subcommands so the installer's seed files come from the SAME askama templates the agent applies, instead of a shell copy that drifts: `maran-agent render-firewall-ruleset --ssh-port N --panel-port N` (fallback-only rules, allows 80+443) and `maran-agent render-firewall-bans` — each prints the render to stdout and exits 0; they extend the `Invocation` enum — two new arms beside `Run`/`ShowUsage` — with the parse pinned precisely, because this file's doc comment records that sloppy argv handling here once started a stray root daemon: the subcommand is matched at `arguments[0]` BEFORE the flag loop; `--ssh-port`/`--panel-port` are accepted only inside their render arm (both required, 1..=65535, `MissingValue`/`InvalidPort` otherwise); `Run` continues to refuse them as `UnknownFlag`; `--help` anywhere still wins. The RENDERING happens in `main.rs`: the arm carries the parsed ports, main calls the Task 3 render types directly and `print!`s the result — stdout output from `main` is the existing `USAGE` precedent, and no other unit prints. New `invocation_tests`: `a_render_subcommand_parses_its_own_flags_and_only_there`, `run_still_refuses_render_flags`, `a_render_subcommand_with_a_missing_port_is_refused`; `agent/tests/{cron,firewall,monitor}_on_a_real_host.rs` + fixtures; `docker/polygon/*.Dockerfile` (+nftables, +cron/cronie); **`docker/polygon/systemctl-stand-in.sh` (+)**: unit state in `/run/polygon-units/<unit>` — `start`/`stop`/`restart` write `active`/`inactive`, `is-active` prints the recorded state (default `active` for units the images start for real) and exits 0/3 accordingly — because today's stand-in ends in `*) exit 0` with NO output, and v1's monitor proposition would have "passed" against a stand-in that cannot stop anything (lesson 7). `.github/workflows/agent.yml` (+): the three suites, `--privileged` where nft needs it, same comment discipline as Plan 4's.

**Polygon propositions (each `#[ignore]`, serial):**
- cron: created entry appears in `crontab -u acc -l` as the constant line + marker; the `.cmd` holds the command verbatim; **cron RUNS it** (`* * * * *` sentinel write; wait ≤70s; sentinel exists owned by the account; output tail returns it; exit file reads 0 and its mtime is recent); a command containing `#` and `%` runs and captures (the two v1 killers, now regression-pinned on a REAL cron); a disabled entry does not run; a foreign line survives.
- firewall: apply → `nft list table inet maran` contains policy drop + loopback-first + fallback ssh allow; **allow 3306 then deny 3306 → the listing contains NO 3306 rule and the rule count equals the seeded count** (the F1 regression test, on real nft); a 22-rule displaces the fallback in the live listing; ban an address with 5s timeout → present in `nft -j list set`, absent after 6s; bans table survives a rules re-apply (add ban → apply ruleset → ban still listed).
- monitor: metrics non-zero and bounded; nginx Running → stand-in `systemctl stop nginx` → Stopped → restore.
- monitor, **the two things no gate and no unit test can see** (proposed by Task 6's implementer, who
  named them rather than leaving them uncovered): the `statvfs` block-size choice (`f_frsize` vs
  `f_bsize`, and free-vs-available — both argued in the doc comment, neither tested anywhere) and the
  four `/proc` paths, which are string literals no structure rule inspects. ONE cheap assertion closes
  both: the reported root-filesystem total is within a percent of `df --output=size /`. Add it.
- monitor, **what the committed fixtures cannot prove**: only `/proc/net/dev` is namespaced, so the
  container captures of meminfo, stat and loadavg are the HOST kernel's — the fixtures pin the FORMAT,
  not per-family data, and Docker cannot give alma9 its own kernel. The polygon is therefore the only
  place a real per-family parse is exercised: assert the metrics parse and are bounded on BOTH images
  against whatever those kernels actually emit.
- cron, **carried from Task 4** (three properties provable only on a real host): that `crontab(1)`
  accepts the rendered table on both families; that `no crontab for` is genuinely what an empty
  account prints — the implementation matches that STRING rather than an exit code and says so, which
  makes this the assertion that catches the day it changes; and that a command containing `#` and `%`
  actually runs and captures, the two characters that killed two earlier designs.

- [ ] **Step 0 — the proto deltas obey rules/proto.md's additive law, checked BY HAND because the lint cannot**: `scripts/lib/proto-lint.sh` was never a stub — it compiled every file with protoc — but it discarded the descriptor set, so it proved compilation and NOTHING about the additive law (lesson 7 — the reviewed diff was the artifact). Closed in task 22: the lint now compares the compiled contract against `proto/agent/v1/contract-baseline.txt` and refuses removals, renames, renumberings, retypings and reserved reuse by name, so this step is machine-checked and no longer by hand. Concretely: NO field is deleted, renumbered or retyped. `BanAddressRequest.reason` and `BanEntry.reason` stay in the file marked deprecated in their comments and are never read or written (removal happens at the next major, with `reserved`); `ServiceStatus.bool running = 2` STAYS and keeps being written for compatibility, and the new `ServiceState state = 4` enum field (next free number) is added beside it with the tri-state — readers prefer `state`; `uptime_seconds` stays, written as 0, comment says unproduced; `MANAGED_SERVICE_FTP` keeps its number with a comment ("reported UNKNOWN until an FTP daemon ships"). New RPCs and new fields are the additive part — and that includes `uint32 ssh_port` and `uint32 panel_port` on `AllowPortRequest`/`DenyPortRequest` (new field numbers; the agent validates both 1..=65535 and renders them per R2 — an absent/zero value is refused, never defaulted, because a defaulted 22 on a host running sshd elsewhere is the lockout this chain exists to prevent).
- [ ] Steps: proto per Step 0 → regenerate → services → status-mapping unit tests → polygon on BOTH images with pasted totals → `maran agent check`/`structure`/`handshake` green.

---

## Phase B — the panel

### Task 8: Agent clients for Cron, Firewall and Monitor

Mirrors `Services/DbService/` file-for-file (read it first; `WireTypeContainmentTests` will fail a leaked `Maran.Agent.V1` type). Exact surfaces:
- `IAgentCronClient`: `ListEntriesAsync(accountUsername, ct) → IReadOnlyList<AgentCronEntry>`; `CreateEntryAsync(accountUsername, AgentCronSchedule, command, ct) → string entryId`; `UpdateEntryAsync(…, entryId, …)`; `DeleteEntryAsync(…, entryId, ct)`; `SetEntryEnabledAsync(…, entryId, bool, ct)`; `GetEntryOutputAsync(…, entryId, ct) → AgentCronRunOutput?`; `GetEnvironmentAsync` / `SetEnvironmentAsync(…, IReadOnlyList<AgentCronEnvVar>, ct)`.
- `IAgentFirewallClient`: `ListRulesAsync`, `AllowPortAsync(port, AgentFirewallProtocol, sourceCidr, sshPort, panelPort, ct)`, `DenyPortAsync(same shape)`, `BanAsync(address, TimeSpan? ttl, ct)`, `UnbanAsync(address, ct)`, `ListBansAsync(ct)`.
- `IAgentMonitorClient`: `GetHostMetricsAsync`, `GetServiceStatusesAsync` (tri-state), `GetAccountsDiskUsageAsync` (used bytes only — quota is panel data, Task 6/Task 11).
Plus `ResilientAgent{Cron,Firewall,Monitor}Client` and registrations; mapping tests per RPC arm.

### Task 9: The Cron module

The module, concretely: no Persistence (crontab is truth); account addressed by row id, resolved in-handler (IDOR = resolution); **`Plan.MaxCronEntries`** (+migration mirroring `RenamePlanMaxFtpUsersToMaxSftpUsers`'s shape, seeds 5/20/200) checked against the AGENT's list before create; backend pre-validates schedule/command/env with mirrors of Task 1's rules; audit actions `CronEntry{Created,Updated,Deleted,EnabledChanged}` + `CronEnvironmentChanged`, success and failure, via a `CronAuditJournal` mirroring the Databases one; controller carries `IpAddress()`/`UserAgent()` helpers as every module's does; errors resx ×3; `Maran.Modules.Cron.Tests` project added to the sln the way Sftp.Tests was; integration IDOR scenarios.

- [ ] Steps: failing handler tests (create/limit/duplicate-from-agent/IDOR/audit-failure-on-refusal) → implement → integration → i18n → mutations (skip limit → red; success-audit on refusal path → red).

### Task 10: The Firewall module and the ban path

The module, concretely:
- `Domain/BanEpisode` carries `ExpiresAt` (nullable = permanent) and `Reason` — the reason lives HERE, never on the wire (R6). Manual bans (command) record an episode too.
- **`Services/StartupBanReconciler.cs`** (hosted service): on panel start, list episodes with `ExpiresAt > now || ExpiresAt == null` → `BanAsync` each with the REMAINING ttl — bans survive reboots because the panel is the durable store (R6). Idempotent by the agent's extend-on-re-ban semantics; agent unavailable at start → logged, retried by the next sampler tick hook (subscribe once, simple timer retry, capped).
- **The reported address is mapped to IPv4 before it is ever sent to the agent.** A dual-stack
  listener reports an IPv4 peer as `::ffff:a.b.c.d`, and the agent's `BanAddress` REFUSES that
  form deliberately — a mapped address placed in the `banned_v6` set matches no real traffic, so
  accepting it would produce a ban that silently does nothing. Normalise once, where the address
  enters this module (`IPAddress.IsIPv4MappedToIPv6` → `MapToIPv4()`), and test it: a ban built
  from a mapped address must reach the agent as the plain IPv4 form. Without this, every
  brute-force ban on a dual-stack host is rejected by the agent and the whole feature is inert.
- `BruteForceDetectedHandler`: whitelist check (`BanSkippedWhitelisted` audit) → episode count in 24h → TTL 15m/1h/24h → `BanAsync` → record episode (keyed ip+window start, redelivery-idempotent) → audit.
- `Options/FirewallOptions.cs`: `SshPort` + `PanelPort`, bound from configuration (`Firewall__SshPort`/`Firewall__PanelPort`, written into `panel.env` by the installer — Task 16); every `AllowPortAsync`/`DenyPortAsync` call passes both. Startup validation refuses the module coming up with either missing/zero, with a message naming the env keys — a silently-defaulted port IS the lockout (R2).
- Every endpoint admin-only, 404 to customers, matching the existing admin-gating idiom (read an admin controller's Authorization first and NAME it in the report).
- Audit: `FirewallRuleAllowed/Denied`, `AddressBanned/Unbanned`, `FirewallWhitelistChanged`, `BanSkippedWhitelisted`.

- [ ] Steps: failing tests (whitelist stops; TTL escalates; reconciler re-applies remaining ttl and skips expired; ban failure audited as failure) → implement → integration → mutations (skip whitelist → red; reconciler re-bans expired → red).

### Task 11: The Monitoring module

**Carries the Sdk quota window, with every file the widening touches named:** `AccountSnapshot` is a positional sealed record — adding `DiskQuotaMb` recompiles every constructor — and `IAccountDirectory` gains `ListAsync(ct)`. Files: `Maran.Sdk/Contracts/AccountSnapshot.cs` (+), `Maran.Sdk/Interfaces/IAccountDirectory.cs` (+), `Maran.Modules/Accounts/Services/AccountDirectory.cs` (+ the implementation — Accounts stays the data's owner), and the four test doubles `backend/tests/Maran.Modules.{Databases,Sites,Sftp,Ssl}.Tests/TestSupport/StubAccountDirectory.cs` (+), all updated IN THIS TASK so the widening lands atomically and no sibling task inherits a broken compile. `ListAsync`'s tenant semantics are stated in its doc comment, out loud, because the interface's own warning says a cross-module abstraction is where isolation gets bypassed by accident: it returns EVERY account on the host, deliberately — it exists for the admin-only host disk view, it applies no tenant scope, and the AUTHORIZATION burden therefore sits wholly on its callers, which is why Monitoring exposes it only behind admin-gated endpoints (404 to customers, like the rest of the module).

The module, concretely: the per-account disk query joins agent-reported used bytes to `ListAsync` snapshots by `Username` (both sides carry it), quota read from `DiskQuotaMb`; `monitoring.Samples` ≈10,080 rows/7d (R10); chart SQL divides by measured elapsed time and clamps negative deltas (R7) — the bucketing test seeds a counter RESET and a sampler GAP and asserts both; SMTP settings GET returns `HasPassword`, never the value; alerts dedup via `AlertStates` transitions (10 consecutive >90% samples → ONE mail); `SendMailRequestedHandler` + `SmtpMailer` (MailKit isolated to one file) — the handler CATCHES every mailer failure, audits, and returns normally: a thrown handler error would hand the token-bearing envelope to Wolverine's dead-letter machinery, which is the at-rest persistence R11 exists to prevent; the named test `a_mailer_failure_is_audited_and_never_thrown` pins it and its mutation (rethrow) must go red; `SendTestMail` to the calling admin; sampler tolerates agent-down as a gap. Audit: `SmtpSettingsSaved`, `TestMailSent`, `MailSkippedNoSmtp`, `MailSendFailed`, `AlertRaised`, `AlertResolved`.

- [ ] Steps: failing tests (bucket math w/ gap+reset; transition dedup; password never in GET; mailer refusal → `MailSendFailed`) → implement → Testcontainers integration for the SQL → mutations (drop clamp → red; drop state row → dedup red).

### Task 12: The Tasks module

The module, concretely: the stream mirrors the REAL site-log transport — `Sites/Controllers/SitesController.cs:215`'s endpoint shape and `Sites/Common/SiteLogStreamWriter.cs`'s SSE framing (`text/event-stream`, `event:`/`data:` frames, heartbeat comments) — via the module's own `Common/TaskStreamWriter.cs` (R9; same framing constants, independent file). `ITaskRecorder` in Sdk (`BeginAsync(kind, subject, correlationId, ct) → Guid`, `ReportAsync(id, percent, line, ct)`, `CompleteAsync`, `FailAsync`); recorder never throws into the instrumented operation (wrapped; a recording failure is a log line). Instrument three admin operations. **CORRECTED during Task 12: the plan named "PHP version install"
and that operation does not exist** — `IAgentPhpClient.InstallVersionAsync` is never called by any
module, and the three call sites use `ListVersionsAsync` only, so instrumenting it would have meant
writing the operation. The three instrumented are certificate ISSUE, certificate RENEW and account
deletion. The implementer checked rather than improvised, which is why this is a corrected plan
line and not a fabricated call site. Tasks list/stream admin-only (R14). Log text capped with a truncation marker.

- [ ] Steps: failing recorder tests (clamp; cap; Fail-after-Complete refused; wrap swallows) + instrumentation tests (deletion leaves ONE task; its failure carries the same error the response did) + a streaming integration test (two SSE frames read, clean cancel) → implement → mutations (break the wrap → the "never throws into" test red).

### Task 13: Identity carried items

The work, concretely:
- **ForwardedHeaders — verify, do not rebuild**: the correct configuration already ships (`ForwardedHeadersExtensions`: `ForwardLimit=1`, loopback the only trusted proxy; nginx appends the peer via `$proxy_add_x_forwarded_for`). What this tree LACKS is the tests that pin it, and R8's whole feature rests on them: a forwarded request records the CLIENT address (not 127.0.0.1); the same header from a non-loopback peer is ignored; a client-stuffed multi-entry header still yields the nginx-appended rightmost value. Add those three integration tests and change no configuration unless one fails.
- **The recorded client address is mapped to IPv4 before storage or publication.** Kestrel behind
  a dual-stack socket reports `::ffff:a.b.c.d` for an IPv4 client, so `FailedLoginByIp`, the
  session record and the `BruteForceDetected` payload all normalise (`IsIPv4MappedToIPv6` →
  `MapToIPv4()`) at the single point the address is read. Two forms of one address split the
  brute-force counter in half and make the threshold unreachable; the agent then refuses the
  mapped form outright (Task 1's `BanAddress`). One integration test pins it: a request from a
  mapped address is counted under its IPv4 form.
- **`RequestPasswordReset` is rate-limited** with the same machinery as login (read `LoginRateLimitPolicy` and mirror; own bucket, keyed by IP), because an unlimited endpoint that sends mail is a mail bomb with our return address.
- **The reset mail goes to the local non-durable queue** (R11): the token never rests in an envelope table AND the response never waits on SMTP. Tests: response equality (status+body) for known vs unknown addresses; and the decoupling test — mailer faked at 5 seconds, both requests answer in milliseconds. The report still states plainly that fine-grained timing is shaped (both paths run the token-generation work), not proven.
- Token: 32 random bytes, stored SHA-256, TTL 1h, single-use; reset revokes all sessions (reuse the Plan-2 revoke-all) and clears lockout. `PasswordResetRefused` audit on expired/used/garbage.
- Forced 2FA steering: `requiresTwoFactorSetup` response → the session may reach ONLY the enrolment endpoints — an authorization filter tested by WALKING the route table (assert every non-enrolment endpoint refuses), not three examples. This steering answers **403, the one deliberate exception** to the 404 rule: the caller is authenticated and being steered, not probed; the constraint's doc comment says so.
- Policy: `SecurityPolicy` row (min length 12, forced-2FA off, lockout 10/15min, brute-force 25/10min) + cache + save command (+`SecurityPolicySaved` audit); `User.cs` constants become seeder defaults; validators read the cache.
- **Depends on Task 11** (`SendMailRequested` + handler exist there) and Task 10 (`BruteForceDetected` contract): run after both.

- [ ] Steps: failing tests per behaviour above (incl. the spoof test and the route-walk) → implement → round-trip integration (request→captured mail→reset→old refused/new works→sessions revoked) → mutations (skip revocation → red; skip rate limit → red).

---

## Phase C — the panel's screens

### Task 14: Cron and Firewall screens

The screens, concretely: api composables are `useCronApi.ts` / `useFirewallApi.ts` (the convention all nine existing files follow); the firewall page gains **preset buttons** (Web: allow 80+443; MySQL external: a labelled toggle that allows/denies 3306 — spec's "пресеты портов" made concrete) beside the raw rule form; **SSH restriction UI**: adding an ssh-port rule and removing the last one both pass through a `UiModal` confirmation naming the lockout risk (R2's `confirm` is UI-level; the backend accepts the operation either way — the agent's fallback makes removal fail-open); the bans table shows reason (panel data) and expiry from stubbed clocks via `page.clock`; the whitelist editor notes the installer-seeded row. Cron page: builder+raw modes, env editor (reserved names hinted), entries table with the Plan-4 dropdown pattern, last-output dialog. e2e propositions: builder→fields mapping; raw-mode client-refusal sends nothing (network stub asserts zero calls); a customer sees the 404-shaped firewall screen; ban expiry countdown via `page.clock`; the SSH-removal confirm appears; presets fire the exact expected calls.

### Task 15: Monitoring, Tasks, settings, reset-by-mail screens

The screens, concretely: `UiChart` (props `series: { at: number; value: number }[]`, `label`, `unit`, `formatValue?`; inline SVG line+area with axis ticks and a hover readout; theme-aware; empty series renders an empty state, never NaN paths) with the rules/vue.md sentence from "Rules changes" §2; the monitoring page (range toggle 24h/7d, six charts — CPU, memory, disk, net rx/tx rates, load — service-status badges, per-account disk table with a used-vs-quota ratio bar from kit primitives); the tasks page + a running-tasks badge in the shell header driven by the same store, which consumes the stream through the EXISTING `useApi` SSE helper as-is (R9); the SMTP page — `UiPasswordInput` WITHOUT generate (an existing provider secret is entered, not set), `HasPassword` hint, send-test-mail button surfacing the RFC 7807 detail verbatim on failure; the security policy page with a forced-2FA warning paragraph; forgot/reset pages — the reset page's new-password field WITH generate; the auth store handles `requiresTwoFactorSetup` and the router keeps a steered admin in enrolment. e2e propositions: chart renders stubbed buckets and the hover readout formats values; the 7d toggle refetches with the range param; a task in the stream raises the badge without navigation; the SMTP form never renders a stored password; forgot-password shows identical screens for known and unknown addresses (two stubs, one equality assertion); a used token shows the refusal and a way back; a steered admin sees no nav and lands in enrolment from any URL.

---

## Phase D — proving it, and closing

### Task 16: The installer and the polygon prove firewall + cron

**Files:** `installer/lib/87-firewall.sh`, `88-cron.sh`, `install.sh` (+), `uninstall.sh` (+), **`installer/panel.env.example` (+)** — `Firewall__SshPort`, `Firewall__PanelPort`, `Firewall__SeedWhitelistCidr` documented like every variable the product reads (rules/security.md), polygon Dockerfiles (+), `assert-installer-steps.sh` (+), `installer/lib/10-preflight.sh` (+).

**The panel-port literal lives ONCE.** Closing the template-drift class moved the number into four places (the two 87 writes, nginx's `listen 8443`, preflight's `MARAN_REQUIRED_PORTS`). One authority: `MARAN_PANEL_PORT=8443` defined at the top of `install.sh`; preflight derives `MARAN_REQUIRED_PORTS` from it, 87 uses it for both writes, and `assert-installer-steps.sh` asserts the nginx vhost's `listen` line equals it — the one place that stays a file literal is thereby tied to the authority by a failing check instead of by hope.

- 87: install nftables; RHEL: `systemctl disable --now firewalld` if present (logged); **seed BOTH files before any include references them** — `nft -f` on an include whose target is missing is a hard error that aborts the whole load (verified: `Error: File not found`, rc=1), so a fresh boot with only one file present comes up with `nftables.service` FAILED and no firewall at all. `/etc/maran/firewall-bans.nft` is written first via `maran-agent render-firewall-bans`, then `/etc/maran/firewall.nft` via `maran-agent render-firewall-ruleset --ssh-port <detected> --panel-port 8443` — the agent binary is installed by an earlier step and rendering through it means ONE template source, no shell copy to drift (`assert-installer-steps.sh` asserts the installer invokes both subcommands and never writes ruleset text of its own; the earlier byte-equality idea died when the seed grew a host-detected parameter), THEN the include lines are wired into the family's `nftables_include_target()` (idempotent, marker comments; bans file first — file order is load order); enable the service and START it, then assert `nft list table inet maran` succeeds — an installer that enables a unit and does not look at it is lesson 7. **Detect the SSH port**: first `^Port` directive of `/etc/ssh/sshd_config` (default 22) → `panel.env` `Firewall__SshPort=`, and beside it `Firewall__PanelPort=8443` written by the SAME step that owns the nginx vhost number — both bound by `FirewallOptions` (Task 10) and sent on every mutation (R2). **Seed the whitelist**: when `SSH_CLIENT` is set, its first field goes into `panel.env` as `Firewall__SeedWhitelistCidr=<ip>/32` (v6 → /128) — the SAME env-file channel, because that IS the existing one-time seed mechanism (`Setup__Token=` in `60-config.sh`; v2 called it a file, which was wrong) — and the Firewall module imports it as the first whitelist row on first run, so R8 cannot ban the person who installed the panel.
- 88: cron package + enabled per family.
- uninstall: delete both live tables, remove `/etc/maran/firewall*.nft`, unwire the include lines (only ours — marker-matched), disable nftables ONLY if 87 enabled it (state marker, mirroring the existing uninstaller's decision-file pattern; if none exists yet, this task adds it).
- [ ] Steps: write → rebuild both images → ALL polygon suites both families (totals pasted) → uninstall assertions incl. running with live bans present.

### Task 17: The Definition of Done pass

- [ ] `maran licenses` — MailKit and serde_json land in the third-party notices; paste the diff.
- [ ] i18n parity: `maran structure` + backend `ResourceKeyParityTests` (auto-discovers the new resx families — name them in the report).
- [ ] IDOR sweep: every new endpoint listed with its proving test; admin surfaces return 404 to customers; the ONE 403 exception (Task 13 steering) listed as such.
- [ ] Audit sweep: table endpoint → success/failure action names, grep-verified.
- [ ] Threat note `docs/superpowers/notes/2026-XX-XX-cron-firewall-monitoring-threat-note.md`: the cron design's injection analysis (why no customer byte reaches the crontab; the `.cmd`-file trust boundary; what `fork_as_account` buys and what it does not; `.cmd`/`.exit`/mtime are account-forgeable, so last-run data is the account's own report, not an audit source); the firewall lockout layers (R1/R2, what `--check` does NOT catch — a semantically-lockout ruleset passes a syntax check; the boot-include failure mode and why both files are seeded); the ban path (a spoofed source cannot complete the TCP handshake that precedes HTTP auth — say it; whitelist seeding; the mutex and the verified element-loss race); SMTP credential handling; reset-token handling incl. why the mail queue is local and non-durable; open items (crontab-parser fuzzing not done; fine-grained timing not proven; the SSH fallback means port 22/`ssh_port` cannot be fully closed while no explicit rule exists — accepted; the proto lint checked only compilation, not the additive law — CLOSED in task 22, see the note's residual-risk list).
- [ ] Quiet-tree measurement of all five suites + both polygons; totals pasted.
- [ ] Mutation ledger: every task's score; survivors killed or accepted with reasons.

## Execution notes for the controller

- Order: 1 → 2 → 3 serial (shared files); 4, 5, 6 after 1–3, disjoint; 7 after 4–6; 8 after 7; 9, 10, 11, 12 after 8 — 9/10/11 share `AuditActions` (serialise the append or coordinate), and 11 additionally edits the four sibling `StubAccountDirectory` test doubles when it widens the Sdk window, so 11 never runs CONCURRENTLY with 9, 10 or 12 (before or after is fine — the widening is atomic within 11); **13 after 10 AND 11** (consumes both contracts); 14 after 9–10; 15 after 11–13; 16 after 7 (and re-runs after 14–15 change nothing agent-side); 17 last on a quiet tree.
- Fresh implementer per task with a brief file; adversarial review told to attack; scoped re-reviews; ledger records every ruling; models per subagent-driven-development defaults.
- The rules-file amendments (three, listed up top) are applied by Tasks 1 and 15 EXACTLY as written there; any further rules change an implementer wants is a BLOCKED escalation, not an edit.
- Nothing is proven until the browser drove it against the real stack: the Phase-C close includes a live run — create a cron entry through the UI against the polygon-backed stack and watch the real cron run it (the 70-second wait is the honest cost); fire a real ban and watch the connection die; watch a task stream during a real PHP install.

## Self-review

- Spec §11 cron: builder+raw ✓, env ✓ (R13 denylist is a narrowing the threat note records), on/off ✓, last output ✓ (content+code+mtime). Firewall: presets ✓ (Task 14 buttons + installer seed), rules port/proto/source ✓ incl. SSH via R2, whitelist ✓, auto-bans ✓. Monitoring: metrics/statuses/charts ✓, alerts ✓, SMTP ✓, per-account disk vs quota ✓ (used bytes from the agent, quota through the extended `IAccountDirectory` window). Tasks: queue UI + SSE ✓ (R9 — actual SSE framing, spec letter satisfied). §15: correlation-id on tasks ✓.
- Plan-2 carried: policy ✓, forced 2FA ✓ (403 exception documented), escalating lockout+bans ✓ (with the ForwardedHeaders fix that makes it real), reset by mail ✓ (rate-limited, no token at rest).
- Review findings 1–20 (v1 pass): closed as the second pass confirmed — 16 outright; 6 closed by R2's detected `ssh_port` + tcp-only displacement; 14 closed by R11's non-durable local queue + the decoupling test; 17 by the idiom tripwire + polygon deny test; 20's last residual (root temp file) by the scratch-dir rule in Task 4.
- Review findings 21–30 (v2 pass): 21 → `read_command_file` added with its list/duplicate consumers and tests; 22 → installer seeds BOTH files before wiring includes and asserts the live table; 23 → Task 7 Step 0 (additive-only by hand, lint honestly named a stub); 24 → R11 rewritten to local non-durable publish with the 5-second-mailer decoupling test; 25 → tcp-only `ssh_rules`; 26 → both abandoned deliberations replaced with decided prose (crontab layout now reasons about foreign env position and pins it with a positional test); 27 → R1 rewritten to the two-table truth; 28 → the cross-area import removed (v3 moved both units to agent-core; finding 33 later narrowed this — only `system_accounts` moves, as an extraction, and QuotaBlocks stays put because quota became panel data); 29 → Task 13 verifies shipped ForwardedHeaders with three new tests instead of "configuring" it; 30 → scratch-dir temp file, env-var seed channel named correctly, the ops::firewall mutex (verified element-loss cited), and the threat note (Task 17) states that `.cmd`/`.exit`/mtime are account-writable so the cron UI reports what the ACCOUNT'S OWN runs left behind — informational, not authoritative, no escalation.
- Review findings 31–35 (v3 pass): 31 → the two ports are installer-known host facts delivered via `panel.env` → `FirewallOptions` (startup-validated, never defaulted) → two NEW additive proto fields → template; R2 now names the 5080-vs-8443 trap outright; 32 → File-structure block rewritten to match Step 0; 33 → quota comes from the panel's own data (no cross-area import, no QuotaBlocks move), `system_accounts.rs` is an extraction with a measured-equal-totals refactor, rules-change §4 shrunk to one honest row; 34 → the installer seeds through `maran-agent render-firewall-*` subcommands — one template source, the drift class dissolved, and `assert-installer-steps.sh` asserts the invocation instead of a byte-equality that no longer can hold; 35 → the mutex, the UDP-non-displacement and the never-throw mail handler each gained a named test and a mutation that must go red.
- Review findings 36–40 (v4 pass): 36 → quota flows through the lawful Sdk window (Task 11); 37 → the two stale used+quota consumers corrected; 38 → `panel.env.example` documents the three new keys and `MARAN_PANEL_PORT` became the single authority with the nginx `listen` line tied to it by a failing check; 39 → the `Invocation` parse pinned (subcommand at `arguments[0]`, render flags only in their arm, `Run` refuses, help wins) with three named tests; 40 → `main.rs` is the one printing site, on the `USAGE` precedent.
- Review findings 41–42 (v5 pass): the Sdk widening moved out of the agent task into Task 11 with all five touched files named (positional-record recompile included), `ListAsync`'s no-tenant-scope semantics and caller-side authorization burden stated in so many words, the File-structure Sdk line updated, and the execution notes now forbid running 11 concurrently with its stub-sharing siblings.
- Placeholder scan: the two remaining "verify on polygon" points from v1 are now VERIFIED FACTS baked into Tasks 2/5; no TBDs remain.
- Type consistency: `ITaskRecorder`, `BruteForceDetected`, `SendMailRequested`, `AgentCron*` names identical at definition and every consumption site; Task 13's dependency edge corrected to 10+11.
