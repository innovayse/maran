# Reviewer packet — `fix/live-findings`, 25 threat and decision notes

**What this is.** `rules/security.md` ("Sensitive change escalation") requires a written threat note
before a privileged change and a **second human reviewer** before it merges to `main`. This branch
has 25 notes and every gate is green, so the only thing left is the reading. **Nothing in this file
discharges that requirement.** It exists to take every fact-check off the reviewer's hour, so the
whole hour goes on the one judgement only a human makes: *is this risk acceptable.*

**Ordering principle: by what a human's attention is worth most on, hardest first.** Not by filename
and not by date. Section 1 is what no test can observe — decisions, not measurements. Section 2 is
what is missing. Section 3 is what a note claims and the tree denies. Only then the per-note
verdicts, which are the cheap part.

**How to read a verdict.** Every VERIFIED below names a grep or a line that **could have returned
the other answer**. A citation on its own is not evidence: this branch produced fifteen confidently
stated facts in one day that did not survive a grep. Where evidence lives in an `#[ignore]`d polygon
suite, that is said and nothing is claimed.

**Status of the notes as files.** 25 notes; **15 tracked, 10 untracked**; and 6 of the 15 tracked
ones carry uncommitted modifications which ARE their correction blocks. So a reviewer who pulls the
branch **as committed sees 15 of 25 notes, and sees 6 of those without their corrections** — reading
claims already known to be false. Committing is the owner's call; the packet's job is to say this
plainly. The untracked ten are listed in §5.

---

## 1. Read first: the risks no test can observe

Each is a decision. No check in this repository can turn any of them green.

1. **An authenticated session outlives the suspension, on both protocols — and only one half is
   written down.** FTPS: the note states the gap and bounds it (`2026-09-09-ftps-threat-note.md:170`
   "An FTPS session that … nothing mid-session. The bound is `idle_session_timeout=600`"), which the
   tree confirms — `templates/vsftpd/vsftpd.conf.j2:84 idle_session_timeout=600`. **SFTP: there is no
   bound and no sentence.** `grep -rn "ClientAlive" installer/ agent/crates/templates/templates/`
   returns **nothing**, and `installer/lib/86-sftp.sh:123-125`'s `Match Group` block sets only
   `ChrootDirectory %h` and `ForceCommand internal-sftp`. Nothing anywhere kills a live session:
   `grep -rn "pkill|kill_sessions|terminate_session|loginctl" agent/crates/ops/src` → no hits. So an
   already-open SFTP session survives a suspension **indefinitely**, and the note covering SFTP
   suspension does not say so. *For the human:* accept an unbounded window, or require an
   `ClientAliveInterval` and a session cull. *This is new since the 2026-09-09 pass, which recorded
   the FTPS half only.*
2. **A group's membership.** `2026-09-09-ftps-threat-note.md:164` — "Membership of `maran-ftps` is
   the whole authorization"; `2026-09-09-group-id-threat-note.md` §1 — who is in the web server's
   group decides who reads a restored home at `0750`. No test can assert who is in a group on a
   customer's host; it is neither code's input nor its output. Closed by a human agreeing with the
   layout, or not at all.
3. **Is `maran` a permanent name in `/etc/passwd`, and should `MARAN_ADOPT_EXISTING_USER=1` exist?**
   Still a real bypass, verbatim in the tree: `installer/lib/40-user.sh:63` `if [
   "${MARAN_ADOPT_EXISTING_USER:-}" = "1" ]; then` — one environment variable hands an existing
   account to Maran. Named as an owner decision at
   `2026-09-09-service-account-rename-threat-note.md:270`.
4. **Has a shipped install already been hit by the cross-tenant `userdel` this branch fixes?**
   (`2026-09-09-transfer-login-deletion-threat-note.md`.) Nothing in the repository can answer it —
   `grep -rn "already been hit|shipped install" docs/superpowers/notes/` returns nothing, and the
   agent records no history that would. A forensic question for the owner, not a code question.
5. **`ABSENT` accepted on reactivation** (`2026-09-08-account-password-state-attestation-threat-note.md`),
   which that note itself calls "exactly what one author cannot check for themselves".
6. **Finish-forward vs roll-back for an interrupted restore**, plus the rollback-set cap of 4 versus
   an age bound (`2026-09-09-restore-interruption-recovery-threat-note.md`). A promise about a
   customer's data.
7. **No orphan sweep exists**, in either the SFTP or the FTPS shape
   (`2026-09-09-account-deletion-exclusion-threat-note.md`): a host already carrying an orphan hands
   the old customer's password to the next tenant of a recycled name.
8. **The customer loses raw SFTP access to their own logs** (`2026-09-09-site-logs-threat-note.md`
   §2.2) — a product decision; that note asks to be challenged on leaving a planted symlink in place
   rather than moving it aside.
9. **SELinux enforcing on AlmaLinux / AppArmor on Ubuntu** — named as unverified by the `site-logs`
   and `ftps` notes both.
10. **Whether the `maran` uid can read journald**
    (`2026-09-09-agent-error-seam-and-path-claim-threat-note.md`) — decides whether that whole change
    buys anything.
11. **The three `unsafe` blocks in `privs/group_id.rs` are POSIX contracts.** The note says the
    honest thing: a passing test proves the libc on the machine that ran it behaved, and Miri does not
    run FFI. Its reviewer items 1 and 2 are grep-shaped **because a grep is the only mechanical check
    the property has** — and both greps are discharged in §4 below, so the reviewer need only decide
    whether grep-shaped is enough.

---

## 2. What is missing

### 2.1 The setup token now travels inside a URL, and still has no note

`rules/security.md` escalation covers "session/token handling", and item 8 singles this token out —
"permission to become the administrator". The delivery changed:

> `installer/lib/90-finish.sh:44`
> `local setup_url="https://${hostname}:${MARAN_PANEL_PORT}/setup?token=${token}"`

**What is safe, verified:** it never reaches a log. `:46` writes to `>&3` and `:48` to `/dev/tty`,
and `:33-35` states why ("so it never lands in /var/log/maran/install.log, which outlives the
install"). **What is new:** the operator is now guided to paste a credential into a browser address
bar — browser history, and any URL-handling telemetry. The argument for it exists **only as a shell
comment** (`:36`), not as a note. `grep -rn "setup?token|setup_url" docs/superpowers/notes/` →
**no hits**, unchanged from the 2026-09-09 pass that first flagged it. *For the human:* rule on
whether this clears the bar for token handling; if yes, the comment should become a note.

> **CORRECTION, 2026-09-11 (later the same day).** "What is safe, verified: it never reaches a log"
> above is WRONG, and it was the second pass on this branch to say so. It is true of
> `/var/log/maran/install.log` and of that file only. `installer/nginx/maran.conf` set no
> `log_format`, so the panel's own nginx used its built-in `combined` and logged `"$request"` —
> query string included — into `/var/log/maran/nginx-access.log`, a file in a directory readable by
> every uid in the `maran` group that nothing rotated. Measured in the Ubuntu 24.04 polygon with the
> installer's own step 80 installing the shipped vhost and the distribution's own nginx serving it:
> `"GET /setup?token=PROBETOKEN1234deadbeef HTTP/2.0" 200`, with a token-free request logged
> without it as the control. nginx's ERROR log carries the raw request line too and can be given no
> format at all. Both are now closed: the token no longer travels in a URL and the vhost logs no
> query string — `docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md` (with its
> "Correction and resolution" section) and `.superpowers/sdd/setup-token-log-report.md`. The
> reviewer requirement this section asks for is **still OUTSTANDING**: only the leak is fixed, not
> the review.

### 2.2 An unexplained `SHA1` in production auth code

`backend/src/Maran.Modules/Identity/Services/TotpService.cs:41` emits
`…&algorithm=SHA1&digits=6&period=30`. This is almost certainly **correct** — RFC 6238's default,
which every authenticator app assumes, and HMAC-SHA1 in TOTP is not the collision-resistance use
`rules/security.md` item 9 bans. But it is the **only** MD5/SHA-1 occurrence in the production tree
that carries no justifying sentence: every other one does (`RefreshTokenHasher.cs:15`,
`PasswordResetTokenHasher.cs:15`, `ops/src/backup/archive/checksum_file.rs:22`,
`extract_databases_as_root.rs:47` all cite item 9 and say why). `2026-08-30-auth-threat-note.md`
mentions no algorithm for TOTP at all (`grep -n "SHA|algorithm|HMAC"` → one line, about refresh
tokens). *A reviewer grepping for banned crypto will stop here and have to re-derive it.* One
sentence in the method's doc closes it; the code needs no change.

### 2.3 A candidate, lesser: `TransferLoginProtocolPolicy` has no note

New on 2026-09-11 (`backend/src/Maran.Modules/Accounts/Domain/Policies/TransferLoginProtocolPolicy.cs`),
it words half the suspension attestation — the half the `sftp-password-suspension` note asks the
reviewer to police. `grep -rln "DescribeAll|per DAEMON" docs/superpowers/notes/` → no hits. Its
sibling `UnmanagedLoginPolicy` **is** covered. Not an escalation surface by the letter of the rule;
listed so the reviewer can decide, not so it reads as a violation.

### 2.4 Ruled out — surfaces that moved since 2026-09-09 and are covered

Checked one by one against the notes, so the reviewer need not:

| Surface that changed | Covered by | Verdict |
|---|---|---|
| `agent/src/peercred/peer_policy.rs` (auth: the only admitted uid) | `panel-socket`, `loopback` | **doc-only change** — the diff is `panel` → `maran` in a comment; `allow_uid` and its comparison are untouched |
| `agent-core/src/validation/fs/path.rs` | `agent-error-seam-and-path-claim` | **doc-only**, and it is that note's own fix: the module header now says it is *not* the containment, the descriptor walk is |
| `ops/src/logins/` (new module) | `sftp-password-suspension` :196, `account-deletion-exclusion` :295 | covered; see §3.3 for its stale path |
| `installer/lib/89-ftps.sh` (new privileged step) | `2026-09-09-ftps-threat-note.md:10, :201, :275` | covered, by name |
| `agent/src/shutdown/stop_signals.rs` (new) | `agent-shutdown` | covered in substance, **stale in the file name** — §3.1 |
| TOTP, recovery codes, refresh rotation, session revocation | `2026-08-30-auth-threat-note.md` :14, :86, :92, :102, :106 | covered, and re-verified in §4 |

---

## 3. Notes whose claim fails against today's tree

A note describing the code as it was is worse than none: it reads as verified.

### 3.1 `2026-09-08-agent-shutdown-threat-note.md` — its **own correction** has gone stale in two days

The 2026-09-09 correction fixed a stale path and introduced another:

> "The unit is a directory: `agent/crates/agent/src/shutdown/{mod.rs, shutdown_signal.rs,
> drain_deadline.rs}`"

`ls agent/crates/agent/src/shutdown/` → **`drain_deadline.rs  mod.rs  stop_signals.rs`**.
`shutdown_signal.rs` is deleted (` D` in `git status`); `stop_signals.rs` is new and untracked. The
argument is unaffected; the map is wrong. **This is the packet's own best argument**: a note needs a
currency check every time the code moves, not one correctness pass.

### 3.2 Four notes name a service group no installed host has, with no correction at all

Tree: `installer/install.sh:57-58 MARAN_USER=maran / MARAN_GROUP=maran`; `rules/security.md` item 8
agrees. Mentions of `panel:panel` / `root:panel` / "the `panel` user", and whether the note
acknowledges the rename:

| Note | mentions | acknowledges |
|---|---|---|
| `2026-09-07-installer-privileged-steps-threat-note.md` | **11** | **no** |
| `2026-09-03-panel-socket-threat-note.md` | 5 | **no** |
| `2026-09-05-backups-threat-note.md` | 4 | **no** |
| `2026-09-03-loopback-trust-boundary.md` | 1 | **no** |
| `2026-09-09-site-logs-threat-note.md` | 7 | yes (2026-09-09) |
| `2026-09-08-agent-shutdown-threat-note.md` | 3 | yes (2026-09-09) |
| `2026-09-09-ftps-threat-note.md` | 2 | yes (2026-09-10, `:413`) |
| `2026-09-09-service-account-rename-threat-note.md` | 11 | it *is* the rename note — correct as written |

The arguments survive the rename in every case (a hosting account is in neither group). What does not
survive is a reviewer checking a table against a real host and finding no such group.

### 3.3 `2026-09-09-sftp-password-suspension-threat-note.md` — two stale details

- Its FTPS addendum's "everything above applies to `ops::ftps` unchanged" still **fails** on the
  by-construction no-strings half: `grep -c String agent/crates/ops/src/ftps/ftps_error.rs` → **5**.
- `:4` and `:24` name `ops::sftp::set_account_logins_locked`; the unit is now
  `ops::logins::set_account_logins_locked` (`agent/crates/ops/src/logins/set_account_logins_locked.rs`).
  Its own `:196` gets this right, so the note contradicts itself across its addendum boundary.

### 3.4 Discharged since 2026-09-09 — do not spend time here

Each re-checked, each could have gone the other way:

- `follow_log.rs` doc defect — **FIXED**: "proves `uid` owns it. / `uid` is `LOG_OWNER_UID` — ROOT,
  not the account's".
- `rules/rust.md` "three waiting locks" — **FIXED**: `:813` "The **four** waiting locks are leaves."
- `panel:panel` in `ops/src/ftps/mod.rs` — **gone**; the only remaining hit is
  `installer/install.sh:146`, deliberate history ("used to be created panel:panel 0750").
- `service-account-rename`'s cited polygon assertion — **correct now**:
  `docker/polygon/assert-installer-steps.sh:4130 assert_the_legacy_service_account_is_migrated_in_place()`.
- `maran structure` — was 72 violations (all `locales/hy: missing key ftp.*`), now `STRUCTURE-OK`.

---

## 4. Contradictions between notes

**The one that mattered is now resolved in-note.** `account-deletion-exclusion:291` called
`set_ftps_password` "crate-private and NOT a customer-facing password change" while
`sftp-password-suspension` called it a new customer-facing rpc. The tree agrees with the second —
`ops/src/ftps/set_ftps_password.rs:175 pub fn set_ftps_password`, `proto/agent/v1/ftp.proto:273 rpc
SetFtpsPassword(...)` — and the first now carries `:311 ## Correction, 2026-09-09 —
set_ftps_password is no longer crate-private`. **Caveat: that note is untracked**, so a reviewer on
the committed branch sees neither the note nor its correction.

**The residual disagreement is §3.2**: three notes say the service group is `maran`, four say
`panel`, and a reviewer trusting the wrong set checks a name that does not exist. Also still live:
`2026-08-31-log-tail-openat.md` says the log tail checks the *account's* uid, `site-logs` says
*root's*; both describe `follow_log.rs`; only the second is true. Both notes carry corrections
saying so — **but the `log-tail-openat` correction is one of the six uncommitted modifications**, so
on the committed branch that note reads as uncorrected and superseded-without-saying-so.

---

## 5. Which notes exist only in the working tree

**Untracked (10)** — a reviewer pulling the branch as committed sees none of these arguments:

`2026-09-09-account-deletion-exclusion`, `2026-09-09-agent-error-seam-and-path-claim`,
`2026-09-09-ftps`, `2026-09-09-group-id`, `2026-09-09-identity-account-deletion`,
`2026-09-09-restore-interruption-recovery`, `2026-09-09-service-account-rename`,
`2026-09-09-sftp-password-suspension`, `2026-09-09-site-logs`, `2026-09-09-transfer-login-deletion`
(all `-threat-note.md`).

**Tracked but with uncommitted corrections (6)** — visible, and visible *wrong*:
`2026-08-31-log-tail-openat`, `2026-09-02-databases-sftp`, `2026-09-03-plan-5-licence-pass`,
`2026-09-04-cron-firewall-monitoring`, `2026-09-05-backups`, `2026-09-08-agent-shutdown`.

`rules/security.md` requires the note to "land in `docs/superpowers/notes/` **as a file**, not as
prose in a report that only one session ever reads". Ten of them are a file only in this tree.

---

## 6. Every promise a note makes to the reviewer, and whether it is met

| Note | Promise | Met? |
|---|---|---|
| `sftp-password-suspension` | **"be sure the panel SHOWS the number rather than swallowing it"** | **YES, and now test-held — the headline change since 2026-09-09.** See below. |
| `group-id` | 1. `GroupId` constructible only through `resolve` | **MET** — `group_id.rs:46 pub struct GroupId(libc::gid_t);` and exactly one `impl GroupId` at `:48`; no `From`, no `Default`, private field. A second `impl` would have shown. |
| `group-id` | 2. nothing reads a `libc::group` field but `gr_gid` | **MET** — `grep -c "gr_name\|gr_passwd\|gr_mem" group_id.rs` → **0**. |
| `group-id` | 3. follow `home_group` → `Placement::group` → `finalise` and rule on the window | **chain VERIFIED, decision open** — `backup_service.rs:442 let group = home_group(distro)?;`, `restore_backup.rs:254 group: home_group`, `:744 chown(&home, …)`. The window is real; the ruling is the human's. |
| `group-id` | 4. `finalise` cannot `chown` anything but the home root | **claim VERIFIED, judgement open** — `restore_backup.rs:4 use std::os::unix::fs::{…, chown}` (symlink-following) applied at `:744` to `placement.home_root.join(account)`. `lchown` + `O_NOFOLLOW` remains the honest form; the note says so itself. |
| `group-id` | 5. no caller treats `PrivError::NoSuchGroup` as recoverable | **MET** — `grep -rn NoSuchGroup agent/crates --include=*.rs` outside tests hits only `group_id.rs` and `priv_error.rs:67`; the sole production consumer, `services/backup/home_group.rs`, maps every failure to `system_failure` with **no fallback**. A fallback to the account's own group would have appeared in that grep. |
| `group-id` | 6. agree that a customer home is group-owned by the web server's group at `0750` | **open — product decision** |
| `auth` | refresh reuse is detected | **MET** — `RefreshSessionCommandHandler.cs:16` "including reuse detection", `:53 _sessionService.RotateAsync` |
| `auth` | another user's session answers 404, not 403 | **MET** — `RevokeSessionCommandHandler.cs:48 … s.Id == command.SessionId && s.UserId == command.UserId`, `:52 ErrorType.NotFound`. Drop the `&& s.UserId` and the claim is false; it is there. |
| `auth` | TOTP window is `(previous: 1, future: 0)` and a code cannot be replayed | **MET** — `TotpService.cs:58 new VerificationWindow(previous: 1, future: 0)`, plus a last-accepted-window guard at `:63-70` |
| `auth` | Argon2id is the only password hash | **MET in substance** — `Argon2idPasswordHasher`; the two `SHA-256` token hashers are full-entropy secrets and each says so. See §2.2 for the one unexplained `SHA1`. |
| `site-logs` | 4 machine-checkable checks (path unreachable, root in both places, `lstat`/`readlink` only, logrotate) | **MET** (verified 2026-09-09; nothing under it moved except the doc fix in §3.4) |
| `transfer-login-deletion` | 5 checks | **MET** (5/5) |
| `ftps` | 5 checks | **MET** (5/5) |
| `restore-interruption-recovery` | 3 checkable checks | **MET**; the "finish forward" one is §1.6 |
| `account-password-state-attestation` | suspension refuses on `EMPTY` **and** `USABLE`; reactivation only on `LOCKED` | **MET** |
| `account-unlock` | nothing on the new path can print the shadow field | **MET — by a type**, a fieldless enum |
| `identity-account-deletion` | every `UserId`-keyed table has an observed-absence assertion | **MET**, by named tests |
| `service-account-rename` | name has one authority; a foreign account refused with an inverse control; legacy account migrates in place | **MET** (the cited assertion name is correct now — §3.4) |
| `agent-error-seam` | reword `rules/security.md` item 2 | **DONE**, and the module header now matches (§2.4) |
| `plan-5-licence-pass` | regenerate `THIRD-PARTY-NOTICES.md` | **MET** as of 2026-09-09 (`NOTICES-OK`); not re-run here |

### The promise that was the cautionary tale, and is now closed

On 2026-09-09 this promise was met *end to end and held by no test* — the exact shape that let a
panel swallow a number for hours. **Two named tests have landed since:**

- `backend/tests/Maran.Modules.Accounts.Tests/Domain/Policies/UnmanagedLoginPolicyTests.cs` — five
  behaviour-sentence tests, including
  `The_count_of_logins_the_panel_does_not_own_is_named_on_the_attestation` and
  `A_count_the_host_never_gave_is_stated_as_unknown_and_never_as_zero`.
- `frontend/e2e/tasks/log-fidelity.spec.ts` — **"the whole of what the task reported reaches the
  screen, character for character"**, whose fixture carries the literal
  `"and 2 login(s) sharing this account's uid that the panel did not create and does not lock."`
  Its own comment at `:56` names itself "the LAST link of the chain that carries the number".

The number's route is `accounts.proto:355 uint32 unmanaged_logins = 9` →
`accounts_service.rs:421 unmanaged_logins: state.logins.unmanaged` →
`AccountSuspensionStateDto.UnmanagedLogins` → `UnmanagedLoginPolicy.Describe` →
`SuspendAccountCommandHandler.DescribeAttestation` → the task log → the screen. **The SPA holds no
`unmanagedLogin` identifier at all** (`grep -rn unmanagedLogin frontend/src` → nothing), which is
correct rather than alarming: `rules/architecture.md` makes the backend own the text, so the SPA
renders a backend sentence. That is *why* the e2e assertion is character-for-character.

---

## 7. Per-note verdict, one line each

Heaviest first. Where a verdict is inherited from the 2026-09-09 pass rather than re-derived here, it
says so — that pass's method was sound and its per-note evidence stands, but a reviewer should know
which lines were re-checked against today's tree and which were not.

| Note | Verdict | The line that settles it |
|---|---|---|
| `group-id` (untracked, **never reviewed by any pass before this one**) | **VERIFIED on all five grep-shaped checks**; three judgements left | `group_id.rs:46` one private-field struct, `:135 if resolved.gr_gid == 0 { … RootGroup }`, 3 `unsafe`; tests incl. the inverse control `a_group_this_host_has_resolves_to_the_gid_its_database_records` |
| `sftp-password-suspension` | VERIFIED; **its promise now test-held**; two stale paths (§3.3) | `log-fidelity.spec.ts:20` carries the literal count sentence |
| `ftps` | VERIFIED, 5/5, plus a 2026-09-10 self-correction | `:170` states the session gap and `vsftpd.conf.j2:84` confirms the bound |
| `site-logs` | VERIFIED; its one wrong fact (group `panel`) is corrected in-note; its doc defect is now fixed | `agent_paths.rs SITE_LOG_ROOT = "/var/log/maran/sites"` |
| `transfer-login-deletion` | VERIFIED, 5/5 *(2026-09-09, not re-derived)* | — |
| `restore-interruption-recovery` | VERIFIED, 3/3 checkable *(2026-09-09)* | — |
| `account-deletion-exclusion` | VERIFIED except the claim its own correction retracts | `set_ftps_password.rs:175 pub fn` |
| `service-account-rename` | VERIFIED; its cited assertion name is now right | `assert-installer-steps.sh:4130` |
| `installer-privileged-steps` | VERIFIED on 4 load-bearing claims *(2026-09-09)*; **6 `OUTSTANDING` markers, the most on the branch**; 11 stale `panel` names | `50-artifacts.sh:21`, `30-postgresql.sh:150 pg_assert_no_tcp_listener` |
| `auth` | **VERIFIED against today's tree on 4 claims** (§6); one unexplained `SHA1` (§2.2) | `RevokeSessionCommandHandler.cs:48` |
| `account-password-state-attestation` | VERIFIED both directions *(2026-09-09)*; §1.5 is the judgement | — |
| `account-unlock` | VERIFIED — mitigation is a **type** | fieldless enum |
| `identity-account-deletion` | VERIFIED; a *late* note, and it says so | — |
| `agent-error-seam-and-path-claim` | VERIFIED; its outstanding half is **done**, in the rules and now in the module header | `validation/fs/path.rs` header |
| `agent-shutdown` | VERIFIED in substance; **its own correction is stale** (§3.1) | `ls src/shutdown/` |
| `privs` (2026-08-30) | current; its coverage list was incomplete and `group-id` now fills the hole | — |
| `panel-socket`, `loopback-trust-boundary` | current in substance; both name `panel` throughout | `peer_policy.rs` one `allow_uid`, doc-only diff |
| `log-tail-openat` | **SUPERSEDED**; corrected in-note, but the correction is **uncommitted** | `follow_log.rs:135 const LOG_OWNER_UID: u32 = 0` |
| `databases-sftp`, `backups`, `cron-firewall-monitoring` | stale paths, each corrected in-note, **all three corrections uncommitted** | — |
| `plan-5-licence-pass`, `backup-object-store-seam`, `open-owner-decisions` | **not threat notes** — a licensing pass and two decision documents. Read, do not verify; they assert no mitigation. | — |

**What each mitigation rests on** — the project prefers a type, accepts a check, and distrusts
discipline. Honestly: **one type** (`account-unlock`'s fieldless enum; `GroupId`'s private field is a
second, though its gid-0 refusal lives in `resolve` and a future `From` impl would repeal it
silently, which is why that note's check 1 is a grep). **Most are checks at one place** — the two uid
comparisons in `follow_log`, the jail comparison in the four delete/password paths, the PAM stack,
the `assume_init` guard. **Two rest on discipline a future contributor can break silently, and should
say so**: the resolve-then-`chown` ordering in the restore (nothing prevents a longer extraction
widening the window), and `home_group`'s refuse-don't-fall-back (a future fallback compiles fine and
is caught only by a human running that note's check 5).

---

## 8. The tests that should exist, named precisely enough to write

None of these is built here.

1. `a_group_recreated_during_a_restore_is_not_applied_from_the_stale_number` — polygon test in
   `agent/crates/agent/tests/backup_on_a_real_host.rs`. Create the account, start a restore large
   enough to hold the window open, `groupdel` then `groupadd` the web server's group so it takes a
   new gid, assert the home ends owned by the **new** gid or the restore refuses. Named by the
   `group-id` note itself; **would fail today**, which is why it is named and not written (fixing it
   is behaviour, and that note is comments only). Confirmed absent:
   `grep -rn a_group_recreated_during_a_restore agent/` → no hits.
2. `a_web_server_group_the_host_does_not_have_refuses_the_restore` — unit test for
   `agent/crates/agent/src/services/backup/home_group.rs`, which today has **no test file at all**
   (`find agent -name home_group_tests.rs` → nothing). It is the test that would catch the fallback
   the `group-id` note's check 5 exists to prevent, converting a grep-shaped reviewer instruction
   into a gate.
3. `an_authenticated_sftp_session_does_not_survive_a_suspension` — polygon test in
   `agent/crates/agent/tests/sftp_on_a_real_host.rs`: open an SFTP session, suspend the account,
   assert the session can no longer read. It would **fail today** (§1.1), so it is a specification of
   the decision, not a regression test — write it only after the human rules on the window.

---

## 9. What gates this packet: nothing

Said plainly, per `rules/testing.md` ("a check must observe what it reports on"), applied to this
file. No CI job reads it, no gate fails if it is wrong, deleted, or goes stale next week — exactly
the property this packet criticises in the notes. Its only protection is that every verdict names the
command that produced it, so any claim here can be re-run in one line.

**Could a check detect a privileged change arriving with no note?** Partly, and its blind spots are
the ones that matter. A script could diff the branch against its merge base, select paths matching the
escalation surfaces (`agent/crates/agent-core/src/privs/**`, `installer/lib/**`, Identity's
session/token code, licence verification), and fail when no file under `docs/superpowers/notes/`
mentions the changed unit. That would have caught `privs/group_id.rs` in September. It is **blind to
a note that exists and is wrong** (§3), blind to **a promise nobody kept** (§6), and blind to the two
findings in §2.1 and §2.2, where the code is fine and the *argument* is missing. So such a gate
would raise the floor and must never be read as the reviewer. **Deliberately not built here.**

## 10. Verification of this packet itself

Run after `source scripts/dev`, no code changed:

- `maran structure` → `STRUCTURE-OK`
- `maran agent check` → `AGENT-CHECK OK — fmt, clippy, test and doc all ran and all passed`
- `maran test rust` → `27 targets: 1838 passed / 0 failed / 110 ignored`,
  `TEST VERDICT: OK — every baselined target reported, no failures, no total dropped.`

Target count checked, not only the total. **What these cannot see:** 110 tests are `#[ignore]`d
polygon suites, and much of the 2026-09-09 notes' evidence lives in exactly those; and the harness
prints its own blind spot — `UNOBSERVED HERE: the baseline comparison is a comparison of COUNTS`, so
a target that loses one test and gains another in the same change is invisible to it.
