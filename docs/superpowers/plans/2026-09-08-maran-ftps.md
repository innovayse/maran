# FTPS (vsftpd) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **CHECKBOX STATUS — set 2026-09-11 by the second Definition-of-Done pass
> by the second Definition-of-Done pass, and this note says exactly what the ticks mean.**
>
> **83 of this plan's 84 steps are ticked.** Every step of Tasks 1-15 and Task 16 Steps 1, 2, 4 and
> 5. A tick here means **the step's deliverables were found in the working tree and every repository
> gate was green over them** — not that a pass watched a red-then-green history. `rules/testing.md`
> does not require one, so the distinction is recorded rather than treated as a gap.
>
> What the ticks rest on, re-measured 2026-09-12 (Step 2's own block carries the full list):
> `maran structure` → `STRUCTURE-OK`; `maran proto` → `PROTO-OK`; `maran api` → `0 failures`;
> `maran lock selftest` → `LOCK SELFTEST OK` (7/0); `maran migrate check` → `MIGRATIONS-OK`;
> `maran migrate guard` → `MIGRATIONS-ADDITIVE`; `maran format --check backend` →
> `FORMAT VERDICT: OK`; `maran agent check` → `AGENT-CHECK OK`; `maran handshake` → `HANDSHAKE-OK`;
> `maran licenses --check` → `NOTICES-OK`; `maran test rust` →
> `27 targets: 1838 passed / 0 failed / 112 ignored`, `TEST VERDICT: OK`; `maran test backend` →
> `19 targets: 2969 passed / 0 failed`, `TEST VERDICT: OK`; `maran test spa` →
> `71 targets: 359 passed / 0 failed / 1 ignored`, `TEST VERDICT: OK`; and — new on 2026-09-12, the
> blocker that had kept Step 2 unticked — `maran polygon verify` on **both** families:
> `13 suites discovered, 111 tests executed … exactly the 111 the baseline declares`,
> `POLYGON VERDICT: OK`.
>
> **The one unticked step is Task 16 Step 3**, and it is owner-gated by its own wording: it asks for
> `scripts/test-baseline.txt` to be refreshed **in its own commit**, because a baseline landed
> alongside the code it counts is a baseline nobody reviewed. The rows are done and measured; the
> separate commit is the owner's, and only the owner commits. Nothing else in this plan is owed to an
> implementer.
>
> **The numbers above are a working-tree measurement, not a committed one.** At the time of writing,
> 161 production source files of this feature — the whole `Ftp` module, `installer/lib/89-ftps.sh`,
> the migrations and the SPA's File transfer screen — are untracked, so CI has never built the tree
> these gates scored. That is the owner's to resolve and it is the largest standing risk on the
> branch, ahead of any defect in the code.
>
> **Two counts inside this plan's own instructions are stale against the tree and are NOT edited
> here, because a count inside a requirement is the owner's to change:** Task 16 Step 1 says "seven
> HTTP endpoints (three on `ftps-server`, four on `ftp-users`)" where the tree has **eight** (five
> on `ftp-users`: GET, GET `{id}`, POST, POST `{id}/password`, DELETE `{id}`); and the File-structure
> table puts **five** audit action names in `Maran.Sdk/Contracts/AuditActions.cs` where
> `FtpAuditJournal` holds **six**, and holds them itself rather than in the Sdk.

**Goal:** An operator can turn FTPS on for a server, and a hosting account can then hold FTPS logins beside its SFTP ones — each login writing as the account's own uid, confined to a root-owned jail, refused unless it negotiates TLS, and holding a password the panel has never seen and cannot reveal.

**Architecture:** The shape every plan since Plan 3 has used, unchanged. The Rust agent owns every system mutation and every path; `agent-core` validates input into types that cannot hold an invalid value; `templates` renders every config file and `safe_write` applies it (render → atomic rename → validate → reload, roll back on refusal — the validator is run against the file at the path the daemon reads, which is why it follows the swap; an earlier pass of this plan stated the two steps the other way round); `distro` is the only crate that may name a distribution; the C# panel holds the rows, the plan limits and the audit trail and reaches the agent only through typed clients behind their resilience pipelines. One new panel module, `Ftp`, lands in the skeleton folder that has been waiting for it since day one, beside the shipped `Sftp` module rather than inside it.

**Tech Stack:** Rust (tonic, askama), C# .NET 9 (EF Core, Wolverine), PostgreSQL for the panel's own rows, vsftpd 3.0.5 as the FTPS daemon, OpenSSL-backed TLS from the agent's own certificate store, nftables for the ports, Vue 3 + TypeScript for the screens.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md` — §11 (FTP/SFTP: *«SFTP по умолчанию … FTPS через vsftpd: виртуальные юзеры на UID аккаунта, chroot в подпапку, TLS обязателен»*), §9 (agent contract and safety), §8 (tenancy, roles, limits), §15 (audit).

**Tracked as:** GitHub issue #20, "FTPS (vsftpd) — deferred from the SFTP/databases plan". The prerequisite it names — issue #4, the databases + SFTP plan — has landed: `docs/superpowers/plans/2026-09-01-maran-databases-sftp.md` (the issue cites the file under its pre-rename name `2026-09-01-maran-databases-ftp.md`; there is no such file, and the SFTP half is what shipped).

## The owner's decisions, made before this plan was written

These three are settled. They are recorded here as the owner's, so that nobody re-opens them in review and so that the reasoning survives the person who had it.

1. **One server certificate for the whole panel host, not one per account.** vsftpd holds a single `rsa_cert_file` per daemon. A per-account certificate would therefore mean a vsftpd daemon per account — a listening process per customer, a port per customer, and a supervision problem that grows with the customer list. Rejected. FTPS presents **the panel host's own hostname** and reuses the SSL module's material for that hostname, marker and all, which means it also picks up a real certificate automatically the day one is issued for that host. The consequence the customer sees is not optional and is not a detail: **a customer connects to the panel's hostname, never to their own domain.** A customer told to connect to `their-site.example` gets a certificate-name mismatch and a client that either refuses or asks them to click through a warning — which is the exact habit this plan exists to avoid teaching them. Every screen, every credential dialog and every locale string in Task 15 therefore names the host to connect to explicitly, and never interpolates the customer's own domain.
2. **TLS is refused-if-absent, not merely offered.** `force_local_logins_ssl=YES` and `force_local_data_ssl=YES`. A client that will not negotiate TLS cannot log in and cannot transfer, and it finds that out at connect time rather than after its password has crossed the wire in the clear. Old clients break visibly. That is the intended outcome.
3. **This is a plan. No product code is written by it.** The owner reviews the plan before a line of it is implemented.

## Global Constraints

Copied from the spec and from `rules/`, with the values that must be typed exactly right written out verbatim. Every task's requirements implicitly include this section.

- **FTPS is off until an operator turns it on, and it is a server-level decision, not a per-account one.** `rules/security.md` §10 forbids a new listening port arriving by surprise; the spec sanctions the daemon, so what this plan owes is that the daemon is installed but **not enabled and not started** by the installer, and that starting it is an explicit administrator action with the firewall consequences shown before it happens.
- **A login writes as the account, numerically.** An FTPS login is a system login created with the account's own uid and gid (`useradd --non-unique --uid <account uid> --gid <account gid>`), exactly as an SFTP login is, so that a file it uploads is indistinguishable from one the account created itself and no home permission has to change to accommodate it.
- **The password is in the host's shadow file and nowhere else.** The panel generates it, sends it once, shows it once and keeps no copy — not plaintext, not a hash. `chpasswd` reads it from standard input, never from an argument vector. No message in `ftp.proto` carries a password back and none may be added.
- **A password is validated, not escaped.** The existing `Password` type (`agent-core/src/validation/secrets/password.rs`) is reused unchanged: its alphabet already excludes the newline, which is what makes a `user:password` line handed to `chpasswd` unbreakable.
- **No caller-supplied chroot path, ever.** `ftp.proto` already carries `reserved 4; reserved "chroot_path";` on two messages, and the reason is written there at length. The FTPS jail is derived from a validated `AccountName` and is not nameable by a request. Nothing in this plan adds a path field to any request.
- **Every name is prefixed with the owning account.** An FTPS login is `<account>_<name>`, minted by a validated type whose constructor cannot produce an unprefixed name, exactly as `SftpUserName` does.
- **No shell strings, anywhere.** Every process is spawned with an argv array against an allow-list of absolute paths supplied by the distro adapter. This plan runs `useradd`, `usermod`, `userdel`, `chpasswd`, `passwd -S`, `getent`, `vsftpd` (for its config check) and `systemctl`; none of it may be assembled into a command line. `openssl` is deliberately NOT on this list: the certificate material the polygon fixtures need is produced through the shipped `ops::ssl` operations, which own the panel's one `openssl` spawn — this plan adds no caller of it.
- **Config-file injection is closed by validation, not by escaping.** `vsftpd.conf` is a line-oriented `key=value` file. Every value written into it comes from a validated type that cannot contain a newline, a carriage return or a control character (`rules/security.md` §4). The one operator-supplied value in the whole feature — the passive-mode advertised address — gets its own validated type for exactly this reason. There is **no per-login config file**: `chroot_local_user=YES` confines a login to its own passwd home, which is the jail, so `user_config_dir` has nothing to carry and is not enabled.
- **Distro facts live only in `distro`.** Every fact about vsftpd that differs between the families and reaches the agent is an adapter method — the binary path and the FTPS group (Task 1), and the **TLS version option key spellings** (Task 6, finding F2: `ssl_tlsv11`/`ssl_tlsv12` on the Debian family, `ssl_tlsv1_1`/`ssl_tlsv1_2` on the RHEL family). The package name, the packaged service name and the shipped `secure_chroot_dir` differ too but reach nothing: the package name lives in the installer's `vsftpd_packages_for_family`, and Maran ships its own unit and its own empty directory under `/run`. `ops` never writes `/etc/vsftpd/vsftpd.conf` or `apt-get` itself. The agent's own config path (`/etc/maran/vsftpd/vsftpd.conf`) is not a distro fact and lives in `AgentPaths`, beside `/etc/maran/nginx/sites` and `/etc/maran/certificates`.
- **The config is rendered, validated, atomically swapped and rolled back.** `vsftpd.conf` goes through `ops::safe_write`, validated by running the real `vsftpd` against the candidate — see Task 8 for the exact invocation and what makes it a real check rather than a decoration — and every render has a byte-exact golden in `agent/crates/templates/tests/golden/vsftpd/`.
- **`vsftpd.conf` is REWRITTEN, never appended to, and has exactly one writer.** vsftpd's parser takes the **LAST** occurrence of a key and every earlier one is overwritten as the file is read. **An earlier pass of this plan stated the opposite — "the first occurrence wins" — and it was wrong.** It was taken in good faith from finding F3 of a working task-03 report; the correction is measured with controls on both families in a working jail-mode report's Step 4b (2026-09-09): a duplicated `listen_port` of `2121` then `2122` bound **2122** (`ss` showed `0.0.0.0:2122`), with single-value controls at both ports binding what they said; and a duplicated `force_local_logins_ssl` behaved as the **appended** value in both directions — `YES` then `NO` let a plaintext login through exactly as a lone `NO` does, `NO` then `YES` refused it exactly as a lone `YES` does — on `ubuntu:24.04` (3.0.5-0ubuntu3.1) and `almalinux:9` (3.0.5-8.el9). **The rule this constraint states is unchanged and its stakes rise.** Under "first wins" an appended line would be inert: a no-op that looks like a change, bad hygiene. Under what is actually there, an appended `force_local_logins_ssl=NO` **silently turns off the mandatory TLS this whole feature exists to enforce** (spec §11; the owner's decision 2) on a config that still parses, still starts and still greets with `220` — the panel would keep reporting a healthy daemon while passwords crossed the wire in the clear. That is the reason the file is replaced whole and never merged into. Nothing in this plan appends: `ops::safe_write` writes the whole rendered text to a temporary file and renames it over the target (`render_validate_swap.rs`), which is a replace on the first run and on every re-run alike, and `/etc/maran/vsftpd/vsftpd.conf` has exactly one writer — the agent. The installer creates the **directory** and never the file (Task 2). The systemd unit's `-obackground=NO` and the Task 8 validator's `-o…` flags are the one deliberate second source of a key, and under the corrected reading they are **not an exception to this rule — they are the ordinary case of it**: `-o` settings are applied after the file has been read, so they are the last setting of their key and they win for the same reason an appended line would. Measured with its inverse control in the same report: file `background=YES` plus `-obackground=NO` leaves the process systemd watches alive, and the same file with no `-o` exits, which under `Type=simple` is a unit systemd calls dead. Task 8's measurement table observes the same thing (`-obackground=NO -olisten=YES` keeps the daemon running against a file that says `listen=NO`). What makes the `-o` overrides safe is therefore not an exemption anyone has to remember, but that the agent is the file's only writer, so the only key set twice is the one Maran sets twice on purpose.
- **Counted limits are checked in the panel.** `Plan` gains `MaxFtpUsers` beside `MaxSites`, `MaxDatabases` and `MaxSftpUsers`.
- **Deleting an account destroys its FTPS access first.** `userdel` of the account does not remove logins created with `--non-unique`, does not unmount the jail's bind mount and does not remove its systemd unit. The `AccountDeleting` cascade does all three, and a failure aborts the deletion. This is Plan 4's pool-leak lesson applied to a second daemon.
- **Suspending an account locks every login it holds — and today it would not.** This was checked rather than assumed, and the assumption was wrong. `ProcessSftpHost::account_logins` selects passwd rows by `row.home == jail_directory`, where `jail_directory` is the **SFTP** jail. An FTPS login's passwd home is the FTPS jail, so the shipped `SetAccountLoginsLocked` would walk straight past it and a suspended customer would keep a working write credential into their home — the exact defect the SFTP rpc's own doc comment was written to describe. Task 5 moves login enumeration into one place that knows about both jails, before any FTPS login can exist.
- **Tenant isolation answers 404, never 403.** Every account-scoped route gets its IDOR test.
- **Errors are values.** `Result<T>` and a typed `Error` in C#; typed error enums in Rust. `AgentErrorTranslator` remains the single wire-error boundary; the agent's text is logged, never returned to a customer.
- **Doc comments on every item, private included.** `CS1591` is an error; `#![warn(missing_docs)]` is on in every agent crate. One file, one type or unit.
- **`unsafe` only inside `agent-core/src/privs/`**, under the existing scoped allow.
- **All repository text in English.** Locale files carry `en`, `ru` and `hy` in parity; `maran structure` checks it.
- **Gates:** `dotnet test Maran.sln`, `dotnet build Maran.sln -warnaserror`, `maran agent check` (fmt, clippy `-D warnings`, tests, rustdoc), `maran structure`, `maran proto`, `maran migrate check`, `maran migrate guard`, `maran licenses --check`, `maran handshake`, `npm run lint && npm run typecheck && npm run build && npx playwright test`, and both container polygons through `maran polygon` + `maran polygon verify`.

### Values that must be typed exactly right

| Value | What it is |
|---|---|
| `maran-ftps` | The system group whose membership is the entire authorization to use FTPS. `DistroAdapter::ftps_group()` returns it; `installer/lib/89-ftps.sh` creates it. |
| `/var/lib/maran-ftps` | `AgentPaths::FTPS_JAIL_ROOT`. Base of the per-account jails. `root:root 0711` — **corrected after Phase A measured it**: this row said `0700`, and at `0700` EVERY FTPS login fails, because vsftpd `chdir()`s into the account's home AFTER dropping to the account's uid, so every component needs the execute bit for that uid. sshd walks `ChrootDirectory` as root BEFORE dropping, which is why the SFTP base beside it is `0700` and this one cannot be. Measured both ways on one host: `0700` -> `500 OOPS: cannot change directory`, `0711` -> `230 Login successful`. What `0711` grants, measured as an unprivileged uid: traverse yes, list no (account names are not enumerable). Containment therefore rests on the per-account jail and on the home's own `0750`, not on this base — the old `0700` was never load-bearing, it only made the daemon fail. A **sibling** of `/var/lib/maran`, never a child — see the constraint below. |
| `/var/lib/maran-ftps/<account>` | One account's jail; the chroot itself. `root:root 0755`. |
| `/var/lib/maran-ftps/<account>/home` | Mount point; the account's real home is bind-mounted here by an enabled systemd `.mount` unit. Its mode is the home's own, because it **is** the home. |
| `21` | The FTPS control port. Explicit TLS (`AUTH TLS`, RFC 4217) on the standard control port — not implicit FTPS on 990. |
| `30000`–`30099` | The passive data port range, `pasv_min_port`/`pasv_max_port`. 100 ports, matching `max_clients=100`, so the range can never be the thing that runs out first. In code these numbers exist ONCE, as `FtpsDefaults` in the `Ftp` module (Task 13, settled row 3); everything else — wire, screens, firewall copy — receives them from there. |
| `maran-ftps` (PAM service) | `pam_service_name`. `/etc/pam.d/maran-ftps` is written by the installer and contains `pam_succeed_if` requiring group membership, then `pam_unix`. The stock `/etc/pam.d/vsftpd` is never edited. |
| `force_local_logins_ssl=YES`, `force_local_data_ssl=YES` | The owner's decision 2, as vsftpd spells it. **The two keys in this whole template that a single appended line can switch off:** the parser takes the last occurrence (see the rewrite-never-append constraint above), so `force_local_logins_ssl=NO` added after the rendered `YES` disables forced TLS on a config that still parses and still starts. Measured in both directions on both families. This is why the file is replaced whole, and why Task 10 asserts the live file carries each of these keys exactly once. |
| `ssl_sslv2=NO`, `ssl_sslv3=NO` | Spelled identically by both families' builds. Two dead protocol versions, refused explicitly rather than left to a default. |
| The TLS **version** keys — `ssl_tlsv1`, and the 1.1/1.2 pair whose spelling is **per family** | The floor, and the one value in this whole template that is a **distro fact**. Measured 2026-09-09 (finding F2 of a working task-03 report): the Debian family's build spells them `ssl_tlsv11` / `ssl_tlsv12` / `ssl_tlsv13`, the RHEL family's spells them `ssl_tlsv1_1` / `ssl_tlsv1_2` / `ssl_tlsv1_3`; `ssl_tlsv1` is the same word on both. The rendered floor is therefore `ssl_tlsv1=NO`, `<the family's 1.1 key>=NO`, `<the family's 1.2 key>=YES`, and the keys come from `DistroAdapter::vsftpd_tls_version_keys()` (Task 6), never from a literal in the template. TLS 1.3 is left at the build's own default and is NOT written: the option exists on both families (contrary to what an earlier draft of this plan said), and with it unset both families were observed negotiating TLSv1.3 in Task 3 Step 5 — with the caveat Task 6 records, that the Debian half of that observation was made with the 1.1/1.2 lines removed rather than rendered. **Why this is not cosmetic:** the wrong spelling is an unrecognised variable, and on the Debian family vsftpd then exits 2 **printing nothing at all** — the silent-refusal blind spot Task 8's two-layer validation is built around. |
| `require_ssl_reuse=NO` | Set explicitly, with its reason in the template: vsftpd's default of `YES` requires the data connection to resume the control connection's TLS session, which OpenSSL 1.1.1+ TLS 1.3 session tickets and most graphical clients do not do, and the failure is a login that works and a directory listing that hangs. |

### Two constraints that already cost this repository a working feature

- **Every component of a chroot path must be root-owned.** OpenSSH refuses a login when any component of `ChrootDirectory` is owned by another uid, which is why the SFTP jail base was moved from `/var/lib/maran/sftp` to `/var/lib/maran-sftp` after **every SFTP login on every real install had been refused**. vsftpd's rule is not the same rule, but it is not weaker: vsftpd refuses to run at all with a chroot root that the logged-in user can write to (`500 OOPS: vsftpd: refusing to run with writable root inside chroot()`). The FTPS jail base is therefore `/var/lib/maran-ftps`, a sibling of `/var/lib/maran` for the same reason and stated in the same place, and the jail directory itself is `root:root 0755` so that the login can traverse it and cannot write to it.
- **`allow_writeable_chroot=YES` was considered and is rejected.** Chrooting a login directly into `/home/<account>` would need it, because the home is `<account>:<web server group> 0750` and the login shares the account's uid — so the login can write to its own chroot root and vsftpd refuses to start the session. Turning that guard off would be the cheap fix. It costs one bind mount to not turn it off, and a guard the daemon's own author wrote is not one this panel disables to save a mount unit.

## What the shipped SFTP subsystem already provides, and what this plan must NOT duplicate

Every row here is code that exists today and is reused as-is. The rule of two applies: a second copy of any of it is a review reject.

| Already shipped | Where | How FTPS uses it |
|---|---|---|
| `Password` validated type | `agent-core/src/validation/secrets/password.rs` | Unchanged. The FTPS password is a `Password`. |
| `AccountName` | `agent-core/src/validation/system/name.rs` | Unchanged; the jail and every login name derive from it. |
| `set_account_logins_locked`, `inspect_account_logins` | `ops/src/sftp/` | **Moved, not copied.** They select by "this login's passwd home is the SFTP jail", which excludes an FTPS login. Task 5 moves the enumeration into `ops::logins`, which knows both jails; the two RPCs keep their wire names and gain the second protocol. (Executed: the write kept its name, `set_account_logins_locked`; the read landed as `account_logins`, because a separate `inspect_account_logins` would have been a second name for the same body — see Task 5's amendment note.) |
| `ops::safe_write` (`render_validate_swap`, `write_config_set`, `remove_config`) | `ops/src/safe_write/` | The vsftpd config files are written through it, like every other config. |
| `ops::ssl` certificate store, `install_certificate`, `generate_self_signed`, `self_signed_marker` | `ops/src/ssl/` | The FTPS certificate **is** an entry in this store, for the panel host's own name. Nothing new writes certificate material. |
| `AccountJail`'s systemd-escaping rule | `ops/src/sftp/model/account_jail.rs` | The FTPS jail is a second type with the same rule; see Task 4 for why it is a second type and not a shared one. |
| `AccountDeleting` / `AccountSuspending` / `AccountResuming` Sdk events | `Maran.Sdk` | The new `Ftp` module subscribes to all three. |
| The one-time-credential dialog pattern | `frontend/src/components/sftp/SftpUserCreatedDialog.vue` | Copied in shape, not in code: the FTPS dialog shows a different set of connection facts (host, port, protocol, TLS requirement). |

---

## The one place this plan departs from issue #20, and the measurement that made it

Issue #20's scope says *"virtual users on the account's UID (`guest_enable`/`guest_username`),
chrooted into a subfolder, password held in vsftpd's own store"*. This plan keeps every property
that sentence is asking for and reaches them by a different mechanism, because the mechanism it
names does not survive the supported OS matrix. **The owner ratified this deviation on
2026-09-09** (Open Questions row 1) — it is the only scope item this plan does not implement
literally, and the evidence that forced it is kept below, beside the settled row.

**What "vsftpd's own store" would have to be.** vsftpd has no credential store of its own; it
authenticates through PAM. "Virtual users" in the vsftpd sense means a PAM stack with no
`pam_unix.so` in it, backed by a database only that stack reads — in practice `pam_userdb.so` over
a Berkeley DB file, since `pam_pwdfile` is a Debian-only package with no RHEL equivalent.

**What was measured, on 2026-09-08, in throwaway containers of each supported family:**

| Family | `pam_userdb.so` present | Backing library it is linked against | Tool that can write that database |
|---|---|---|---|
| Ubuntu 24.04 | yes, `libpam-modules` (priority: required) | `libdb-5.3.so` | `db_load`, package `db-util` |
| Debian 13 | yes, `libpam-modules` 1.7.0-5 | `libdb-5.3.so` | `db_load`, package `db-util` |
| AlmaLinux 9 | yes, `pam-1.5.1-28.el9`, baseos | `libdb-5.3.so` | `db_load`, package `libdb-utils` |
| AlmaLinux 10 | yes, `pam-1.6.1-9.el10`, baseos | **`libgdbm.so.6`** | **`libdb-utils` does not exist in AlmaLinux 10** — `gdbmtool`, package `gdbm` |

So the credential file's **format** changes inside one family between two supported major versions,
and the tool that writes it changes with it. That is not a distro fact the `DistroAdapter` can carry
honestly: the adapter is per-family (`rules/architecture.md`: "Adding a family is a new adapter
folder plus one arm in `adapter_for`"), and encoding "AlmaLinux 9 unlike AlmaLinux 10" in it means
the agent's credential store depends on which shared object a packager happened to link. The
failure mode is the worst kind: a database written in the wrong format authenticates nobody, on one
OS version, with a "530 Login incorrect" that names nothing.

**What this plan does instead.** An FTPS login is a system login created exactly the way an SFTP
login already is — `useradd --non-unique --uid <account uid> --gid <account gid>`, a `nologin`
shell, no membership of the SFTP group — and the FTPS PAM stack authenticates it with `pam_unix`
guarded by `pam_succeed_if.so … user ingroup maran-ftps`. Reading the scope sentence's properties
one at a time:

- *"on the account's UID"* — literally, and by the same `--non-unique` mechanism the SFTP logins
  already use. An upload lands owned by the account.
- *"chrooted into a subfolder"* — `chroot_local_user=YES` chroots to the login's passwd home, which
  is the root-owned jail; the account's real files are the `home` directory inside it. No
  caller-supplied path exists anywhere, which is what `ftp.proto`'s `reserved "chroot_path"` demands.
- *"password … never in the panel"* — the panel mints it, shows it once and keeps nothing. The hash
  is in the host's shadow file, which is where the shipped SFTP logins' hashes already are.
- *"vsftpd's own store"* — this is the property given up, and what is bought with it is one
  credential path instead of two, on all eight supported OS/version combinations, with `chpasswd`,
  `passwd -S` and `usermod --lock` — three mechanisms already proven on both polygon families —
  doing the work.

**What is lost, stated plainly.** An FTPS login is now a row in `/etc/passwd`. Two consequences,
both closed here rather than waved at: it could in principle be used against another PAM-consuming
service, which is why the shell is `nologin`, the login joins no group but `maran-ftps`, and
`/etc/pam.d/maran-ftps` is a service of our own rather than an edit to a shared one; and the login
namespace is now shared with SFTP, so `alice_web` can be an SFTP login or an FTPS login and never
both. That second consequence is not a defect — it is the `Protocol` distinction issue #20 asks
for, enforced by `useradd` refusing a duplicate name rather than by a check the panel has to
remember to run.

## What the certificate story is, exactly

The owner's decision 1 says FTPS reuses the SSL module's material for the panel host's own name.
Read against the shipped code, that has one consequence worth writing down before Task 8:

**FTPS consumes certificate material. It never produces or replaces any.** `ops::ssl` already owns
the store, the key/certificate pairing check, the atomic two-file write and the self-signed marker;
`install_certificate` additionally requires a **site vhost** to exist for the domain
(`SslOpError::SiteNotFound`) because it rewires that vhost as its second write. So the FTPS
hostname is a domain the panel already serves as a site — which is also the only way an HTTP-01
certificate for it can ever be issued — and enabling FTPS for a hostname with no material in the
store is a typed refusal naming what to do about it, never a daemon quietly serving the wrong name.

Concretely:

- The material FTPS points vsftpd at is `<CERTIFICATE_DIRECTORY>/<ftps hostname>/fullchain.pem` and
  `privkey.pem` — the same two paths nginx serves for that site. `AgentPaths::CERTIFICATE_DIRECTORY`
  is `/etc/maran/certificates`.
- Whether that material is a placeholder is answered by `self-signed.marker` **existing beside it**.
  Nothing in this plan parses a certificate's text to decide anything. The marker is a file
  precisely because the previous design read openssl's printed subject and a reviewer destroyed a
  customer's real certificate with an organisation containing a comma; the FTPS status query
  therefore asks `path.exists()` through one new read-only unit in `ops::ssl` (Task 7) and adds no
  second opinion of its own.
- When a real certificate is later issued for that hostname, `install_certificate` replaces the
  bytes at those exact paths and removes the marker. vsftpd loads its certificate once, at daemon
  start, so the panel restarts vsftpd on the `CertificateInstalled` event — a restart that aborts
  in-flight transfers, which is stated in the panel's copy and happens at most once a renewal
  period.

**The alternative that was rejected:** extracting a site-less certificate path out of `ops::ssl` so
FTPS could generate its own placeholder for a hostname with no site. It would have meant a second
caller of the marker discipline, in privileged code whose last bug destroyed a customer's private
key. Requiring the hostname to be a site costs the operator one site and buys zero new writers of
certificate material.

---

## File structure

Where each new unit lives, decided before the tasks so the decomposition is locked in rather than
discovered.

### Agent — `agent/crates/`

| Path | Responsibility |
|---|---|
| `agent-core/src/agent_paths.rs` | Gains `FTPS_JAIL_ROOT` (`/var/lib/maran-ftps`), with the root-ownership reason written where `SFTP_JAIL_ROOT`'s is. |
| `agent-core/src/validation/system/ftps_user_name.rs`, `ftps_user_name_error.rs` | `FtpsUserName` — the account prefix, the 32-byte `useradd` ceiling, and `decode` back to a suffix. Built on the shipped `prefixed_name.rs`, exactly as `SftpUserName` is. |
| `agent-core/src/validation/web/passive_address.rs`, `passive_address_error.rs` | `PassiveAddress` — the one operator-supplied value written into `vsftpd.conf`. An IPv4 literal or nothing; refuses anything that is not four dotted decimal octets, which is what makes a newline in a config file impossible rather than escaped. |
| `distro/src/adapter.rs` | **Three** new trait methods: `vsftpd_binary` and `ftps_group` (Task 1, executed), and `vsftpd_tls_version_keys` (Task 6 — see the amendment note in Task 1 for why the third one is added by its consumer and not by re-opening a finished task). Three and not six, because Maran ships its own systemd unit, its own config path and its own `secure_chroot_dir` under `/run`, so those three places the families differ never reach the agent — but the TLS version keys are written into a file the agent renders, so they do. |
| `distro/src/vsftpd_tls_version_keys.rs` | `VsftpdTlsVersionKeys { tls_v1, tls_v1_1, tls_v1_2 }` — three `&'static str`s, one per key the template writes. A shared type at the crate root, where `family.rs` already puts `DistroFamily`; `maran structure`'s check 16 is satisfied by construction (`VsftpdTlsVersionKeys` ⇔ `vsftpd_tls_version_keys.rs`). |
| `distro/src/debian/debian_adapter.rs`, `distro/src/rhel/rhel_adapter.rs` | Their per-family answers, each measured on the polygon image rather than recalled — including the TLS key spellings, which are the one row of Task 1's fact table that turned out to reach the rendered config. |
| `templates/src/vsftpd/vsftpd_daemon_config.rs` | The askama render type for `vsftpd.conf`. Folder-prefixed and byte-exact against the type it holds, because `check-structure.sh`'s check 16 derives the expected file name from the single `pub struct` — `VsftpdDaemonConfig` demands `vsftpd_daemon_config.rs`, `daemon_config` is in neither the `subject_named` allow-list nor the `*_service|*_status` family, and the shipped prior art is the same shape (`nftables_allow.rs`/`NftablesAllow`, `pool_override.rs`/`PoolOverride`). |
| `templates/templates/vsftpd/vsftpd.conf.j2` | The template. Replaces the `.gitkeep` that has held the folder since day one. |
| `templates/tests/golden/vsftpd/vsftpd.conf`, `vsftpd_passive_address.conf`, `vsftpd_ipv4_only.conf`, `vsftpd_rhel_tls_keys.conf` | Byte-exact goldens: the default dual-stack render, the render with an advertised passive address, the IPv4-only render the enable path falls back to when the kernel refuses an IPv6 bind (settled row 6), and — added by this plan's third amendment — the otherwise-default render carrying the **RHEL family's** TLS version keys. The first three carry the Debian family's spelling. Four goldens and not six: see Task 6's "Why one extra golden and not a per-family pair". |
| `ops/src/logins/mod.rs`, `systemd_escape.rs`, `account_logins.rs`, `set_account_logins_locked.rs`, `logins_error.rs`, `logins_host.rs`, `process_logins_host.rs`, and `model/{account_login,login_protocol,account_login_set}.rs` | The one place that knows what file-transfer logins an account holds and which daemon each belongs to, plus the systemd path-escaping rule both jails derive their mount unit's name with. Moved out of `ops::sftp`, not copied. The returned types live under `model/`, per the operation anatomy. |
| `ops/src/ftps/mod.rs`, `ftps_error.rs`, `ftps_host.rs`, `process_ftps_host.rs` | The FTPS area, in the shape every area here has. |
| `ops/src/ftps/enable_ftps.rs`, `disable_ftps.rs`, `get_ftps_status.rs`, `reload_ftps_tls.rs`, `probe_listen_mode.rs` | The server-level operations — one file per rpc, named after it, per the mechanical rpc→file law — plus the IPv6-bind probe the enable path decides its listen mode with. |
| `ops/src/ftps/create_ftps_user.rs`, `set_ftps_password.rs`, `delete_ftps_user.rs`, `remove_account_ftps.rs`, `ensure_account_jail.rs` | The login-level operations, the account-deletion cascade's FTPS half, and the idempotent jail-and-mount step `create_ftps_user` starts with. |
| `ops/src/ftps/model/ftps_jail.rs`, `ftps_user_request.rs`, `ftps_configuration.rs`, `ftps_state.rs`, `listen_mode.rs`, `candidate_outcome.rs`, `ftps_unit.rs` | The derived paths and the typed inputs and outputs, including the `ListenMode` the probe chooses and the state reports, and the `CandidateOutcome` layer 1 answers with (`candidate_outcome.rs` added by the fifth amendment: Task 8 shipped it and this table did not name it). **`ftps_unit.rs` added by the sixth pass for the same reason:** `FtpsUnit` is shipped in that folder and this table named six files where the tree holds seven (`ls agent/crates/ops/src/ftps/model/`). Nothing about the code changed. |
| `ops/src/ssl/certificate_state.rs` | The one read-only unit that answers "is there material for this domain, and is it the placeholder?" — the marker's existence, nothing parsed. |
| `agent/src/services/ftps/ftps_service.rs`, `ftps_status.rs` | Proto → ops → response, and the error → `AgentError` map — `ftps_status.rs`, matching the `*_status` family the structure gate already exempts, shaped like `sftp_status.rs`'s `to_agent_error`. No collision with ops: that crate's read operation is `get_ftps_status.rs`. |
| `agent/tests/ftps_on_a_real_host.rs` | The polygon suite: a real vsftpd, a real TLS session, a real refusal. |
| `scripts/lib/check-structure.sh` | The new agent files are added to its subject-name list in the same task, or `maran structure` fails on them. |
| `rules/rust.md` | The canonical agent layout gains the `ops/src/logins/` (with its `model/`), `ops/src/ftps/`, `services/ftps/` and `templates/src/vsftpd/` rows, each added by the task that creates the folder. **Corrected by the fifth pass:** those rows are already in, and the two sentences this row said Task 16 would retire ("stays for a future FTP daemon and holds nothing"; "A future FTP daemon would get its own `ftp/` beside it") no longer exist — the executing tasks reworded them. What Task 16 Step 5 still owes on this file is the `{…,sftp,ftp,…}` area-list token becoming `ftps`, and the `vsftpd/user_config.rs` row being **changed** to name the deferral rather than deleted. **Corrected by the sixth pass:** the area-list token is ALREADY `ftps` in the tree (`grep -n 'accounts,sites,php,db,sftp' rules/rust.md` → line 255, `{…,sftp,ftps,logins,files,…}`), so of the two only the `user_config.rs` row is still owed here; Task 16 Step 5 carries the full re-measure. |

### Contract — `proto/agent/v1/`

| Path | Change |
|---|---|
| `common.proto` | New `enum TransferProtocol` — `TRANSFER_PROTOCOL_UNSPECIFIED/SFTP/FTPS`. It lives here and not in `ftp.proto` because `accounts.proto` reads it too, and it is **not** called `Protocol`: `firewall.proto` already defines a top-level `Protocol` in package `maran.agent.v1`, and protoc refuses two of them. |
| `ftp.proto` | New `service FtpsService` with `EnableFtps`, `DisableFtps`, `GetFtpsStatus`, `ReloadFtpsTls`, `CreateFtpsUser`, `SetFtpsPassword`, `DeleteFtpsUser`, and their request/response/ok messages. Purely additive. The file header comment is corrected in the same edit: the real FTP daemon it anticipated has arrived, it took `Ftps*` names rather than the retired `Ftp*` ones, and the reason is recorded. |
| `accounts.proto` | `SftpLoginSuspensionFact` gains `TransferProtocol protocol = 3;` and `GetAccountSuspensionStateOk` gains `uint32 unmanaged_logins = 9;` — both additive, both numbers unused today. |
| `firewall.proto` | `FirewallRule`, `AllowPortRequest` and `DenyPortRequest` gain `optional uint32 port_to` — **`= 4` in `FirewallRule`, `= 7` in `AllowPortRequest` and `= 7` in `DenyPortRequest`**, and the three numbers differ for a reason an executor must not smooth over: `FirewallRule` holds 1–3, so 4 is free there, while both request messages carry `reserved 4; reserved "ssh_port";` and hold 1, 2, 3, 5, 6, so their next free number is 7 and protoc refuses 4 outright. Additive; absent means the single port the message already carried. |
| `contract-baseline.txt` | Regenerated by `maran proto --accept`, in its own commit, reviewed as the diff of added inventory lines it is. |

### Panel — `backend/src/`

| Path | Responsibility |
|---|---|
| `Maran.Agent.Client/Interfaces/IAgentFtpsClient.cs`, `IFtpsServiceInvoker.cs` | The typed client and its transport seam. |
| `Maran.Agent.Client/Services/FtpsService/AgentFtpsClient.cs`, `GrpcFtpsServiceInvoker.cs`, `CreateFtpsUserArguments.cs`, `FtpsStatusDto.cs` | The client, its invoker, the request carrier and its one DTO. **`CreatedFtpsUserDto.cs` was named here by earlier passes and is retired by the sixth** — see Task 12's Files list for the measurement. |
| `Maran.Sdk/Contracts/AgentCapability.cs` | Gains `Ftps`, or `AgentCapabilityGuard` refuses to compose the module that resolves `IAgentFtpsClient`. |
| `Maran.Sdk/Events/CertificateInstalled.cs` | Published by `Ssl` when material lands for a domain; handled by `Ftp`. |
| `Maran.Host/Resilience/ResilientAgentFtpsClient.cs` | The pipeline decorator. |
| `Maran.Modules/Ftp/` | The module, in the skeleton folder that has held a `.gitkeep` since day one: `Maran.Modules.Ftp.csproj` and `GlobalUsings.cs` (the skeleton holds folders only — verified, no csproj exists under it; and **not all the folders** — `Domain/Entities/`, `Domain/Policies/`, `Controllers/` and `Common/` are absent and are created by the task that lands their first file, see Task 13's fifth-pass note), `FtpModule.cs`, `FtpManifest.cs`, `Domain/Entities/{FtpUser,FtpsSettings}.cs`, `Domain/Policies/FtpsDefaults.cs` (settled row 3's named constants), `Persistence/`, `Commands/{EnableFtps,DisableFtps,CreateFtpUser,ResetFtpUserPassword,DeleteFtpUser}/`, `Queries/{GetFtpsStatus,ListFtpUsers,GetFtpUser}/`, `Controllers/`, `IntegrationEvents/Handlers/`, `Services/FtpAuditJournal.cs`, `Resources/ErrorMessages{,.ru,.hy}.resx`. |
| `Maran.Modules/Accounts/Domain/Entities/Plan.cs` | Gains `MaxFtpUsers`, with a migration that backfills a real value. |
| `Maran.Sdk/Contracts/AccountSnapshot.cs`, `Maran.Modules/Accounts/Services/AccountDirectory.cs` | The limits window every module reads: the record gains `MaxFtpUsers` and the Accounts-side directory fills it. The record is positional, so every construction site gains the argument (Task 14 names them). |
| `Maran.Modules/Sftp/Common/SftpUserDto.cs` | Gains the protocol label, so the merged screen can render both lists from backend-owned values. |
| `Maran.Agent.Client/Services/AccountsService/LoginTransferProtocol.cs` | New: the panel's own spelling of the wire's `TransferProtocol`, mapped by number the way the sibling `AccountLoginPasswordState` already is. |
| `Maran.Agent.Client/Services/AccountsService/SftpLoginSuspensionFactDto.cs` → `FileTransferLoginSuspensionFactDto.cs` | Renamed with its type, which gains `Protocol`. The list carries FTPS logins after this plan, and a type whose name says SFTP is a name that stops the next reader looking. The WIRE field keeps its name and number; this is the DTO layer's own vocabulary. |
| `Maran.Agent.Client/Services/AccountsService/AccountSuspensionStateDto.cs` | Gains `UnmanagedLogins`; `SftpLogins` becomes `FileTransferLogins`. Positional, so 18 construction sites are named by the compiler (Task 14 counts them). |
| `Maran.Modules/Accounts/Commands/{SuspendAccount,ReactivateAccount}/` | The two attestation handlers: the sentence speaks of file-transfer logins with a per-protocol count, and the unmanaged count is reported rather than refused on. Without this, Task 10's two new wire fields are written and never read. |
| `Maran.Modules/Firewall/` | The rule entity, its configuration, its migration, its validator and its DTO gain the range's upper bound. |
| `Maran.Sdk/Contracts/AuditActions.cs` | `FtpsEnabled`, `FtpsDisabled`, `FtpUserCreated`, `FtpUserDeleted`, `FtpUserPasswordReset`. |

### SPA — `frontend/src/`

| Path | Responsibility |
|---|---|
| `types/ftpUser.ts`, `types/ftpsStatus.ts` | The shapes the API returns. |
| `composables/apis/useFtpApi.ts` | The calls, used from the store only. |
| `stores/ftp.ts` | State and actions. |
| `pages/ftp/FtpUsersPage.vue` | The merged file-transfer screen: SFTP and FTPS logins in one list, each labelled with its protocol. |
| `components/ftp/FtpUserCreateForm.vue`, `FtpUserCreatedDialog.vue`, `FtpsServerPanel.vue` | The create form with its protocol choice, the one-time credential dialog that names the host to connect to, and the administrator's enable/disable panel. |
| `locales/{en,ru,hy}/ftp.json` | Copy, in parity. |

### Installer — `installer/`

| Path | Responsibility |
|---|---|
| `installer/lib/89-ftps.sh` | The package, the `maran-ftps` group, `/etc/pam.d/maran-ftps`, `/etc/maran/vsftpd/`, the jail base, the systemd unit, `/etc/logrotate.d/maran-ftps`, and a daemon that is installed and not listening. |
| `installer/install.sh` | One `run_step 89-ftps.sh step_ftps` line between step 88 and step 90. |
| `installer/uninstall.sh` | Removes what step 89 added, in the same marker-delimited discipline the other steps use. |
| `docker/polygon/{ubuntu24,alma9}.Dockerfile` | `COPY installer/lib/89-ftps.sh` and the packages the assertions need. |
| `docker/polygon/assert-installer-steps.sh` | Runs step 89's own functions and asserts what they left behind, including the refusals. |

---

## Phase A — the host, proven before anything is built on it

The order of this plan is the order of risk. Everything below Phase A is ordinary panel code; Phase A
is the part that can only fail on a real machine, so it is finished, run and asserted first. Two
things belong to this phase by that logic and are scheduled inside it rather than at the end:
the **threat note** (Task 2 Step 0 — `rules/security.md` requires it written BEFORE the privileged
change, and Task 2 is the first privileged change), and the **end-to-end composite proof**
(Task 3 Step 5) — the riskiest thing in this plan is not any one mechanism but vsftpd + our own PAM
stack + a `--non-unique` uid + `chroot_local_user` + a bind mount + forced TLS working *together*,
and that composite is hand-proven on a real host here, before six tasks build on it, exactly as the
SFTP plan proved its jail in a Step-0 transcript before a line of code depended on it.

### Task 1: What vsftpd actually is on each family — measured, not recalled

**Files:**
- Modify: `agent/crates/distro/src/adapter.rs`, `distro/src/debian/debian_adapter.rs`, `distro/src/rhel/rhel_adapter.rs`
- Modify: `agent/crates/agent/tests/binary_paths_on_a_real_host.rs`
- Test: `agent/crates/distro/src/tests/debian/debian_adapter_tests.rs`, `distro/src/tests/rhel/rhel_adapter_tests.rs`

**Interfaces:**
- Consumes: `DistroAdapter`, and the existing `sftp_group` as the shape to copy.
- Produces: two new trait methods —
  - `fn vsftpd_binary(&self) -> &'static str` — `/usr/sbin/vsftpd` on both families.
  - `fn ftps_group(&self) -> &'static str` — `maran-ftps` on both families.

**The facts this task encodes, and where each was measured.** Every row below was obtained by
installing the package in a throwaway container of that family on 2026-09-08 and asking the host,
not by recalling documentation. The rows that DIFFER are the reason several later decisions look
the way they do.

| Fact | Ubuntu 24.04 / Debian 13 | AlmaLinux 9 / 10 |
|---|---|---|
| Package | `vsftpd` (3.0.5-0ubuntu3.1, 3.0.5-0.2) | `vsftpd` (3.0.5-8.el9, 3.0.5-12.el10) |
| Binary | `/usr/sbin/vsftpd` | `/usr/sbin/vsftpd` |
| Packaged unit | `vsftpd.service`, `Type=simple`, `ExecStart=/usr/sbin/vsftpd /etc/vsftpd.conf` | `vsftpd.service`, `Type=forking`, `ExecStart=/usr/sbin/vsftpd /etc/vsftpd/vsftpd.conf` |
| **Enabled by the package's own install, with NO mask in place** | **YES** — `/etc/systemd/system/multi-user.target.wants/vsftpd.service` exists straight after `apt-get install` | no |
| **Whether that `.wants` symlink still appears when the unit is MASKED first** | **SETTLED 2026-09-09 (third measurement): NO — with the mask in place before `apt-get install`, no `.wants` entry and no `dsh-also` record is created; without the mask, both are.** | not applicable (nothing is enabled either way) |
| Shipped config leaves the daemon | `listen_ipv6=YES`, `local_enable=YES`, `anonymous_enable=NO` — i.e. **listening on port 21 and accepting local logins in the clear** | same config values, but nothing starts it |
| `secure_chroot_dir` in the shipped config | `/var/run/vsftpd/empty` (`/usr/share/empty` does not exist) | `/usr/share/empty` (`/var/run/vsftpd/empty` does not exist) |
| Stock `/etc/pam.d/vsftpd` | `pam_listfile` on `/etc/ftpusers`, `@include common-*`, **`pam_shells.so`** | `pam_listfile` on `/etc/vsftpd/ftpusers`, `password-auth`, **`pam_shells.so`** |
| `pam_succeed_if.so` | present, `/usr/lib/x86_64-linux-gnu/security/` | present, `/usr/lib64/security/` |
| Default `background` | `NO` | **`YES`** |
| TLS **version** option spelling (measured 2026-09-09, F2 — added by the third amendment) | `ssl_tlsv1`, **`ssl_tlsv11`**, **`ssl_tlsv12`**, **`ssl_tlsv13`** (Ubuntu 24.04, `vsftpd 3.0.5-0ubuntu3.1`) | `ssl_tlsv1`, **`ssl_tlsv1_1`**, **`ssl_tlsv1_2`**, **`ssl_tlsv1_3`** (AlmaLinux 9, `vsftpd 3.0.5-8.el9`) |

**SETTLED, and kept here because how it was settled is the useful half — formerly CONTESTED: the `.wants` symlink under a mask.** A third measurement (a working jail-mode report) ran all four cells of *mask x image* on both Debian-family images and agreed with Task 3, not Task 2. The mechanism was re-driven by hand: `deb-systemd-helper enable` delegates to systemd, which refuses a masked unit, so the helper exits **before** writing either the link or its state file, and the postinst swallows that with `|| true`. One caveat was deliberately kept rather than inverting the claim symmetrically: that absence rests on `systemctl` being present, and the helper's other branch writes the links itself. **The mask is the control on every path either way.** The original disagreement follows. Two of this plan's own tasks measured the Debian
family's behaviour and got opposite answers, so the plan records both rather than picking one:

- **Task 2, 2026-09-09** (a working task-02 report), `ubuntu:24.04` and
  `debian:trixie` throwaway containers: *"the SAME wants symlink is created even WITH the mask in
  place"* — i.e. `systemctl mask` does not stop dpkg's enablement bookkeeping.
- **Task 3, 2026-09-09** (a working task-03 report, Step 3), four throwaway
  containers of the same two images: with the mask link present before install, **no** `.wants`
  entry and **no** `/var/lib/systemd/deb-systemd-helper-enabled/vsftpd.service.dsh-also` record;
  without the mask, both appear. The proposed mechanism is that `deb-systemd-helper` checks for
  exactly that `/dev/null` link and skips the enable.

**A third measurement is being taken by the owner of `installer/`. Until it lands, no task may
assert on the `.wants` entry in either direction, and none does** — Task 2's observable control and
Task 3's assertion are both the **mask symlink** (`readlink -f /etc/systemd/system/vsftpd.service`
is `/dev/null`), which both measurements agree on and which is the thing that actually keeps the
daemon from starting. Task 3's assertions print the `.wants` state on both branches with
`NOT THE CONTROL either way:` in front of it, which is why nothing downstream broke while the two
readings disagreed — and is the reason this row can be left open instead of blocking Tasks 4–16.

**What would settle it, in one edit.** A single run per Debian-family image, in a throwaway
container, capturing all four cells of the 2×2 (mask / no mask × `ubuntu:24.04` / `debian:trixie`)
in one transcript, and recording for each: `ls -l /etc/systemd/system/multi-user.target.wants/vsftpd.service`,
`ls /var/lib/systemd/deb-systemd-helper-enabled/vsftpd.service.dsh-also`, the `dpkg` version of
`init-system-helpers`, and whether `/usr/sbin/policy-rc.d` is present. The two runs differ somewhere
in that list — the most likely candidate is **whether the mask link existed BEFORE `apt-get install`
ran or was created after it**, which is the ordering step 89 exists to get right and which a
transcript makes unambiguous. When that run lands, the owner replaces this note and the contested
table row with the settled fact, and — in the same edit — reconciles the comment block in
`installer/lib/89-ftps.sh`, which today says the postinst creates the `.wants` entry *"mask or no
mask"* and therefore states Task 2's reading as settled fact. `installer/**` is not this plan's
file to edit; the disagreement is recorded here so the owner closes both in one pass.

Four of those rows decide later tasks, and they are called out here so that no later task re-derives
them:

1. **The Debian family starts the daemon for you.** `apt-get install vsftpd` leaves port 21 open with
   the distribution's own configuration. Step 89 therefore masks the packaged unit *before* the
   package manager runs (Task 2), and the polygon asserts the daemon is not listening after the step
   (Task 3).
2. **`pam_shells.so` is in both stock PAM stacks.** An FTPS login has a `nologin` shell, so the stock
   stack would refuse every login this panel creates. That is why `/etc/pam.d/maran-ftps` is a file
   of our own and the stock one is never edited.
3. **`secure_chroot_dir` and the unit's `Type` differ.** Maran ships its own unit
   (`maran-ftps.service`) with its own config path and its own empty directory under `/run`, so
   neither difference reaches the agent at all — which is why this task adds two adapter methods and
   not six.
4. **The TLS version keys are spelled differently, and unlike rows 1–3 that difference reaches a
   file the agent writes.** `secure_chroot_dir` and `Type` are differences Maran routes around by
   shipping its own unit; this one it cannot, because the rendered `vsftpd.conf` has to contain the
   family's own words. It is therefore an adapter method
   (`rules/architecture.md`: *"If a change needs a second branch, the adapter is missing a method"*),
   and the method is added by **Task 6**, the task that renders the file — see the amendment note
   immediately below.

> **AMENDED AFTER EXECUTION — 2026-09-09.** This task shipped on 2026-09-09 and added exactly the
> **two** adapter methods its Steps 1–5 name: `vsftpd_binary` and `ftps_group`. Nothing about the
> code it produced has changed, and a reader must not infer that it shipped a third method — it did
> not. What was amended after execution is this task's **evidence table**, which gained the TLS
> version-key row (finding F2) and the CONTESTED `.wants` note, because this table is the one place
> in the plan where a reader looks up "what is vsftpd on each family" and a table that is silent
> about the fact that broke Task 6 is a table that will mislead the next reader.
>
> **Why the third adapter method is added by Task 6 and this task is NOT re-opened.** Adding a Step
> 6 here would put an unexecuted instruction inside a task whose checkboxes are ticked and whose
> phase is closed: an executor working the plan task-by-task starts at Task 4, never reads a
> finished task's steps again, and the method would simply never be written — with the failure
> surfacing as Task 6 rendering a config that vsftpd on Ubuntu refuses **with no message at all**.
> Task 6 is unexecuted, is the only consumer of the value, and is the task whose own Step 3 (running
> the real daemon over the render) is the check that catches a wrong spelling, so the method, its
> two family implementations and their adapter tests are listed in Task 6's Files and Steps. The
> split is deliberate and it is the honest one: the **fact** belongs to the task that measures facts,
> the **code** belongs to the task that will actually be run.

- [x] **Step 1: Write the failing adapter tests**

```rust
#[test]
fn debian_names_the_ftp_daemon_where_the_package_puts_it() {
    let adapter = DebianAdapter;
    assert_eq!(adapter.vsftpd_binary(), "/usr/sbin/vsftpd");
    assert_eq!(adapter.ftps_group(), "maran-ftps");
}
```

and the same two assertions against `RhelAdapter`, in that family's own test file.

- [x] **Step 2: Run them and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-distro vsftpd`
Expected: FAIL — `vsftpd_binary` is not a method of `DistroAdapter`.

- [x] **Step 3: Implement both methods on the trait and both families**

Doc comments carry the measurement, not a guess. The trait method's comment says what the value is
FOR (an argv[0] the agent may spawn, and the allow-list it belongs to); each family's comment says
which package puts the binary there.

- [x] **Step 4: Run and watch them pass**

- [x] **Step 5: Extend the real-host binary check so a moved binary fails by name**

`agent/crates/agent/tests/binary_paths_on_a_real_host.rs` already asserts that every path the
adapter names exists on the polygon it is running in. Add `vsftpd_binary` to it. **What makes this
non-vacuous:** the suite runs inside an image that has installed vsftpd (Task 3 adds it), so an
adapter returning a path the family does not use fails here rather than at a customer's first
enable. Guard it the way the file already guards the others — a missing `MARAN_POLYGON` refuses the
run instead of skipping it.

---

### Task 2: `installer/lib/89-ftps.sh` — a daemon that is installed and does not listen

**Files:**
- Create: `installer/lib/89-ftps.sh`, `installer/systemd/maran-ftps.service`, `docs/superpowers/notes/2026-09-XX-ftps-threat-note.md` (Step 0 — before anything else in this task)
- Modify: `installer/install.sh` (one `run_step` line), `installer/uninstall.sh` (the removal half), `installer/lib/20-dependencies.sh` (nothing — see below), `README.md` (the "File access is SFTP only" paragraph is no longer true)

**Interfaces:**
- Consumes: `MARAN_OS_FAMILY` from the preflight step, and the `run_step NN-name.sh step_name` convention.
- Produces, all idempotent, each callable on its own so the polygon can drive them:
  - `mask_packaged_vsftpd()` — `systemctl mask vsftpd.service` **before** the package is installed.
  - `install_vsftpd_package()` — the family's package manager, package name from `vsftpd_packages_for_family`.
  - `ensure_ftps_group()` — `groupadd --system maran-ftps` if absent.
  - `ensure_ftps_directories()` — the jail base and the agent's config directory.
  - `install_ftps_pam_service()` — `/etc/pam.d/maran-ftps`.
  - `install_ftps_unit()` — `maran-ftps.service`, installed and left **disabled and stopped**.
  - `install_ftps_logrotate()` — `/etc/logrotate.d/maran-ftps` (`root:root 0644`): weekly, `rotate 4`,
    `compress`, `missingok`, `notifempty`, `create 0640 root root`. Without it
    `vsftpd_log_file=/var/log/maran/ftps.log` grows unbounded — the installer configures no rotation
    for it anywhere else, and both families ship logrotate in their base set.
  - `step_ftps()` — all of the above, then a line saying FTPS is installed and off.

**Why the mask comes first, and what it is protecting.** Measured in Task 1: on the Debian family the
package's own postinst enables and starts `vsftpd.service` with the distribution's configuration,
which listens on port 21 and accepts local logins over an unencrypted control channel. An installer
that installed the package and then disabled the unit would still have opened that port for the
length of the install — on a machine that is, by definition, reachable from the internet, because
the operator just reached it. `systemctl mask` points the unit at `/dev/null`, so the postinst's
`systemctl start` fails harmlessly and no socket is ever bound. The mask is left in place: this
panel never runs the packaged unit, it runs its own.

**Not part of that argument, and CONTESTED: what the mask does to the `.wants` symlink.** This task
(2026-09-09) measured that `/etc/systemd/system/multi-user.target.wants/vsftpd.service` is created
by the postinst *mask or no mask*; Task 3 re-measured on the same two images the same day and got
the opposite. Both readings are recorded in full, with what would settle them, in the CONTESTED note
under Task 1's fact table, and a third measurement is being taken by the owner of `installer/`.
Nothing in this task or in Task 3 rests on it: the mask symlink is the control on both readings, and
this step's own assertion (Step 4) is `readlink -f /etc/systemd/system/vsftpd.service` = `/dev/null`
and never the `.wants` entry. When the third run lands, the settled fact replaces this note, Task 1's
row and the "mask or no mask" sentence in `installer/lib/89-ftps.sh`'s comment block in one edit.

**The jail base, and why it is a sibling of `/var/lib/maran`.**

```
/var/lib/maran-ftps        root:root 0711   created here (0711, not 0700: vsftpd chdirs after
                                            dropping privileges — see the paths table); per-account
                                            jails are created by the agent
/etc/maran/vsftpd          root:root 0755   the agent's config directory (the config file itself is 0644 root:root)
/run/maran-ftps/empty      root:root 0755   secure_chroot_dir; created by the unit, not by this step
```

`/var/lib/maran` is `panel:panel 0750` (step 40), and an unprivileged uid that owns an ancestor of a
chroot can rename a level aside and leave an entry of its own at the name every customer's jail hangs
under. That is the escalation the SFTP jail base was moved to escape, and this base is placed at the
sibling for the same reason and with the same words in the file.

**The PAM service, written in full because it is the entire authorization boundary:**

```
#%PAM-1.0
# Maran FTPS — managed by installer/lib/89-ftps.sh, do not edit between markers.
#
# A service of our own, never an edit to /etc/pam.d/vsftpd: both families' stock
# file includes pam_shells.so, and every login this panel creates has a nologin
# shell, so the stock stack refuses all of them. Editing a file the distribution
# owns to work around that would also change what the packaged daemon accepts.
#
# The first line is the authorization, and it is the only one: a login that is
# not in the maran-ftps group cannot authenticate here at all. root, the panel
# uid, every hosting account and every other system account are therefore
# refused by this stack whether or not they hold a password — which is a
# stronger statement than a deny-list file, because it is a property of what
# this stack contains rather than of what somebody remembered to add to a list.
auth     required   pam_succeed_if.so user ingroup maran-ftps quiet_success
auth     required   pam_unix.so
account  required   pam_succeed_if.so user ingroup maran-ftps quiet_success
account  required   pam_unix.so
session  required   pam_unix.so
```

`pam_unix.so` and not `password-auth`/`common-auth`: those aggregates pull in the host's whole
authentication policy — `pam_faillock`, `pam_sss`, a directory service an operator has configured —
and an FTPS login is a local shadow entry and nothing else. Including them would make this daemon's
answer depend on configuration nobody wrote for it.

**The unit, written in full:**

```ini
[Unit]
Description=Maran FTPS (vsftpd)
Documentation=man:vsftpd.conf(5)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
# background=NO on the command line, not in the config file: the two families
# disagree about the default (Debian NO, RHEL YES, measured 2026-09-08), and a
# daemon that forks under Type=simple is one systemd immediately considers dead.
ExecStart=/usr/sbin/vsftpd /etc/maran/vsftpd/vsftpd.conf -obackground=NO
# vsftpd's own privilege-separation directory: it must exist, be empty, and not
# be writable by the unprivileged user the daemon drops to. RuntimeDirectory
# gives all three and takes it away again on stop, so no stale directory can
# outlive the daemon and be replaced by something else.
RuntimeDirectory=maran-ftps/empty
RuntimeDirectoryMode=0755
Restart=on-failure
RestartSec=2s

[Install]
WantedBy=multi-user.target
```

- [x] **Step 0: Write the threat note — before any privileged line exists**

`rules/security.md`'s escalation applies to this plan three times over — a new listening port, a new
PAM stack, and a new privileged installer step — and its rule is exact: *"The threat note is written
**first**, before the change"*; written after the fact it is a justification validating a choice
already made. This task is the first one that writes a privileged surface, so the note is this
task's first step, on disk before any line of `installer/lib/89-ftps.sh`, the PAM stack or the unit
is written.

Write `docs/superpowers/notes/2026-09-XX-ftps-threat-note.md` (dated the day it is written). It:

- states the second-reviewer requirement as **OUTSTANDING**;
- names what a reviewer must check: the PAM stack's refusal of a non-member, the
  mask-before-install ordering, the jail's ancestor chain, and the passive range's exposure;
- names what the author could not verify (an agent session cannot dispatch a reviewer, and cannot
  observe a real internet-facing install);
- records that suspension locks *passwords*, so an already-authenticated vsftpd session survives
  until it disconnects — bounded by `idle_session_timeout=600` — one sentence, so a reviewer weighs
  it rather than discovers it;
- records that systemd hardening directives for `maran-ftps.service` were weighed and deliberately
  not prescribed: most of them conflict with what vsftpd does by design (chroot into `/var/lib`
  with bind mounts of `/home`, setuid children), and a unit that looks hardened while
  contradicting the daemon is worse than one that states the weighing.

The branch may land; it MUST NOT merge to `main` until a human has read the note (Task 16 Step 4
verifies the note exists and still says OUTSTANDING, and the owner is told at hand-off).

- [x] **Step 1: Write the step file, one function per action**

Follow `86-sftp.sh` exactly in shape: `set -euo pipefail`, `readonly` constants at the top with the
reason for each, marker-delimited blocks for anything written into a file that already existed, and
a `step_ftps` that is a list of calls and nothing else.

`vsftpd_packages_for_family` is the only place a package name appears, mirroring
`mysql_packages_for_family` in `85-mysql.sh`, so a package rename stops the polygon build rather
than a customer's install.

- [x] **Step 2: Wire it into `install.sh` and `uninstall.sh`**

`run_step 89-ftps.sh step_ftps`, between `88-cron.sh` and `90-finish.sh`. The uninstaller stops and
disables `maran-ftps.service`, removes the unit, removes `/etc/pam.d/maran-ftps`,
`/etc/logrotate.d/maran-ftps` and `/etc/maran/vsftpd`, unmasks the packaged unit, and —
deliberately — **removes neither
`/var/lib/maran-ftps` nor any login**. The jails hold live bind mounts of customers' homes and an
`rm -rf` across one deletes the home it points at; the uninstaller says what is left and how to
finish by hand, exactly as `report_legacy_jails` does in step 86.

- [x] **Step 3: Prove the step is idempotent by running it twice**

Run it, capture `stat -c '%U:%G %a'` for all three directories plus the PAM file, the logrotate
file and the unit, run it again, and `diff` the captures. **What would make this fail:** a `groupadd` without its `getent`
guard, an `install -d` that does not restate the mode, or a marker block appended rather than
replaced.

- [x] **Step 4: Assert what this environment can actually observe about "not listening"**

Where this step runs — a container image build with no booted systemd — the packaged postinst
cannot start the daemon whether or not the mask exists, so a bare "nothing is bound to port 21"
check is green either way: a check that cannot fail where it runs is decoration
(`rules/testing.md`). The assertions here are therefore on the artifacts a filesystem can answer
for, and the blind spot is printed rather than papered over:

- `readlink -f /etc/systemd/system/vsftpd.service` is `/dev/null` — the mask, observed as the
  symlink it is. **What would make this fail:** removing the `mask_packaged_vsftpd` call, which is
  precisely the mutation Task 3 scores.
- `systemctl is-enabled maran-ftps` says `disabled` — also a symlink fact, answerable without a
  booted systemd.
- The `ss -lntp` port-21 line still runs, prefixed with its own honesty:
  `echo "UNOBSERVED HERE: whether the postinst would have started the packaged daemon — no systemd boots in this image; the live fact is observed in ftps_on_a_real_host.rs (Task 10), where one does"`.

---

### Task 3: The polygon proves step 89, on both families

**Files:**
- Modify: `docker/polygon/ubuntu24.Dockerfile`, `docker/polygon/alma9.Dockerfile`
- Modify: `docker/polygon/assert-installer-steps.sh`

**Interfaces:**
- Consumes: `installer/lib/89-ftps.sh`'s public functions.
- Produces: build-time assertions that fail the image build — and therefore every polygon suite — if
  step 89 stops doing its job.

**The rule this task exists to obey.** An installer step "proven" by a test that passes when vsftpd is
absent proves nothing. Both Dockerfiles therefore `COPY installer/lib/89-ftps.sh` and the assert
script **runs the step's own functions**; it never repeats their work. The image installs the package
through `install_vsftpd_package`, so a package name that stops being right on a family stops that
image's build.

> **AMENDED AFTER EXECUTION — 2026-09-09.** This task shipped on 2026-09-09: eight assertions in
> `docker/polygon/assert-installer-steps.sh`, twenty mutations each shown red on both families, and
> the Step 5 composite hand-driven per family. **No assertion it produced was changed by this
> amendment and none needs to be.** Checked one by one against findings F2 and F3: **no assertion
> here names a TLS option key** — the five in this task are about a group, a directory's owner and
> mode, a PAM directive, a mask symlink and a planted symlink — so the wrong spelling could not have
> been caught here and nothing here now carries it. **No assertion here names the `.wants` entry**
> either; the executed script prints its state on both branches prefixed with
> `NOT THE CONTROL either way:`, which is exactly why the contested row (Task 1) broke nothing. What
> changed in this text is Step 5's sentence, which now says which family's TLS key spelling the
> hand-driven config must use — a correction to an instruction, matching what the executed run
> actually had to do to make the daemon start at all.
>
> **AMENDED AFTER EXECUTION AGAIN — 2026-09-09, fourth pass.** This task's report
> (a working task-03 report) is where finding F3 was raised, and **its premise was
> backwards**: it recorded that vsftpd takes the FIRST occurrence of a key, and the plan carried that
> into three places in good faith. Re-measured with controls on both families
> (a working jail-mode report's Step 4b): the parser takes the **LAST** occurrence — a
> duplicated `listen_port` bound the appended value, and a duplicated `force_local_logins_ssl`
> behaved as the appended value in both directions on `ubuntu:24.04` and `almalinux:9`. Recorded here
> rather than only at the end of the plan because a reader who arrives at this executed task's report
> for its F3 finding must not leave with the wrong half. **No assertion this task shipped is affected
> and none needs re-running:** its five polygon assertions are about a group, a directory's owner and
> mode, a PAM directive, a mask symlink and a planted symlink; none of them reads a vsftpd config
> file, so none of them could have depended on the ordering in either direction. What the correction
> changes is the plan's unexecuted tasks — Global Constraints, Task 6, Task 8 and Task 10 — and it
> raises the stakes there rather than lowering them: under the corrected reading an appended
> `force_local_logins_ssl=NO` silently disables forced TLS instead of being inert.

- [x] **Step 1: `COPY` the step into both images and register it in the assert script's file list**

`readonly FTPS_STEP="${INSTALLER_LIB}/89-ftps.sh"` beside the others, and
`require_installer_file "$FTPS_STEP" installer/lib/89-ftps.sh`, so a missing `COPY` is a named
failure rather than a silently skipped assertion.

- [x] **Step 2: Write the assertions**

Each one names what it observes and what would make it fail:

```bash
# The group exists and is a system group. Fails if ensure_ftps_group stops running
# or loses its --system flag.
assert_ftps_group_is_created() {
  ensure_ftps_group
  getent group maran-ftps >/dev/null || fail "89-ftps.sh did not create the maran-ftps group"
  local gid; gid="$(getent group maran-ftps | cut -d: -f3)"
  [ "$gid" -lt 1000 ] || fail "maran-ftps is not a system group (gid ${gid})"
}

# The jail base is root-owned and 0711 (NOT 0700 — see the paths table: vsftpd chdirs
# into the home AFTER dropping privileges), and is NOT under /var/lib/maran. Fails if the
# base moves back under a directory the panel uid owns — the defect that made every
# SFTP login on every real install fail.
assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root() {
  ensure_ftps_directories
  local owner mode
  owner="$(stat -c '%U:%G' /var/lib/maran-ftps)"
  mode="$(stat -c '%a' /var/lib/maran-ftps)"
  [ "$owner" = "root:root" ] || fail "/var/lib/maran-ftps is ${owner}, must be root:root"
  [ "$mode" = "711" ] || fail "/var/lib/maran-ftps is mode ${mode}, must be 711"
  case "$(readlink -f /var/lib/maran-ftps)" in
    /var/lib/maran/*) fail "the FTPS jail base is under the panel-owned state root" ;;
  esac
}

# The PAM stack refuses a login that is not in the group. Asserted on the FILE, and the
# assertion says so: this image boots no daemon, so what a real pam_authenticate would
# do is UNOBSERVED HERE and is proven in ftps_on_a_real_host.rs (Task 10) instead.
assert_ftps_pam_stack_requires_group_membership() {
  install_ftps_pam_service
  grep -q 'pam_succeed_if.so user ingroup maran-ftps' /etc/pam.d/maran-ftps \
    || fail "/etc/pam.d/maran-ftps does not require maran-ftps membership"
  grep -q 'password-auth\|common-auth' /etc/pam.d/maran-ftps \
    && fail "/etc/pam.d/maran-ftps pulls in the host's aggregate stack"
  echo "UNOBSERVED HERE: whether pam_authenticate really refuses a non-member; see ftps_on_a_real_host.rs"
}

# The packaged unit is masked BEFORE the package lands, so its postinst cannot start it.
# Fails if mask_packaged_vsftpd is removed or moved after install_vsftpd_package.
assert_the_packaged_daemon_was_never_startable() {
  [ "$(readlink -f /etc/systemd/system/vsftpd.service)" = "/dev/null" ] \
    || fail "vsftpd.service is not masked; the package's postinst could start it"
}

# The inverse control. A gate that only ever sees good input passes even when it refuses
# everything, so this feeds the step a host it must REFUSE: a /var/lib/maran-ftps that is
# a symlink. ensure_ftps_directories must replace it rather than follow it. Each half
# asserts on an axis that can actually go red: the link is gone, what stands at the name
# is a real root-owned 0711 directory, and the link's target was never written into —
# following the symlink would have chmod'd or populated /tmp/planted, so its emptiness
# is the observation that nothing went through the link.
assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed() {
  rm -rf /var/lib/maran-ftps /tmp/planted && mkdir -p /tmp/planted
  ln -s /tmp/planted /var/lib/maran-ftps
  ensure_ftps_directories
  [ -L /var/lib/maran-ftps ] && fail "ensure_ftps_directories followed a planted symlink"
  [ -d /var/lib/maran-ftps ] || fail "the jail base is not a real directory after the replace"
  [ "$(stat -c '%U:%G %a' /var/lib/maran-ftps)" = "root:root 711" ] \
    || fail "the replaced jail base is not root:root 0711"
  [ -z "$(ls -A /tmp/planted)" ] || fail "ensure_ftps_directories wrote through the symlink"
  rm -rf /tmp/planted
}
```

- [x] **Step 3: Build both images and watch every assertion run**

Run: `source scripts/dev && maran polygon build`
**What would make this fail:** any of the five above; and, because the assert script is `set -euo
pipefail` and every check `fail`s by name, a check whose subject is missing from the image reports
the missing `COPY` rather than passing quietly.

- [x] **Step 4: Mutation pass on the installer step**

Three mutations, each alone, each scored against a full image build:

| Mutation | Named assertion that must go red |
|---|---|
| Delete the `mask_packaged_vsftpd` call from `step_ftps` | `assert_the_packaged_daemon_was_never_startable` |
| Change the jail base to `/var/lib/maran/ftps` | `assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root` |
| Drop the `pam_succeed_if` line from the PAM stack | `assert_ftps_pam_stack_requires_group_membership` |

Restore with a fresh mtime and verify each restore with `cmp`, per `rules/testing.md`. A mutation
whose image fails to BUILD has measured nothing and is retried with a mutation that builds.

- [x] **Step 5 (polygon proof, not a design spike): hand-drive one FTPS login end to end**

The design above is decided, not open — this step **proves** the composite it rests on, the way the
SFTP plan's Task 4 Step 0 proved its jail before a line of code depended on it. Every mechanism in
this plan is individually measured; what no measurement above has exercised is all of them
*together*: vsftpd + `/etc/pam.d/maran-ftps` + a `--non-unique` login on the account's uid +
`chroot_local_user` into a root-owned jail + the bind-mounted home + forced TLS. Six tasks build on
that composite, so it is proven here, by hand, in a booted Ubuntu polygon container — before
Phase B starts.

In the container: run step 89's own functions, create an account home exactly as plan 3 leaves it
(`<account>:<web server group> 0750`), build the jail and the bind mount by hand
(`/var/lib/maran-ftps/alice` root:root 0755, `home` bind-mounted), write a minimal `vsftpd.conf`
carrying the Task 6 template's confinement and TLS pairs plus a self-signed certificate — **in the
TLS version-key spelling of the family the container is (Task 1's fact table, F2 row): the other
family's spelling makes vsftpd exit 2 with no message, which is how F2 was found here** — start the
daemon, create `alice_files` with `useradd --non-unique --uid <alice's uid> --gid <alice's gid>
--shell <nologin> --groups maran-ftps --home /var/lib/maran-ftps/alice --no-create-home`, set a
password over `chpasswd` stdin, and assert four things on the real host:

1. `curl --ssl-reqd ftp://…` logs in over TLS, lands in the jail, and a file it uploads into
   `home/` is owned by the *account's* uid in `/home/alice` — the `--non-unique` property, observed.
2. `curl` WITHOUT `--ssl-reqd` (plain FTP) cannot log in — the owner's decision 2, observed.
3. A system login with a valid password and no `maran-ftps` membership is refused — the PAM gate,
   observed (this is what Task 3's file-level assertion declared UNOBSERVED).
4. `cd /etc` and `cd /home` from inside the session both fail — the chroot, observed.

Record the transcript in the ledger. If any of the four fails, that is a finding that stops the
plan — the design is revisited here, where one hand-built host is the cost, not in Task 10, where
six tasks of code are.

---

## Phase B — the agent's foundations

### Task 4: The jail, the login name, and the one value an operator types

**Files:**
- Create: `agent/crates/agent-core/src/validation/system/ftps_user_name.rs`, `ftps_user_name_error.rs`
- Create: `agent/crates/agent-core/src/validation/web/passive_address.rs`, `passive_address_error.rs`
- Create: `agent/crates/ops/src/ftps/mod.rs`, `ops/src/ftps/model/mod.rs`, `ops/src/ftps/model/ftps_jail.rs`
- Create: `agent/crates/ops/src/logins/mod.rs`, `ops/src/logins/systemd_escape.rs` (the escaping rule extracted from `sftp/model/account_jail.rs`, which starts calling it in this task)
- Modify: `agent/crates/ops/src/lib.rs`, `ops/src/sftp/model/account_jail.rs`, `agent/crates/agent-core/src/agent_paths.rs`, `validation/system/mod.rs`, `validation/web/mod.rs`
- Modify: `rules/rust.md` (canonical layout rows), `scripts/lib/check-structure.sh` (the new subject names)
- Test: `agent-core/src/tests/validation/system/ftps_user_name_tests.rs`, `tests/validation/web/passive_address_tests.rs`, `agent-core/src/tests/agent_paths_tests.rs`, `ops/src/tests/ftps/ftps_jail_tests.rs`

**Interfaces:**
- Consumes: `AccountName`, `prefixed_name.rs` (the shared prefix/decode machinery `SftpUserName` is built on), `AgentPaths`.
- Produces:
  - `AgentPaths::FTPS_JAIL_ROOT` = `"/var/lib/maran-ftps"`.
  - `FtpsUserName::for_account(&AccountName, requested: &str) -> Result<FtpsUserName, FtpsUserNameError>`, `as_str()` returning `<account>_<name>`, and `decode(&AccountName, &str) -> Option<FtpsUserName>` for reading a login back out of the password database. The 32-byte `useradd` ceiling and the refusal of the separator are inherited from `prefixed_name.rs` and are not re-implemented.
  - `PassiveAddress::parse(&str) -> Result<PassiveAddress, PassiveAddressError>`, `as_str()`. Accepts an IPv4 dotted quad and nothing else.
  - `FtpsJail::for_account(&AccountName, systemd_unit_directory: &str) -> FtpsJail` with `directory()`, `mount_point()`, `source_directory()`, `unit_name()`, `unit_path()` — the same five derived values `AccountJail` produces, under `FTPS_JAIL_ROOT`.

**Why `FtpsJail` is a second type and not a parameter on `AccountJail`.** The two jails are the same
shape and different lifetimes: an account can hold SFTP logins and no FTPS ones, or the reverse, and
deleting the last login of one protocol must not unmount the other. A single type parameterised by
root would make "which jail am I tearing down" a value that has to be right at every call site,
including the account-deletion cascade where getting it wrong unmounts a live customer home. Two
types make the wrong one a compile error. What they DO share — systemd's path-escaping rule, which
must produce byte-for-byte the name systemd derives from `Where=` — is extracted into one function
both call, because that rule getting out of step with itself is a mount unit systemd silently refuses
to load and a login that lands in an empty directory.

- [x] **Step 1: Write the failing tests**

```rust
#[test]
fn an_ftps_login_is_named_after_the_account_that_owns_it() {
    let account = AccountName::parse("alice").expect("valid account");
    let login = FtpsUserName::for_account(&account, "web").expect("valid login");
    assert_eq!(login.as_str(), "alice_web");
}

#[test]
fn a_passive_address_that_is_not_a_dotted_quad_is_refused() {
    // The one operator-typed value that reaches vsftpd.conf, which is a
    // line-oriented key=value file: a newline in it appends a directive of
    // somebody else's choosing to a file the daemon reads as root.
    for candidate in ["203.0.113.7\npasv_min_port=1", "ftp.example.com", "203.0.113", "", " 203.0.113.7"] {
        assert!(PassiveAddress::parse(candidate).is_err(), "{candidate:?} must be refused");
    }
    assert_eq!(
        PassiveAddress::parse("203.0.113.7").expect("valid").as_str(),
        "203.0.113.7"
    );
}

#[test]
fn the_jail_unit_name_is_systemds_own_escaping_of_its_mount_point() {
    let account = AccountName::parse("alice").expect("valid account");
    let jail = FtpsJail::for_account(&account, "/etc/systemd/system");
    assert_eq!(jail.directory(), "/var/lib/maran-ftps/alice");
    assert_eq!(jail.mount_point(), "/var/lib/maran-ftps/alice/home");
    assert_eq!(jail.source_directory(), "/home/alice");
    // systemd escapes `/` to `-` and `-` to `\x2d`; a unit whose file name is not
    // the escaping of its own Where= is one systemd refuses to load, and the
    // failure appears only on a real host as a login landing in an empty jail.
    assert_eq!(jail.unit_name(), r"var-lib-maran\x2dftps-alice-home.mount");
    assert_eq!(
        jail.unit_path(),
        r"/etc/systemd/system/var-lib-maran\x2dftps-alice-home.mount"
    );
}

#[test]
fn the_jail_root_is_not_inside_the_panel_owned_state_root() {
    // The escalation that made every SFTP login on every real install fail: an
    // unprivileged uid owning an ancestor of a chroot can rename a level aside
    // and leave an entry of its own at the name every customer's jail hangs under.
    assert!(!AgentPaths::FTPS_JAIL_ROOT.starts_with("/var/lib/maran/"));
    assert_eq!(AgentPaths::FTPS_JAIL_ROOT, "/var/lib/maran-ftps");
}
```

- [x] **Step 2: Run and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-agent-core ftps && cargo test -p maran-agent-core passive_address && cargo test -p maran-ops ftps_jail`
Expected: FAIL — none of the four types exist.

- [x] **Step 3: Implement, reusing rather than copying**

`FtpsUserName` delegates to `prefixed_name.rs`. The escaping rule moves out of
`sftp/model/account_jail.rs` into `ops/src/logins/systemd_escape.rs` **in this task**, and both jail
types call it — one rule, one implementation, because a unit whose file name is not the escaping of
its own `Where=` is one systemd silently refuses to load. `PassiveAddress` parses with
`std::net::Ipv4Addr::from_str` and then requires the input to be byte-identical to the parsed
address's `to_string()`, which is what refuses `010.0.0.1` and a trailing space without a hand-written
character loop.

- [x] **Step 4: Run and watch them pass**

- [x] **Step 5: Mutation pass**

Remove the newline rejection from `PassiveAddress` and confirm the named refusal test dies. Change
`FTPS_JAIL_ROOT` to `/var/lib/maran/ftps` and confirm the ancestor test dies. Replace the escaping
call with a plain `replace('/', "-")` and confirm the unit-name test dies — that mutation is the one
that would ship a mount unit systemd refuses.

---

### Task 5: `ops::logins` — one place that knows every login an account holds

**Files:**
- Create: `agent/crates/ops/src/logins/{account_logins,set_account_logins_locked,logins_error,logins_host,process_logins_host}.rs` — and **not** `logins/mod.rs` or `logins/systemd_escape.rs`: Task 4 creates both, because the jail types it builds there already need the escaping rule, and a second task claiming to create a file that exists is a task an executor cannot verify it has done. **Nor `inspect_account_logins.rs`** — see the amendment note below
- Create: `agent/crates/ops/src/logins/model/{mod,account_login,login_protocol,account_login_set}.rs` — every produced type gets its own file under `model/`, per the operation anatomy; a Produces list with no file for `AccountLoginSet` would leave the executor inventing a location
- Modify: `agent/crates/ops/src/logins/mod.rs` (Task 4's file gains this task's six declarations and its `model` sub-module), `agent/crates/ops/src/sftp/mod.rs`, `sftp_host.rs`, `process_sftp_host.rs`, `model/account_jail.rs`, `agent/crates/ops/src/lib.rs`
- Modify: `rules/rust.md` — the canonical agent layout gains the `ops/src/logins/model/` row here, in the task that creates that folder. Task 4 added the `ops/src/logins/` row when it created the folder; Task 16 Step 5 only verifies that both are there, and a step that verifies a row nobody was told to write is a step that fails
- Modify: `agent/crates/ops/src/accounts/account_operations.rs` (it imports `inspect_account_logins` from `crate::sftp` — at line 17, not the line 14 this plan wrote — and its suspension-state path calls it; the import re-points at `ops::logins`, and at `account_logins`, which is the name the read landed under) and `agent/crates/ops/src/accounts/model/account_suspension_state.rs` (its `sftp_logins: Vec<SftpLoginSuspensionFact>` field becomes the `AccountLoginSet` this module returns, so the protocol and the unmanaged count can reach the wire in Task 10 and the panel's attestation in Task 14)
- Delete: `agent/crates/ops/src/sftp/{inspect_account_logins,set_account_logins_locked}.rs` and `model/sftp_login_suspension_fact.rs` — moved, with their tests, not deleted outright; the fact type's role is taken over by `AccountLogin`, which carries the protocol beside the name and the lock state
- Modify: `agent/crates/agent/src/services/sftp/sftp_service.rs` (the `SetAccountLoginsLocked` rpc handler re-points at `ops::logins`)
- Test: `ops/src/tests/logins/{fake_logins_host,account_logins_tests,set_account_logins_locked_tests}.rs` (beside the `systemd_escape_tests.rs` Task 4 moved there), plus the moved suspension-fact tests and the touched `ops/src/tests/accounts/account_operations_tests.rs`

**Interfaces:**
- Consumes: `AccountName`, `SftpUserName`, `FtpsUserName`, `AccountJail`, `FtpsJail`, `DistroAdapter::{passwd_database, passwd_binary, usermod_binary}`.
- Produces:
  - `enum LoginProtocol { Sftp, Ftps }`.
  - `struct AccountLogin { pub name: String, pub protocol: LoginProtocol, pub locked: bool }`.
  - `account_logins(&dyn LoginsHost, &dyn DistroAdapter, &AccountName) -> Result<AccountLoginSet, LoginsError>` where `AccountLoginSet { pub logins: Vec<AccountLogin>, pub unmanaged: u32 }`.
  - `set_account_logins_locked(..., locked: bool) -> Result<AccountLoginSet, LoginsError>` — the write, returning what it observed afterwards.
  - `LoginsError`: `AccountMissing`, `SpawnFailed { code: i32 }`, `StatusUnreadable`, `AccountBusy`.

> **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass). Two names in this block are not what shipped.**
>
> **There is no `inspect_account_logins` in `ops::logins`, and there should not be.** This block
> listed it as a produced function and its file and its test file, carried over from the two shipped
> `ops::sftp` operations this task moved. Written into `logins/` it would have been a second name for
> `account_logins` with an identical body — `rules/rust.md`'s rule-of-two run backwards ("could one be
> replaced by a call to the other with nothing observable changing?": yes) and `rules/testing.md`'s
> "a defensive call that cannot fail is deleted, not labelled". **`account_logins` IS the read**, and
> it is the name `account_operations.rs`, the real-host suite and Task 9's ownership check all call it
> by. The `SetAccountLoginsLocked` and `GetAccountSuspensionState` rpcs keep their wire names, which
> is what the "the two operations keep their names and their rpc" row in the reuse table is about; the
> ops-layer function name is the one that changed. (a working task-05 report,
> deviation 1.)
>
> **`LoginsError` has a fourth variant, `AccountBusy`.** Without it the moved write could not keep
> taking the hosting account's lock, and dropping that lock would have been a silent regression: a
> login created between the enumeration and the last `usermod` would not be locked by the suspension
> that was running. No new lock was introduced — it is the same per-account lock the shipped
> operations take, and it never waits.

**The defect this task closes, in one paragraph.** `ProcessSftpHost::account_logins` selects passwd
rows with `row.home == jail_directory`, where the jail is the **SFTP** jail. An FTPS login's passwd
home is the FTPS jail, so on the day the first FTPS login exists, suspending its account would leave
it authenticating — the account's own `usermod --lock` does not reach a `--non-unique` login, and
this enumeration would not see it. That is the identical defect the SFTP rpc's own doc comment
describes, one protocol later, and it is closed before an FTPS login can exist rather than after.
Moving the enumeration rather than teaching `ops::sftp` about FTPS is `rules/architecture.md`'s rule
about facilities: a thing two areas need is its own area, never a lodger in the one that needed it
first.

**`unmanaged`, and why the count is not decoration.** Selecting by "the home is one of the two jails"
is precise and it is also a blind spot: a passwd entry sharing the account's uid whose home is
neither jail is invisible to this function and therefore to a suspension. Those entries are not
created by this panel and are not locked, and pretending otherwise is exactly the shape of check this
repository has been burned by. So `account_logins` also counts them, `GetAccountSuspensionStateOk`
carries the count as an additive field (Task 10), and the panel's suspension attestation reports it —
**in Task 14**, which names the DTOs and the two handlers that read the count and the protocol, so
the sentence in this paragraph is a task and not an aspiration. **A
positive control proves the count can see**: a test plants a passwd row on the account's uid with a
home of `/home/alice` and asserts `unmanaged == 1`, so a counter that has gone blind fails rather
than reading zero.

- [x] **Step 1: Move the two operations and their tests, unchanged, and run the suite**

No behaviour change in this step. `maran structure` must stay green (the `src/tests/` mirror moves
with the code), and the per-target rows in `scripts/test-baseline.txt` must be **identical** — the
baseline counts per test target, and a unit-test move inside `maran-ops` stays inside the same
target, so it renames no row; a changed row here means a test was lost or gained, and a move that
loses a test looks exactly like a move that worked.

Run: `source scripts/dev && maran test`
Expected: the identical rows and totals as the committed baseline.

- [x] **Step 2: Write the failing tests for the second protocol and the count**

```rust
#[test]
fn an_ftps_login_is_found_and_labelled_by_the_jail_its_home_is() {
    let host = FakeLoginsHost::with_passwd(&[
        ("alice",      1001, "/home/alice"),
        ("alice_web",  1001, "/var/lib/maran-sftp/alice"),
        ("alice_files",1001, "/var/lib/maran-ftps/alice"),
        ("bob_web",    1002, "/var/lib/maran-sftp/bob"),
    ]);

    let found = account_logins(&host, &FakeDistro, &AccountName::parse("alice").unwrap()).unwrap();

    assert_eq!(
        found.logins.iter().map(|l| (l.name.as_str(), l.protocol)).collect::<Vec<_>>(),
        vec![("alice_files", LoginProtocol::Ftps), ("alice_web", LoginProtocol::Sftp)]
    );
}

#[test]
fn suspending_an_account_locks_its_ftps_login_too() {
    // The whole reason this module exists. Before the move, this login was
    // invisible to the enumeration and a suspended customer kept a working
    // write credential into their own home.
    let host = FakeLoginsHost::with_passwd(&[("alice_files", 1001, "/var/lib/maran-ftps/alice")]);

    set_account_logins_locked(&host, &FakeDistro, &AccountName::parse("alice").unwrap(), true).unwrap();

    assert_eq!(host.locked_users(), vec!["alice_files"]);
}

#[test]
fn a_login_on_the_accounts_uid_that_is_in_neither_jail_is_counted_and_not_locked() {
    let host = FakeLoginsHost::with_passwd(&[
        ("alice_files", 1001, "/var/lib/maran-ftps/alice"),
        ("alice_hand",  1001, "/home/alice"),
    ]);

    let found = set_account_logins_locked(&host, &FakeDistro, &AccountName::parse("alice").unwrap(), true).unwrap();

    assert_eq!(found.unmanaged, 1, "a hand-made login on this uid must be COUNTED");
    assert_eq!(host.locked_users(), vec!["alice_files"], "and must not be locked by us");
}
```

- [x] **Step 3: Run and watch the three fail**

- [x] **Step 4: Implement**

`account_logins` reads the passwd database once and classifies each row: home equals the SFTP jail →
`Sftp`; home equals the FTPS jail → `Ftps`; uid equals the account's uid, name is not the account's
own → `unmanaged += 1`. `passwd -S` supplies `locked` per login, matching the FIRST LETTER of the
status field, because `L` on Debian and `LK` on RHEL is a difference that already cost the RHEL
family a working suspension.

- [x] **Step 5: Run and watch them pass, then run the whole workspace**

Run: `source scripts/dev && maran agent check`

- [x] **Step 6: Mutation pass, each mutation alone, scored against the whole workspace**

| Mutation | Named test that must go red |
|---|---|
| Classify only the SFTP jail (drop the FTPS arm) | `an_ftps_login_is_found_and_labelled_by_the_jail_its_home_is`, `suspending_an_account_locks_its_ftps_login_too` |
| Return `unmanaged: 0` unconditionally | `a_login_on_the_accounts_uid_that_is_in_neither_jail_is_counted_and_not_locked` |
| Compare the whole `passwd -S` status field to `"L"` instead of its first letter | the RHEL-shaped locked-status test that moved with the code |

---

### Task 6: `templates::vsftpd` — the daemon's configuration, byte for byte

**Files:**
- Create: `agent/crates/templates/src/vsftpd/mod.rs`, `src/vsftpd/vsftpd_daemon_config.rs` — the file name is the type's own snake_case and not the shorter `daemon_config.rs`: `scripts/lib/check-structure.sh` check 16 reads the single `pub struct` out of every `.rs` under `agent/crates` and requires the basename to be its snake_case, `daemon_config` is in neither the `subject_named` allow-list nor the `*_service|*_status` family, and `maran structure` would have reported *"holds `VsftpdDaemonConfig`, so the file is vsftpd_daemon_config.rs"* before this task's first test ran. With the exact name there is nothing to exempt, which is why this task's Modify list — unlike Tasks 4 and 8 — does not touch `scripts/lib/check-structure.sh`: an allow-list entry added to admit a shorter file name is a gate taught to stop asking
- Create: `agent/crates/templates/templates/vsftpd/vsftpd.conf.j2` (replacing the `.gitkeep`)
- Create: `agent/crates/templates/tests/golden/vsftpd/vsftpd.conf`, `vsftpd_passive_address.conf`, `vsftpd_ipv4_only.conf`, `vsftpd_rhel_tls_keys.conf`
- Create: `agent/crates/distro/src/vsftpd_tls_version_keys.rs` — the third adapter fact, added here and not by the executed Task 1; see that task's amendment note
- Modify: `agent/crates/templates/src/lib.rs`, `tests/golden_test.rs`, `rules/rust.md`
- Modify: `agent/crates/distro/src/lib.rs` (the new module), `distro/src/adapter.rs` (`vsftpd_tls_version_keys`), `distro/src/debian/debian_adapter.rs`, `distro/src/rhel/rhel_adapter.rs`
- Test: `agent/crates/distro/src/tests/debian/debian_adapter_tests.rs`, `distro/src/tests/rhel/rhel_adapter_tests.rs` — each family's spelling pinned as a literal in that family's own test file, the shape `ftps_group`'s tests already have

**Interfaces:**
- Consumes: nothing but plain values — `maran-templates` has no `maran-distro` dependency and this task adds none (verified against `agent/crates/templates/Cargo.toml`, whose only dependencies are `askama` and `thiserror`). This crate renders text and validates nothing; the validated types live in `agent-core`, the distro facts live in `distro`, and the conversion belongs to `ops::ftps`.
- Produces: `VsftpdDaemonConfig { certificate_path: String, private_key_path: String, passive_port_min: u16, passive_port_max: u16, passive_address: Option<String>, max_clients: u32, log_path: String, ipv4_only: bool, tls_v1_key: &'static str, tls_v1_1_key: &'static str, tls_v1_2_key: &'static str }` with `render_config()` — **the house name, corrected by the fifth pass from the `render()` an earlier draft wrote**: every shipped render type spells it `render_config` (`php_fpm/pool.rs`, `nftables/nftables_ruleset.rs`), and the vsftpd type shipped spelling it that way too. `ipv4_only` is settled row 6 reaching the template: the enable path's probe (Task 8) decides it, this crate only renders it — the crate validates nothing and decides nothing, as ever. The three `tls_*_key` fields are finding F2 reaching the template the same way: `ops::ftps` reads them off `DistroAdapter::vsftpd_tls_version_keys()` and hands them over as three plain strings, so the render type carries the family's words without the crate ever learning which family it is rendering for.
- Produces, in `distro`: `VsftpdTlsVersionKeys { tls_v1: &'static str, tls_v1_1: &'static str, tls_v1_2: &'static str }` (`distro/src/vsftpd_tls_version_keys.rs`) and `DistroAdapter::vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys`, answering `{"ssl_tlsv1", "ssl_tlsv11", "ssl_tlsv12"}` on the Debian family and `{"ssl_tlsv1", "ssl_tlsv1_1", "ssl_tlsv1_2"}` on the RHEL family.

**Why the TLS version keys are an adapter method, and why the method has three fields and not two.**
Measured 2026-09-09 (F2): Ubuntu 24.04's `vsftpd 3.0.5-0ubuntu3.1` knows `ssl_tlsv1`, `ssl_tlsv11`,
`ssl_tlsv12`, `ssl_tlsv13`; AlmaLinux 9's `vsftpd 3.0.5-8.el9` knows `ssl_tlsv1`, `ssl_tlsv1_1`,
`ssl_tlsv1_2`, `ssl_tlsv1_3`. That is a **platform fact**, and `rules/architecture.md` allows it in
exactly one place: *"No platform fact appears as a literal outside the `distro` crate … If a change
needs a second branch, the adapter is missing a method."* It is the same shape as this plan's
`vsftpd_binary()` and `ftps_group()` — a per-family answer, each family's value measured on that
family's own polygon image and pinned by a literal in that family's own adapter test.

The method carries **all three keys the template writes**, including `ssl_tlsv1`, which is spelled
identically on both families today. Carrying only the two that differ would leave the template
holding one key of a four-key family as a literal and receiving the others as values, so the next
reader cannot tell by looking which of them is safe to type and which is a trap — and the identical
one is precisely the trap, because it is the one that tempts. It does **not** carry a `tls_v1_3`
field: the template does not write that key, and an adapter method that answers a question nothing
asks is the speculative abstraction `rules/architecture.md` (DRY and YAGNI) refuses.

**What the daemon is left to decide, stated because an earlier draft got it wrong.** TLS 1.3 is not
written at all. The retired comment in this task's template said *"There is no `ssl_tlsv1_3`
option"*, and that is false on **both** families — there is one, spelled two ways. It is left unset
because the build's own default enables it and that was observed rather than assumed: Task 3 Step 5
negotiated **TLSv1.3** on both the ubuntu24 and the alma9 polygon, with no 1.3 key in either config.
Writing a third differing key to restate a default would buy nothing and add a third thing to get
wrong per family. **One caveat, because the two observations were not identical and a reader must
not be told they were:** the alma9 run carried `ssl_tlsv1_2=YES`, while the ubuntu24 run reached
TLSv1.3 with the 1.1/1.2 lines *removed altogether* — that is how F2 was isolated, by deleting the
two lines the daemon would not accept. So "the Debian family, rendered with `ssl_tlsv12=YES`,
negotiates TLSv1.3" is inferred from two halves and not yet observed as one. Step 3 below is what
makes the Debian daemon accept that render, and Task 10's real login is what observes the version it
then negotiates — both on that family's own image, before anything depends on it.

**The template, in full.** Every line that is not vsftpd's default is here because a default was
wrong for this design, and each says so.

```
# Rendered by the Maran agent. Hand edits are overwritten on the next apply.
#
# Explicit FTPS (AUTH TLS, RFC 4217) on the control port, not implicit FTPS on
# 990: one port to open, and the same port an operator already understands.

{%- if ipv4_only %}
# IPv4 only. The enable path probed an IPv6 listening bind the way vsftpd would
# and the kernel refused it (IPv6 is disabled on this host), so the daemon
# listens on an IPv4 socket instead of the dual-stack default below. Chosen by
# the agent at enable time and reported in the status; never a setting.
listen=YES
listen_ipv6=NO
{%- else %}
# listen_ipv6 rather than listen, which is what both distributions' own shipped
# configuration does: an AF_INET6 socket accepts IPv4 clients too on a Linux host
# with the default net.ipv6.bindv6only=0, so one daemon serves both families of
# address. The two options are mutually exclusive; setting both is a refusal.
listen=NO
listen_ipv6=YES
{%- endif %}
listen_port=21

anonymous_enable=NO
local_enable=YES
write_enable=YES
local_umask=022
# Off: it reads a .message file out of the customer's own directory and prints it
# to every client. Harmless and pointless, and one less file the customer controls
# that the daemon interprets.
dirmessage_enable=NO
# Listings show `ftp` rather than the numeric owner. Every login on one account
# shares that account's uid, so the ids leak nothing between tenants — but they do
# tell a customer the uid the panel assigned them, which is a fact they have no use
# for and an attacker does.
hide_ids=YES

xferlog_enable=YES
xferlog_std_format=NO
vsftpd_log_file={{ log_path }}

# The confinement. chroot_local_user chroots each login into its own passwd home,
# which the agent set to that account's root-owned jail; the account's real files
# are the `home` directory bind-mounted inside it. allow_writeable_chroot stays NO
# — the jail is root:root 0755 precisely so that this guard never has to be
# disabled, and a login that could write to its own chroot root is one vsftpd
# itself refuses to serve.
chroot_local_user=YES
allow_writeable_chroot=NO
secure_chroot_dir=/run/maran-ftps/empty
nopriv_user=nobody

# Our own PAM stack, never the distribution's: both families' stock /etc/pam.d/vsftpd
# includes pam_shells.so, and every login this panel creates has a nologin shell.
# The stack's first line requires membership of the maran-ftps group, which is the
# entire authorization for this daemon.
pam_service_name=maran-ftps

# Passive data. The range is fixed and opened in the firewall as one rule; it is not
# negotiable at runtime because nftables has to be told about it in advance. The FTP
# conntrack helper, which would open these dynamically, cannot help here: it reads
# the PASV reply off the control channel, and this control channel is encrypted.
pasv_enable=YES
pasv_min_port={{ passive_port_min }}
pasv_max_port={{ passive_port_max }}
{%- if let Some(address) = passive_address %}
# The address advertised in the PASV reply, for a host behind NAT whose public
# address is not the one the socket is bound to. Validated as an IPv4 dotted quad
# before it reaches this file.
pasv_address={{ address }}
{%- endif %}
# The data connection's source port does not need to be 20. Not needing a
# privileged source port means the child never needs the capability to bind one.
connect_from_port_20=NO

max_clients={{ max_clients }}
max_per_ip=10
idle_session_timeout=600
data_connection_timeout=120

# TLS, and the owner's decision that it is refused-if-absent. force_local_logins_ssl
# means the password cannot cross the wire before AUTH TLS; force_local_data_ssl
# means the file cannot either. A client that will not negotiate is refused at
# connect time rather than after it has sent a credential.
ssl_enable=YES
force_local_logins_ssl=YES
force_local_data_ssl=YES
rsa_cert_file={{ certificate_path }}
rsa_private_key_file={{ private_key_path }}
# The floor. TLS 1.0 and 1.1 are refused; 1.2 is on; 1.3 is left at the build's
# own default, which enables it: both families were observed negotiating TLSv1.3
# with no 1.3 option in the file, so writing one would restate a default in a
# third per-family spelling and buy nothing.
#
# The three key NAMES are rendered rather than typed, because they are a distro
# fact: the Debian family's build spells this family of options ssl_tlsv11 /
# ssl_tlsv12 / ssl_tlsv13 and the RHEL family's spells them ssl_tlsv1_1 /
# ssl_tlsv1_2 / ssl_tlsv1_3 (measured 2026-09-09). The wrong spelling is an
# unrecognised variable, and on the Debian family vsftpd then exits 2 printing
# NOTHING — a daemon that never starts and never says why. The names come from
# DistroAdapter::vsftpd_tls_version_keys(); this crate never learns which family
# it is rendering for.
{{ tls_v1_2_key }}=YES
{{ tls_v1_1_key }}=NO
{{ tls_v1_key }}=NO
ssl_sslv2=NO
ssl_sslv3=NO
# vsftpd's own default here is DES-CBC3-SHA, a single 3DES suite that no modern
# client offers and no reviewer would accept. Set explicitly, always.
ssl_ciphers=HIGH
# vsftpd defaults this to YES, which requires the data connection to resume the
# control connection's TLS session. TLS 1.3 ticket-based resumption and most
# graphical clients do not do that, and the failure it produces is the worst kind:
# the login succeeds and the directory listing hangs.
require_ssl_reuse=NO
```

**`seccomp_sandbox` is deliberately not set here.** vsftpd's own default applies. It is called out
because its known failure mode is specific and would otherwise be a mystery: vsftpd 3.0.x's syscall
filter has needed downstream patches as glibc moved, and when it bites, a session dies mid-command
with `500 OOPS: priv_sock_get_cmd` or a child killed by `SIGSYS`. Task 10's real-host transfer is
what observes it. If it appears there, the fix is one line (`seccomp_sandbox=NO`) plus the measured
message and kernel in this comment plus a golden update — and the sandbox is not what this design
relies on, which is the chroot, the uid and the PAM group.

- [x] **Step 0: Add the third adapter method, with each family's spelling measured on that family**

This is the half of finding F2 that belongs to the `distro` crate, and it is done first because the
render type cannot be written without it. Write `VsftpdTlsVersionKeys` in
`distro/src/vsftpd_tls_version_keys.rs` (three `&'static str` members, doc comment on the type and
on every member, per `rules/rust.md`), add `vsftpd_tls_version_keys` to `DistroAdapter` with a doc
comment that says what the value is FOR (three key names written verbatim into a rendered
`vsftpd.conf`) and what the failure looks like when it is wrong on the Debian family (exit 2, no
output), and implement it on both families. Then pin each family's literals in that family's own
test file, the way `debian_names_the_ftp_daemon_where_the_package_puts_it` pins `vsftpd_binary`:

```rust
#[test]
fn the_debian_family_spells_the_tls_version_options_without_underscores() {
    let keys = DebianAdapter.vsftpd_tls_version_keys();
    assert_eq!(keys.tls_v1, "ssl_tlsv1");
    assert_eq!(keys.tls_v1_1, "ssl_tlsv11");
    assert_eq!(keys.tls_v1_2, "ssl_tlsv12");
}
```

and, in `distro/src/tests/rhel/rhel_adapter_tests.rs`, the same three assertions against
`RhelAdapter` with `"ssl_tlsv1"`, `"ssl_tlsv1_1"` and `"ssl_tlsv1_2"`. **Why a test that just
restates a constant is not a tautology here:** the two families' values must DIFFER, and a
copy-paste of one adapter into the other is exactly how this ends up wrong; each file pins its own
family's measured word, so a drift is a named failure in the family that drifted. The values
themselves were measured on Ubuntu 24.04 and AlmaLinux 9 (Task 1's fact table, F2 row); the other
supported versions of each family are covered by the family's polygon image running Step 3 below,
which asks the real binary rather than trusting the row.

- [x] **Step 1: Write the golden test first, with no golden file**

`tests/golden_test.rs` gains four cases: the default dual-stack render, the render with a passive
address, the IPv4-only render (`vsftpd_ipv4_only.conf`) the enable path falls back to, and the
default render carrying the RHEL family's TLS key spelling (`vsftpd_rhel_tls_keys.conf`).
Run it and watch it fail with "golden file not found" — which is the point: the golden is created by
looking at the render and approving it, never by writing the file first.

**Why one extra golden and not a per-family pair.** The key spelling varies independently of the two
axes the other goldens exist for — listen mode and passive address — so a per-family pair of all
three would be six files showing the same two-line difference three times, and `rules/testing.md`
is explicit that *the golden diff IS the review artifact*: three copies of one diff teach a reviewer
less than one, and each extra copy is another file that has to be updated in lockstep for any
unrelated template change. One extra golden, rendered with everything else at its default, makes the
diff between `vsftpd.conf` and `vsftpd_rhel_tls_keys.conf` **exactly the two lines the families
disagree about** — the 1.1 and the 1.2 keys, since `ssl_tlsv1` is spelled the same word on both.
The fact, isolated, which is what a golden is for. The first three goldens carry
the Debian family's spelling, and the test names say so rather than leaving it to be inferred
(`the_default_render_carries_the_debian_family_tls_keys`,
`the_rhel_family_render_differs_only_in_its_tls_version_keys`); the second of those additionally
asserts the two renders differ in **two** lines and no others, so a future template change that
accidentally makes the two families' configs diverge somewhere else fails here by name rather than
being absorbed into a second golden nobody diffs.

- [x] **Step 2: Implement the render type and the template, then create all four goldens from the output**

Read the render before approving it. The diff of a golden IS the review artifact.

- [x] **Step 3: Assert the goldens against the real daemon, on BOTH families, not just against themselves**

A golden proves the render is stable; it does not prove vsftpd accepts it — and F2 is precisely a
config that two goldens can agree on and one family's daemon refuses. Add a `#[ignore]`d polygon test
in Task 10 that renders this config **with the running host's own adapter** and real paths, and runs
the Task 8 validator over it. **What would make this fail:** a typo in any key — measured,
`500 OOPS: unrecognised variable in config file: <key>` on the RHEL family and **exit 2 with no
output at all** on the Debian family — or a value vsftpd rejects. **The both-families part is not
decoration and is what F2 costs to prevent:** a key that exists on one family and not the other is
invisible to a suite that only ever runs on one, so this test must be scored on the ubuntu24 image
AND the alma9 image, and Task 16 Step 2's `maran polygon verify` run is where that is enforced.
F2 was found because Task 3's hand-driven proof reached a real daemon; this step is the automated
version of the same question, and it is the reason it must not be scheduled after five more tasks of
code — Step 0 above now puts the fact in the adapter before anything is written against it.

- [x] **Step 4: Mutation pass**

Flip `force_local_logins_ssl` to `NO` and confirm the golden test goes red by name. Delete the
`ssl_ciphers` line and confirm the same. Both are protections whose removal would otherwise be
invisible: the daemon starts happily either way. Then hardcode the dual-stack pair — render
`listen=NO`/`listen_ipv6=YES` regardless of `ipv4_only` — and confirm the `vsftpd_ipv4_only.conf`
golden goes red: that mutation is a daemon that silently cannot bind on every IPv6-disabled host.

Then the two mutations finding F2 owes:

| Mutation | Named test that must go red |
|---|---|
| Give `DebianAdapter` the RHEL spelling (`ssl_tlsv1_2`, `ssl_tlsv1_1`) | **the Debian adapter test, and Step 3's real-daemon check on the ubuntu24 polygon — TWO layers, not three.** ~~the `vsftpd.conf` golden~~ — CORRECTED 2026-09-12: the golden CANNOT see this mutation. `agent/crates/templates/Cargo.toml`'s `[dependencies]` are `askama` and `thiserror` and no `maran-distro` (by design — `templates` renders text and knows no platform), so `golden_test.rs:481-491` writes BOTH families' spellings out as its own literals and the render it compares never consults an adapter. A harness told to expect three layers here and finding two red would report a kill as a partial miss, or worse read the golden's silence as the protection being untested — the false-confidence direction rules/testing.md is written against. The mutation itself is unchanged and still owed |
| Hardcode either spelling into the template instead of rendering `{{ tls_v1_2_key }}` | `the_rhel_family_render_differs_only_in_its_tls_version_keys` — the `vsftpd_rhel_tls_keys.conf` golden stops differing where it must |

The second one is the mutation that matters, because it is the shape the plan itself shipped for a
day: a spelling typed once into a template reads as correct on the family the author happened to
have in front of them.

**Finding F3, in one line where it belongs — with its premise corrected.** vsftpd takes the **LAST**
occurrence of a key; an earlier pass of this plan wrote "the first occurrence" here and in the Global
Constraints, taken from finding F3 in a working task-03 report, and it was backwards.
A working jail-mode report's Step 4b measured it with controls on both families on
2026-09-09: a duplicated `listen_port` bound the second value, and a duplicated
`force_local_logins_ssl` behaved as the appended value in both directions. This template is therefore
rendered and written whole, through `ops::safe_write`, which replaces the file — nothing anywhere
merges a key into an existing `vsftpd.conf`, on the first apply or on a re-apply, and no second writer
of that path exists. What changes with the correction is what an append would COST: not an inert line,
but the `force_local_logins_ssl=YES` this template renders silently overridden on a config that still
parses and still starts. See the Global Constraints bullet for the full statement, and note that the `-o`
command-line settings are no longer described as an exception to the rule — being applied after the
file is read makes them the last setting of their key, which is the rule itself.

---

### Task 7: `ops::ssl::certificate_state` — the marker, read and never parsed

**Files:**
- Create: `agent/crates/ops/src/ssl/certificate_state.rs`, `ops/src/ssl/model/certificate_state.rs`
- Modify: `agent/crates/ops/src/ssl/mod.rs`
- Test: `ops/src/tests/ssl/certificate_state_tests.rs`

**Interfaces:**
- Consumes: `SiteCertificate::for_domain`, `SslHost::read_material`, and the crate-private `self_signed_marker`.
- Produces: `certificate_state(&dyn SslHost, &Domain) -> Result<CertificateState, SslOpError>` returning
  `CertificateState { pub certificate_path: String, pub private_key_path: String, pub present: bool, pub is_self_signed_placeholder: bool }`.

**Why this is one new read-only unit and not a second opinion.** FTPS needs two facts about the
material it points vsftpd at: is it there, and is it the placeholder a customer's client will warn
about. Both already have an authoritative answer inside `ops::ssl` — the files exist, and
`self-signed.marker` sits beside them — and the whole reason the marker is a FILE is that the
previous design asked openssl to print a subject and parsed the text. A reviewer broke that with a
certificate whose organisation is `Example, OU = maran-self-signed, more`: openssl quotes a value
containing a comma rather than escaping it, a splitter without quoting produced a fragment that
trimmed to an exact match, and a customer's certificate and private key were destroyed by the check
that existed to protect them. **This function therefore performs no parsing of any kind and spawns
no process.** It answers `path.exists()` twice. Anything in this plan that wants to know what a
certificate IS asks this function; nothing in this plan reads a certificate's text.

- [x] **Step 1: Write the failing tests**

```rust
#[test]
fn material_with_a_marker_beside_it_is_reported_as_a_placeholder() {
    let host = FakeSslHost::with_material("ftp.example.test", MARKER_PRESENT);
    let state = certificate_state(&host, &Domain::parse("ftp.example.test").unwrap()).unwrap();
    assert!(state.present);
    assert!(state.is_self_signed_placeholder);
}

#[test]
fn material_without_a_marker_is_never_reported_as_a_placeholder_however_its_subject_reads() {
    // The regression this function must not reintroduce. The certificate below is
    // self-signed AND its subject contains the marker text; without the FILE it is
    // somebody's real certificate and must be reported as one.
    let host = FakeSslHost::with_material_named(
        "ftp.example.test",
        "/O=Example, OU = maran-self-signed, more/CN=ftp.example.test",
        MARKER_ABSENT,
    );
    let state = certificate_state(&host, &Domain::parse("ftp.example.test").unwrap()).unwrap();
    assert!(state.present);
    assert!(!state.is_self_signed_placeholder);
}

#[test]
fn a_domain_with_no_material_is_absent_rather_than_an_error() {
    let host = FakeSslHost::empty();
    let state = certificate_state(&host, &Domain::parse("ftp.example.test").unwrap()).unwrap();
    assert!(!state.present);
    assert!(!state.is_self_signed_placeholder);
    // The paths are still returned: the caller's refusal message names where the
    // material would have to be.
    assert!(state.certificate_path.ends_with("/ftp.example.test/fullchain.pem"));
}
```

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Implement — four lines of body and a long doc comment**

The doc comment is written forward from the question ("how do I know what this certificate is?") and
names the destroyed-key incident as the reason the answer is a file. It also states the limit: this
function does not verify that the material is valid, in date, or matched — `install_certificate`
owns all three and is the only thing that writes here.

- [x] **Step 4: Run and watch them pass**

- [x] **Step 5: Mutation pass**

Make `is_self_signed_placeholder` true whenever the certificate's subject contains
`maran-self-signed`, and confirm
`material_without_a_marker_is_never_reported_as_a_placeholder_however_its_subject_reads` goes red.
That mutation is the historical bug, re-run as an experiment.

---

## Phase C — the agent's operations

### Task 8: `ops::ftps` — enable, disable, and a status that observed something

**Files:**
- Create: `agent/crates/ops/src/ftps/{ftps_error,ftps_host,process_ftps_host,enable_ftps,disable_ftps,get_ftps_status,reload_ftps_tls,probe_listen_mode,validate_candidate_config}.rs`
- Create: `agent/crates/ops/src/ftps/model/{ftps_configuration,ftps_state,listen_mode,candidate_outcome}.rs`
- Modify: `agent/crates/ops/src/ftps/mod.rs` (created in Task 4), `agent-core/src/agent_paths.rs` (the config path), `scripts/lib/check-structure.sh`, `rules/rust.md`
- Test: `ops/src/tests/ftps/{fake_ftps_host,enable_ftps_tests,disable_ftps_tests,get_ftps_status_tests,reload_ftps_tls_tests,probe_listen_mode_tests,validate_candidate_config_tests}.rs`

**Interfaces:**
- Consumes: `templates::vsftpd::VsftpdDaemonConfig`, `ops::safe_write::render_validate_swap`, `ops::ssl::certificate_state`, `DistroAdapter::{vsftpd_binary, ftps_group, service_manager, vsftpd_tls_version_keys}`, `Domain`, `PassiveAddress`, `Port`. `vsftpd_tls_version_keys` is finding F2's adapter method (added in Task 6): this is the one place that reads it, and it copies its three strings straight into `VsftpdDaemonConfig` — `ops` branches on no family and the templates crate learns of none.
- Produces:
  - `AgentPaths::VSFTPD_CONFIG_PATH` = `"/etc/maran/vsftpd/vsftpd.conf"` and `AgentPaths::FTPS_UNIT` = `"maran-ftps.service"` — agent decisions, identical on every family, so they belong there and not on the adapter. **The log path is NOT one of them:** `const FTPS_LOG_PATH: &str = "/var/log/maran/ftps.log"` is a documented private const in `ops/src/ftps/enable_ftps.rs`, beside the one function that renders it. See the amendment note below.
  - `FtpsConfiguration { hostname: Domain, passive_port_min: Port, passive_port_max: Port, passive_address: Option<PassiveAddress>, max_clients: u32 }`.
  - `enable_ftps(&dyn FtpsHost, &dyn DistroAdapter, &FtpsConfiguration) -> Result<FtpsState, FtpsError>` — idempotent; probes the listen mode, renders, validates, swaps, restarts, observes, rolls back. Named `enable_ftps` and not `apply_ftps_configuration`, because `rules/rust.md` is exact: *"Its name matches the proto rpc in snake_case. The mapping rpc → file is 1:1 and mechanical"* — and the rpc is `EnableFtps`.
  - `disable_ftps(&dyn FtpsHost, &dyn DistroAdapter) -> Result<FtpsState, FtpsError>` — stops and disables the unit. Idempotent; a daemon that is already off is a success that touches nothing.
  - `get_ftps_status(&dyn FtpsHost, &dyn DistroAdapter, hostname: Option<&Domain>) -> Result<FtpsState, FtpsError>` — read-only; the file name is the rpc's, same law.
  - `reload_ftps_tls(&dyn FtpsHost, &dyn DistroAdapter) -> Result<FtpsState, FtpsError>` — the operation behind the `ReloadFtpsTls` rpc, which previously had **no ops home at all**: a service method may only do proto → ops → response, so the restart it performs has to live here. It restarts a RUNNING daemon so vsftpd re-reads the certificate material that was replaced underneath it, then re-asks the two post-swap questions (unit active, `220` on the control port) and returns `NotListening`/`ServiceRefused` if either answer is no. On a stopped or disabled daemon it is a no-op success — a daemon that is off picks the new material up at its next start by construction, and "reload TLS" must never be the thing that turns a deliberately-disabled daemon on.
  - `probe_listen_mode(&dyn FtpsHost) -> ListenMode` — settled row 6's deterministic probe, a named unit so a unit test can drive it. `ProcessFtpsHost` binds an IPv6 TCP listening socket the way vsftpd would — `[::]:0`, `SOCK_STREAM` — and drops it; the classification treats a refusal with `EAFNOSUPPORT` or `EADDRNOTAVAIL` (std's `ErrorKind::Unsupported` / `ErrorKind::AddrNotAvailable`) as "this kernel has no IPv6" and answers `ListenMode::Ipv4Only`; success, and every OTHER refusal, answers `ListenMode::DualStack` — an unexpected bind error is not evidence about address families, and the daemon's own start (layer 2) will surface the real problem and roll back.
  - `ListenMode { DualStack, Ipv4Only }` in `model/listen_mode.rs`.
  - `FtpsState { pub running: bool, pub control_port_answered: bool, pub certificate: Option<CertificateState>, pub forced_tls: Option<bool>, pub passive_port_min: Option<u16>, pub passive_port_max: Option<u16>, pub listen_mode: Option<ListenMode> }` — **seven fields, four of them optional, and one of them not in this plan's first four passes**; see the amendment note under this bullet. `enable_ftps` reports the mode it probed and rendered; `get_ftps_status` reports the mode the LIVE config carries, by checking which of the template's two exact listen pairs the file on disk contains — the agent is the only writer of that file and the pairs are golden-pinned, so this is the agent reading its own decision back, not parsing foreign configuration. **That single-writer property is load-bearing, and the argument for it was rewritten in the fourth pass because the premise it rested on inverted.** An earlier pass argued: vsftpd takes the FIRST occurrence of a key, so a file that had ever been appended to could contain both listen pairs and this read would answer with whichever it found first regardless of which one the daemon obeyed. The parser in fact takes the **LAST** occurrence (measured with controls on both families, a working jail-mode report's Step 4b), which makes the failure worse-shaped rather than milder: a read that matches the pair the template rendered would report the mode the panel *intended* while the daemon obeyed the pair appended after it — a check that agrees with the panel's own decision no matter what the daemon is doing, which is exactly the blind gate `rules/testing.md` spends a section on. **The conclusion survives and gains one rule.** It survives because nothing appends and the agent is the only writer (Global Constraints), which is what makes "read the pair back" a sound question rather than a guess. The rule the correction adds: **this read takes the LAST occurrence of each key it looks at, never the first**, so that where the file and the daemon could disagree the agent answers with what the daemon obeys — and a live file carrying BOTH listen pairs, or either forced-TLS key more than once, is by construction not a file this agent wrote, which is the case Task 10's `the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen` exists to make observable rather than assumed.
  - `FtpsError`: `CertificateMissing { domain: String, expected_path: String }`, `ConfigRejected { output: String, output_is_unavailable_on_this_platform: bool }`, `ServiceRefused { unit: String }`, `NotListening`, `SpawnFailed { code: i32 }`, `Render`, `ConfigUnreadable`, `ConfigWrite` — **eight, not the six an earlier pass listed**: a live config that cannot be READ is a different answer from one that cannot be written, and both are different from a render failure, so the status read-back and `render_validate_swap`'s own refusal each get a variant rather than being folded into `SpawnFailed`.

> **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass). Five statements above were wrong about what
> this task shipped; the code is right and the text was not.**
>
> **`AgentPaths::FTPS_LOG_PATH` cannot exist, and the reason is a gate, not a preference.** Placing it
> there made `maran structure` fail: check 20 requires every absolute `AgentPaths` constant outside
> `/etc` to appear in `installer/systemd/maran-agent.service`'s `ReadWritePaths=`. The agent never
> opens `/var/log/maran/ftps.log` — it renders the path into a configuration, and vsftpd, running
> under its OWN unit, is what creates and writes it. Putting it in `AgentPaths` would have made the
> agent's declared writable set claim a file it never touches, which is the drift that gate exists to
> catch, pointed the wrong way. It is not a platform fact either (both families take the same path),
> so it is a private const beside `enable_ftps`, with that argument in its doc comment.
> `VSFTPD_CONFIG_PATH` is under `/etc` and therefore excluded from check 20; `FTPS_UNIT` is a unit
> name and not a path. Both stayed. (a working task-08 report, PHANTOM 2.)
>
> **`FtpsState`'s four optional fields are the honest shape, not a convenience.** A status read taken
> while the daemon is off, or before any config has ever been written, has no live file to read a
> passive range, a listen mode or a forced-TLS answer out of — and a `0`, a `DualStack` or a `false`
> in those positions would be a fabricated observation, which is exactly the blind-gate shape this
> plan condemns. `None` says "not observed here"; the wire's nine scalar fields (**nine, not the
> eight this sentence said until the sixth pass — `forced_tls = 9` shipped; see the amendment under
> Task 10's status block for the measurement**) are filled from the
> `Some` cases and the panel renders the absence as absence.
>
> **`forced_tls: Option<bool>` is a field this plan did not know it had.** The live-config read-back
> already answers whether the daemon on disk is still forcing TLS, and four named tests hold it up —
> `a_planted_force_local_logins_ssl_no_is_reported_as_forced_tls_switched_off`,
> `a_planted_force_local_data_ssl_no_is_reported_as_forced_tls_switched_off`, and the two
> last-occurrence tests killed by mutant 7 (dropping `.rev()` from the live read). The consequence is
> stated where it belongs, in "Known risks that are not open questions": the detection this plan
> called unbuilt **exists at the ops layer**, and what is missing is only its ninth wire field, its
> DTO, its screen line and three locales.
>
> > **CORRECTED 2026-09-09 (sixth pass): the ninth wire field is no longer missing.** `forced_tls = 9`
> > is on all four `Ok` messages, is in the accepted `contract-baseline.txt`, and is filled by
> > `services/ftps/ftps_status_fields.rs`. What remains outstanding is the panel-and-screen half —
> > and per the Known Risks row that is the owner's to decide, not an amendment's. Stated here only
> > so the next reader does not go looking for a wire field that is already there.
>
> **The model folder holds four files, not three:** `ftps_configuration.rs`, `ftps_state.rs`,
> `listen_mode.rs` and `candidate_outcome.rs` — `CandidateOutcome` is what
> `validate_candidate_config` answers with — `StillRunning` (accepted) and `Exited { output }`
> (refused, whatever the status, with the output empty on every Debian-family refusal) — and
> it is a produced type, so per the operation anatomy it has its own file and is re-exported from
> `ftps/mod.rs`.
>
> **Two entries on this task's Modify list were not needed and are deliberately untouched:**
> `scripts/lib/check-structure.sh` — the shipped checks already accept this area unchanged
> (`maran structure` answers `STRUCTURE-OK`), and an allow-list entry added where none is needed is a
> gate taught to stop asking; and `rules/rust.md`, whose `ops/src/ftps/` and `services/ftps/` rows
> were written by the earlier tasks that created those folders. Task 4's Modify list carries the same
> `check-structure.sh` entry and the same answer applies to it. What `rules/rust.md` is still owed is
> the last-occurrence rule for a live-config read: it lives today only in this plan and in the doc
> comments of `get_ftps_status.rs` and `ftps_state.rs`, and Task 16 Step 5 is where it lands in the
> rules.

**The two-layer validation, and what each layer can and cannot see.** vsftpd has no `-t`. That was
measured rather than assumed, and so was everything below, on Ubuntu 24.04 and AlmaLinux 9 on
2026-09-08:

| Candidate config | Ubuntu 24.04 | AlmaLinux 9 |
|---|---|---|
| good, inetd mode (`listen=NO`) | exit **2**, no output | exit **2**, `500 OOPS: vsftpd: not configured for standalone, must be started from inetd` |
| good, `-obackground=NO -olisten=YES` | **still running** after 2 s | **still running** after 2 s |
| good, no `-obackground` | still running | **exit 0** — RHEL defaults `background=YES` |
| bad bool | exit **2**, no output | exit **2**, `500 OOPS: bad bool value in config file for: anonymous_enable` |
| unknown key | exit **2**, no output | exit **2**, `500 OOPS: unrecognised variable in config file: not_a_real_option` |
| unloadable certificate | exit **2**, no output | exit **2**, `500 OOPS: SSL: cannot load RSA certificate` |
| port already bound | exit **2**, no output | exit **2**, `500 OOPS: could not bind listening IPv4 socket` |

Two conclusions the design is built on. **The exit code is only a signal in standalone mode** — in
inetd mode a good config exits 2 as well, so an inetd-mode check is decoration. And **the Debian
family prints nothing at all**, so a gate that greps the output would report every Debian config as
good; a gate that greps for the good-case message would report every Debian config as bad. Both are
the blind gate `rules/testing.md` spends a section on.

So:

- **Layer 1, before the swap — `validate_candidate_config`.** Spawn
  `vsftpd <candidate> -obackground=NO -olisten=YES -olisten_ipv6=NO -olisten_address=127.0.0.1 -olisten_port=<ephemeral>`
  and poll for exit for at most 1500 ms. **Exited ⇒ refused**, with stdout carried in
  `ConfigRejected.output` and `output_is_unavailable_on_this_platform` set when it is empty, so the
  panel's operator-facing message says "vsftpd refused the configuration and this platform's build
  prints no reason" rather than pretending to know. **Still running ⇒ accepted**, and the child is
  killed. The ephemeral port is obtained by binding `127.0.0.1:0`, reading the assigned port and
  dropping the socket. Its blind spot, stated in the code: if that port is taken in the microseconds
  between drop and spawn, the candidate is refused for a reason that is not its own — which fails
  safe, because nothing has been swapped, and is reported as a refusal the operator can retry.
- **Layer 2, after the swap — the daemon's own answer.** `render_validate_swap`'s reload step
  restarts `maran-ftps.service`, then asks two questions the config file cannot answer: does the
  service manager report the unit active, and does a TCP connect to the control port return a `220`
  greeting. Either answer being no restores the previous config, restarts again, and returns
  `NotListening` or `ServiceRefused`. This is the layer that catches what a parse cannot: a
  certificate whose key does not match, a port something else already holds, a unit systemd will not
  start.

**The blind spot both layers share, stated because the fourth pass made it matter.** Neither layer can
see an edit made to the live file AFTER the swap. Layer 1 validates the text the agent itself just
rendered, so it never meets a later edit at all; layer 2 asks whether the unit is active and whether
the control port greets with `220`, and a config whose forced-TLS keys have been switched off answers
both questions perfectly — it is a healthy daemon, doing the wrong thing. While the constraint read
"the first occurrence wins" this was uninteresting, because an appended line was inert. Under the
measured behaviour, one appended `force_local_logins_ssl=NO` disables the feature's whole promise and
every check in this task still reports green. What observes it is not a third validation layer here —
there is nothing left in the file for a parser to complain about — but `get_ftps_status`'s live-file
read in the `FtpsState` bullet above (last occurrence, never first) and Task 10's real-host assertion that each key the template writes
appears exactly once, with its planted-duplicate control. This paragraph exists so the next reader of
this section does not conclude that "validated twice" means "cannot be silently unmade".

**Why `enable_ftps` refuses rather than generating certificate material.** It calls
`certificate_state` for the hostname and returns `CertificateMissing { domain, expected_path }` when
there is none. FTPS never writes into the certificate store — see "What the certificate story is,
exactly" above. The refusal names the path, so the panel's message can tell the operator to create
the site and let the SSL module place material there.

- [x] **Step 1: Write the failing tests against a fake host**

```rust
#[test]
fn enabling_ftps_for_a_hostname_with_no_certificate_material_refuses_and_names_the_path() {
    let host = FakeFtpsHost::with_no_certificate();
    let error = enable_ftps(&host, &FakeDistro, &configuration()).unwrap_err();
    assert!(matches!(
        error,
        FtpsError::CertificateMissing { ref expected_path, .. }
            if expected_path == "/etc/maran/certificates/ftp.example.test/fullchain.pem"
    ));
    assert!(host.written_configs().is_empty(), "nothing may be written before the material exists");
}

#[test]
fn a_candidate_the_daemon_refuses_never_reaches_the_live_path() {
    let host = FakeFtpsHost::with_certificate().refusing_candidates();
    assert!(matches!(enable_ftps(&host, &FakeDistro, &configuration()),
                     Err(FtpsError::ConfigRejected { .. })));
    assert_eq!(host.live_config(), None, "the live config was written despite a refusal");
}

#[test]
fn a_daemon_that_starts_but_does_not_answer_on_the_control_port_rolls_the_config_back() {
    // The failure a parse check cannot see: the unit is active and nothing is
    // listening. Measured shape — a certificate whose key does not match parses
    // fine and fails at the first handshake.
    let host = FakeFtpsHost::with_certificate().with_live_config("previous").silent_on_control_port();
    assert!(matches!(enable_ftps(&host, &FakeDistro, &configuration()),
                     Err(FtpsError::NotListening)));
    assert_eq!(host.live_config().as_deref(), Some("previous"));
    assert_eq!(host.restart_count(), 2, "the rollback must restart the daemon on the restored config");
}

#[test]
fn applying_the_same_configuration_twice_writes_once_and_restarts_once() {
    let host = FakeFtpsHost::with_certificate();
    enable_ftps(&host, &FakeDistro, &configuration()).unwrap();
    enable_ftps(&host, &FakeDistro, &configuration()).unwrap();
    assert_eq!(host.restart_count(), 1, "an idempotent apply must not bounce a running daemon");
}

#[test]
fn a_refusal_on_a_platform_that_prints_nothing_says_so_rather_than_inventing_a_reason() {
    let host = FakeFtpsHost::with_certificate().refusing_candidates_silently();
    let error = enable_ftps(&host, &FakeDistro, &configuration()).unwrap_err();
    assert!(matches!(error, FtpsError::ConfigRejected { ref output, output_is_unavailable_on_this_platform }
                     if output.is_empty() && output_is_unavailable_on_this_platform));
}

#[test]
fn a_kernel_that_refuses_an_ipv6_bind_selects_the_ipv4_only_mode() {
    // Settled row 6. EAFNOSUPPORT and EADDRNOTAVAIL are what a kernel with IPv6
    // disabled answers a [::]:0 listening bind with; anything else is not
    // evidence about address families and keeps the dual-stack default.
    let refused = FakeFtpsHost::with_certificate()
        .refusing_ipv6_bind(std::io::ErrorKind::AddrNotAvailable);
    assert_eq!(probe_listen_mode(&refused), ListenMode::Ipv4Only);

    let unrelated = FakeFtpsHost::with_certificate()
        .refusing_ipv6_bind(std::io::ErrorKind::PermissionDenied);
    assert_eq!(probe_listen_mode(&unrelated), ListenMode::DualStack);
}

#[test]
fn enabling_on_an_ipv6_disabled_host_writes_the_ipv4_only_config_and_the_state_says_so() {
    // The whole settlement, end to end through the enable path: the probe's
    // answer must reach the rendered file AND the returned state, because the
    // screen states the mode from the state and the daemon binds from the file.
    let host = FakeFtpsHost::with_certificate().refusing_ipv6_bind(std::io::ErrorKind::Unsupported);
    let state = enable_ftps(&host, &FakeDistro, &configuration()).unwrap();
    assert_eq!(state.listen_mode, ListenMode::Ipv4Only);
    let live = host.live_config().expect("a config was written");
    assert!(live.contains("listen=YES") && live.contains("listen_ipv6=NO"));
}

#[test]
fn reloading_tls_restarts_a_running_daemon_and_reports_a_port_that_stopped_answering() {
    let host = FakeFtpsHost::with_certificate().with_running_daemon();
    reload_ftps_tls(&host, &FakeDistro).unwrap();
    assert_eq!(host.restart_count(), 1);

    let silent = FakeFtpsHost::with_certificate().with_running_daemon().silent_on_control_port();
    assert!(matches!(reload_ftps_tls(&silent, &FakeDistro), Err(FtpsError::NotListening)));
}

#[test]
fn reloading_tls_on_a_stopped_daemon_touches_nothing_and_succeeds() {
    // A daemon that is off reads the new material at its next start by
    // construction, and a TLS reload must never be the thing that starts a
    // deliberately-disabled daemon.
    let host = FakeFtpsHost::with_certificate();
    let state = reload_ftps_tls(&host, &FakeDistro).unwrap();
    assert_eq!(host.restart_count(), 0);
    assert!(!state.running);
}
```

- [x] **Step 2: Run and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-ops ftps`

- [x] **Step 3: Implement**

`ProcessFtpsHost` is the only file that touches the machine: it spawns with argv arrays against
`DistroAdapter::vsftpd_binary()` and `service_manager()`, opens the TCP probe and the IPv6 bind
probe with bounded timeouts, and never assembles a command line. `enable_ftps` runs
`probe_listen_mode` first — the mode is an input to the render — then compares the rendered bytes
with what is on disk, so a repeat apply writes nothing and restarts nothing — the property the
idempotency test names, and the one that keeps a nightly reconcile from bouncing a live daemon.

- [x] **Step 4: Run and watch them pass**

- [x] **Step 5: Mutation pass, each alone, scored against the whole workspace**

| Mutation | Named test that must go red |
|---|---|
| Swap the candidate in before validating | `a_candidate_the_daemon_refuses_never_reaches_the_live_path` |
| Skip the control-port probe and trust `is-active` | `a_daemon_that_starts_but_does_not_answer_on_the_control_port_rolls_the_config_back` |
| Generate a self-signed placeholder instead of refusing when material is missing | `enabling_ftps_for_a_hostname_with_no_certificate_material_refuses_and_names_the_path` |
| Drop the bytes comparison and always write | `applying_the_same_configuration_twice_writes_once_and_restarts_once` |
| Force `probe_listen_mode` to always answer `DualStack` | `a_kernel_that_refuses_an_ipv6_bind_selects_the_ipv4_only_mode`, `enabling_on_an_ipv6_disabled_host_writes_the_ipv4_only_config_and_the_state_says_so` |
| Make `reload_ftps_tls` restart a stopped daemon | `reloading_tls_on_a_stopped_daemon_touches_nothing_and_succeeds` |

**Inverse control, required by `rules/testing.md`:** a validator mutated to refuse everything passes
every test above that only feeds it broken input. `applying_the_same_configuration_twice_...` is that
control — it feeds the gate a config it must ACCEPT — and Task 10's polygon feeds the real daemon a
real render it must accept.

---

### Task 9: `ops::ftps` — the login, and the account it dies with

**Files:**
- Create: `agent/crates/ops/src/ftps/{create_ftps_user,set_ftps_password,delete_ftps_user,remove_account_ftps,ensure_account_jail}.rs`
- Create: `agent/crates/ops/src/ftps/model/ftps_user_request.rs`
- Modify: `agent/crates/ops/src/ftps/{mod,ftps_host,process_ftps_host,ftps_error}.rs` — and the
  `ftps_host.rs`/`process_ftps_host.rs` half of that is the **bulk of this task, not a touch-up**:
  see the note below
- Modify: `agent/crates/ops/src/accounts/account_operations.rs` — deletion is `AccountOperations::delete` (there is no `delete_account.rs`, and writing one would be a second, parallel deletion path beside the real one). Its shipped order is `drop_account_databases → remove_account_sftp → remove_crontab` (under the cron lock) `→ remove_account_pools → require_existing → userdel`; this task inserts `remove_account_ftps` directly after `remove_account_sftp`, and the method's signature gains `&dyn FtpsHost` beside the `php_host, db_host, sftp_host` it already takes.

  > **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass).** This line previously stated the shipped
  > order as `databases → sftp → pools → crontab → userdel`, and that was wrong in two ways. The pool
  > sweep is not third: it was deliberately moved to be the **LAST** step before `userdel`, because a
  > pool written by another operation after the sweep would otherwise survive `userdel` naming a user
  > that no longer exists. The crontab removal runs before it, under `cron_lock`, and there is a final
  > `require_existing` between the sweep and `userdel`. Measured by reading the method
  > (`ops/src/accounts/account_operations.rs`, the `delete` body and the comment blocks above each
  > step). **The insertion point this task named was unaffected and is where the step actually went**
  > — directly after `remove_account_sftp`, which is what shipped. Nothing about the code changed with
  > this correction; only the plan's description of the order it was inserted into.
- Modify: `agent/crates/ops/src/accounts/account_error.rs` — a new `FtpsRemoval` variant wrapping `FtpsError`, beside the shipped `DatabaseRemoval`/`SftpRemoval`/`PoolRemoval`, so a refused FTPS teardown aborts the deletion with `userdel` NOT run, exactly as the doc comment on `delete` already promises for the other three.
- Modify: `agent/crates/agent/src/services/accounts/accounts_service.rs` — the signature ripple: the `DeleteAccount` handler passes the FTPS host through, and the service's generics gain the `FtpsHost` parameter.
- Modify: `agent/crates/agent/tests/account_deletion_on_a_real_host.rs` — the shipped real-host deletion suite (Step 6 below); a cascade whose mount teardown is proven only against a fake whose refusal behaviour the test author invented is not proven.
- Test: `ops/src/tests/ftps/{create_ftps_user_tests,set_ftps_password_tests,delete_ftps_user_tests,remove_account_ftps_tests}.rs`, `ops/src/tests/accounts/account_operations_tests.rs`

**Interfaces:**
- Consumes: `FtpsUserName`, `Password`, `AccountName`, `FtpsJail`, `AccountOwnership` (the shipped type that answers "which uid and gid does this account have"), `templates::systemd::Unit`, `ops::safe_write`.
- Produces:
  - `create_ftps_user(&dyn FtpsHost, &dyn DistroAdapter, &FtpsUserRequest) -> Result<(), FtpsError>` where `FtpsUserRequest { account: AccountName, user: FtpsUserName, password: Password }`.
  - `set_ftps_password(&dyn FtpsHost, &dyn DistroAdapter, &AccountName, &FtpsUserName, &Password) -> Result<(), FtpsError>` — **the account is a parameter, and it is the whole of the operation's safety**; see the amendment note below.
  - `delete_ftps_user(&dyn FtpsHost, &dyn DistroAdapter, &FtpsUserName) -> Result<(), FtpsError>`.
  - `remove_account_ftps(&dyn FtpsHost, &dyn DistroAdapter, &AccountName) -> Result<(), FtpsError>` — every login, then the mount unit, then the jail.
  - New `FtpsError` variants: `AlreadyExists`, `NotFound`, `JailFailed`, `AccountMissing`, `AccountBusy`, `AccountIdentityChanged`, `PasswordRejected`, `StatusUnreadable`, `SuspensionNotRestored` — **nine, not the seven this line listed until the sixth pass and not the four an earlier pass listed**. The last two are the shadow-field re-read `set_ftps_password` rests on and neither was ever named in this list: `StatusUnreadable` is `getent` printing something this agent cannot read as a shadow entry — its own variant because a caller that reads a shadow field decides whether to re-lock on the answer, and an unreadable field read as "no password" would LOCK a working login while read as "has a password" would hand a suspended customer their login back; `SuspensionNotRestored` is the one condition here an operator must act on — the password IS set and the login is OPEN, raised from a re-read of the raw field and never from an exit status, because `usermod --lock` exits zero on a login it did nothing to. Counted against the tree: `grep -cE '^\s{4}[A-Z][A-Za-z]+' agent/crates/ops/src/ftps/ftps_error.rs` → `17`, which is Task 8's eight plus these nine. The code did not change with this correction. `AccountBusy` is the account lock refusing (without it the creation could not take the lock the shipped operations already take); `AccountIdentityChanged` is the identity re-read between the jail work and `useradd`; and `PasswordRejected` exists because a refused `chpasswd` means the login EXISTS and is unchanged, which a bare `SpawnFailed` cannot say.

> **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass): two things this task shipped differently, and
> both matter to Task 10 and to anything downstream.**
>
> **1. `set_ftps_password` carries the account, takes the account's lock, checks ownership, and
> re-asserts the suspension.** The signature written here originally was
> `(&dyn FtpsHost, &dyn DistroAdapter, &FtpsUserName, &Password)`, with no account in it — and with
> no account it can do none of the three:
>
> - **No lock.** The whole operation is a read-modify-write of one shadow field. A suspension landing
>   between the read and `chpasswd` would be unlocked by this operation on the strength of a fact
>   that has expired. The shipped entry point takes the SAME account lock `create_ftps_user`, the
>   account's deletion and the four backup operations take — no new lock, so no new edge in the
>   wait-for graph — and answers `FtpsError::AccountBusy` rather than waiting.
> - **No ownership check.** A login name is `<account>_<name>` and account names may themselves
>   contain the separator, so no decode of the name is unique: a request authorised for account
>   `alice` naming login `b` addresses the system user `alice_b`, which may be the FTPS login of a
>   NEIGHBOURING account. `useradd` refuses a duplicate name so `create_ftps_user` cannot make that
>   mistake; setting a password has no such gate and would write a credential onto another tenant's
>   login. The shipped check is the same jail enumeration `remove_account_ftps` uses — a candidate is
>   this account's login only when its passwd home is exactly this account's FTPS jail, a value the
>   agent itself wrote — and a login the account does not hold is `FtpsError::NotFound`. The jail also
>   separates the two DAEMONS, so this operation cannot re-credential an SFTP login either.
> - **No shadow re-assert.** `chpasswd` REPLACES the whole shadow field, marker and all — so a
>   password reset on a SUSPENDED login silently clears the `!` that `logins::set_account_logins_locked`
>   put there. That is the exact suspension bypass repaired for SFTP on the same day, and it applies
>   to FTPS verbatim, because Task 5's enumeration locks FTPS logins too. The shipped operation reads
>   the stored state before the write and re-locks after it, answering
>   `FtpsError::SuspensionNotRestored` when it cannot.
>
> **This is a requirement on Task 10, not only a record.** `SetFtpsPassword`'s request already carries
> `account_username` beside `ftps_username` (it is shaped like its `SftpService` counterpart), so the
> rpc handler passes the account through to this entry point and inherits all three protections; a
> handler that called any narrower function would reintroduce the bypass. The crate-private
> `set_ftps_password_under_lock` exists only for `create_ftps_user`, which already holds the lock —
> the lock never waits, so an entry point taking it twice would refuse itself — and **it is not the
> function a service method may call.**
>
> **2. `FtpsHost` as Task 8 shipped it had no login surface at all.** Task 8 left it with the daemon
> methods only — `run`, `read_config`, `write_config`, `run_candidate`, `ephemeral_port`,
> `bind_ipv6_listener`, `control_port_greeting`, `certificate_state` — and `run` takes no stdin. Every
> method the login half needs is added by THIS task: `run_with_stdin` (a separate method rather than
> an `Option<&str>` on `run`, so the pipe that carries a password does not look like an ordinary
> parameter at nine daemon call sites), `account_ownership`, `create_directory`, `account_logins`,
> `path_exists`, `remove_file`, `remove_directory`. So "Modify `ftps_host.rs`, `process_ftps_host.rs`"
> is most of this task's work, and an executor who reads it as a touch-up will under-estimate it.
> `ensure_account_jail` additionally reads the live mount unit back and skips an identical write,
> which the SFTP jail step does not do: copying that would restart a live bind mount under an
> account's existing logins every time a second login was made.

**What `create_ftps_user` does, in order, and why each step is where it is.**

1. **Ensure the jail**, idempotently: `/var/lib/maran-ftps/<account>` `root:root 0755`, then
   `<jail>/home` as a mount point — created `root:root 0755` like its parent, the one mode that is
   only ever seen when the mount is NOT up, so an empty root-owned `home` a client lands in is the
   visible signature of a failed mount rather than a mystery — then a systemd `.mount` unit written
   through the config-write protocol and enabled. `0755` and root-owned because vsftpd refuses to serve a login whose chroot
   root the login can write to, and because the login must be able to traverse into `home`. The unit
   is declarative and enabled rather than an imperative `mount` call: an imperative mount is gone at
   the next boot and every FTPS login for that account then lands in an empty directory.
2. **Create the login**: `useradd --non-unique --uid <account uid> --gid <account gid> --shell
   <nologin> --groups maran-ftps --home <jail> --no-create-home <account>_<name>`. The uid is the
   account's, so an uploaded file is owned by the account; the group is the entire authorization the
   PAM stack checks; the home is the jail, which is what `chroot_local_user` chroots to; `nologin`
   and the absence of `maran-sftp` mean the credential opens nothing else on the host.
3. **Set the password**: `chpasswd` reading `<login>:<password>` from **stdin**. Never an argument
   vector — a password on a command line is visible in `ps` to every local user — and the `Password`
   type's alphabet excludes the newline, so the single line cannot become two.

Idempotency, as the contract requires: a login that already exists is `AlreadyExists` and its
password is **not** changed, so retrying a creation whose response was lost cannot reset a credential
the customer has already been shown.

**`delete_ftps_user` removes the login and nothing else.** `userdel` without `-r`. The jail and its
mount are account-level: deleting one login must not unmount an account that still has others. The
account-deletion cascade owns the teardown, and `remove_account_ftps` does it in the only safe
order — every login first, then `systemctl disable --now <unit>`, then remove the unit, then
`remove_dir` (not `remove_dir_all`) on the mount point and the jail. `remove_dir` refuses a directory
that is not empty, which is exactly the property this depends on: a mount point that is still mounted
must NOT be walked into, because what is under it is the customer's home.

- [x] **Step 1: Write the failing tests against a fake host**

```rust
#[test]
fn an_ftps_login_is_created_on_the_accounts_uid_with_no_shell_and_only_the_ftps_group() {
    let host = FakeFtpsHost::for_account("alice", 1001, 1001);
    create_ftps_user(&host, &FakeDistro, &request("alice", "files", "Gen3rated-pw")).unwrap();

    let created = host.created_user().expect("a login was created");
    assert_eq!(created.name, "alice_files");
    assert_eq!(created.uid, 1001);
    assert_eq!(created.gid, 1001);
    assert!(created.shell.ends_with("nologin"));
    assert_eq!(created.groups, vec!["maran-ftps"], "an FTPS login must not join the SFTP group");
    assert_eq!(created.home, "/var/lib/maran-ftps/alice");
}

#[test]
fn the_password_is_set_over_stdin_and_never_appears_in_an_argument_vector() {
    let host = FakeFtpsHost::for_account("alice", 1001, 1001);
    create_ftps_user(&host, &FakeDistro, &request("alice", "files", "Gen3rated-pw")).unwrap();

    let spawn = host.last_spawn().expect("a process set the password");
    assert_eq!(spawn.argv, vec!["/usr/sbin/chpasswd"]);
    assert_eq!(spawn.stdin, "alice_files:Gen3rated-pw\n");
    assert!(!host.spawns().iter().any(|s| s.argv.iter().any(|a| a.contains("Gen3rated-pw"))));
}

#[test]
fn the_jail_is_root_owned_and_not_writable_by_the_login_that_is_chrooted_into_it() {
    let host = FakeFtpsHost::for_account("alice", 1001, 1001);
    create_ftps_user(&host, &FakeDistro, &request("alice", "files", "Gen3rated-pw")).unwrap();

    let jail = host.created_directory("/var/lib/maran-ftps/alice").expect("the jail was created");
    assert_eq!((jail.owner.as_str(), jail.group.as_str(), jail.mode), ("root", "root", 0o755));
}

#[test]
fn creating_a_second_login_for_the_same_account_does_not_write_the_mount_unit_again() {
    let host = FakeFtpsHost::for_account("alice", 1001, 1001);
    create_ftps_user(&host, &FakeDistro, &request("alice", "files", "pw-one")).unwrap();
    create_ftps_user(&host, &FakeDistro, &request("alice", "media", "pw-two")).unwrap();
    assert_eq!(host.written_units().len(), 1);
}

#[test]
fn creating_a_login_that_already_exists_reports_already_exists_and_does_not_reset_its_password() {
    let host = FakeFtpsHost::for_account("alice", 1001, 1001).with_existing_login("alice_files");
    assert!(matches!(create_ftps_user(&host, &FakeDistro, &request("alice", "files", "new-pw")),
                     Err(FtpsError::AlreadyExists)));
    assert!(host.spawns().iter().all(|s| !s.argv.ends_with(&["chpasswd".to_owned()])));
}

#[test]
fn removing_an_accounts_ftps_refuses_to_delete_a_jail_whose_mount_is_still_up() {
    // remove_dir, never remove_dir_all: under that mount point is the customer's
    // real home, and a recursive delete across a live bind mount deletes it.
    let host = FakeFtpsHost::for_account("alice", 1001, 1001).with_jail_still_mounted();
    assert!(matches!(remove_account_ftps(&host, &FakeDistro, &account("alice")), Err(FtpsError::JailFailed)));
    assert!(host.directory_still_exists("/var/lib/maran-ftps/alice/home"));
}

// Plan 4's pool-leak lesson, one daemon later: userdel does not touch a
// --non-unique login, a mount unit, or a jail, and a re-created account of the
// same name would inherit all three. The order is proven CAUSALLY — see the
// amendment note below for why, and for the four tests that shipped.
#[test]
fn an_ftps_jail_that_cannot_be_taken_down_stops_the_deletion_before_userdel_removes_the_home() { /* … */ }

#[test]
fn the_sftp_teardown_still_runs_and_runs_before_the_ftps_one() { /* … */ }

#[test]
fn an_ftps_teardown_that_refuses_leaves_the_sftp_teardown_already_done() { /* … */ }

#[test]
fn an_account_with_no_ftps_at_all_is_deleted_without_the_new_step_refusing() { /* … */ }
```

> **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass): the ordering test this task shipped is not
> the one written here, and the method it used is better.** This step originally specified one test,
> `deleting_an_account_removes_its_ftps_before_the_account_and_leaves_the_sftp_teardown_in_place`,
> taking a **subsequence over a single `hosts.order()` vector** obtained from a
> `FakeCascadeHosts::with_account(…)` fixture. **That fixture does not exist and the test as written
> is unwritable**: the cascade is driven by four separate host fakes, each recording only its own
> calls, and three separate fakes have no shared clock — there is nothing that can produce one
> ordered vector of steps across them (measured while writing the test,
> a working task-09 report's Step 5).
>
> What shipped instead is the house pattern the SFTP half of this same cascade already uses
> (`a_jail_that_cannot_be_taken_down_stops_the_deletion_before_userdel_removes_the_home`): the order
> is proven **causally**, by making one step refuse and observing what did and did not happen. That
> is stronger than a timeline, not a substitute for one — **a step that ran after `userdel` could not
> have prevented `userdel`** — and it is the method this plan should have specified. The four tests
> in `ops/src/tests/accounts/account_operations_tests.rs` and what each proves:
>
> - `an_ftps_jail_that_cannot_be_taken_down_stops_the_deletion_before_userdel_removes_the_home` —
>   the FTPS step precedes `userdel`: its refusal leaves the account standing and `userdel` never ran.
> - `the_sftp_teardown_still_runs_and_runs_before_the_ftps_one` — SFTP precedes FTPS: the SFTP step
>   refuses and the FTPS host is never asked a single question.
> - `an_ftps_teardown_that_refuses_leaves_the_sftp_teardown_already_done` — the FTPS step was ADDED,
>   not substituted: the SFTP teardown is complete when the FTPS one refuses.
> - `an_account_with_no_ftps_at_all_is_deleted_without_the_new_step_refusing` — the inverse control
>   `rules/testing.md` requires: a teardown mutated to refuse everything fails here.
>
> Scored: `cargo test -p maran-ops` 1093 passed / 0 failed / 1 ignored, and mutant M5 (deleting
> `remove_account_ftps(…)?` from the cascade) kills the first of the four by name. Any later task or
> reader looking for a `FakeCascadeHosts` or an `order()` vector should stop: neither exists, and
> writing one would be a fifth fake recording what these four already prove.

- [x] **Step 2: Run and watch all ten fail**

(Six login tests plus the four cascade tests above. This step said *seven* while the cascade was one
`order()` assertion; it is ten now that the ordering is proven causally.)

- [x] **Step 3: Implement, and keep the privilege discipline where it belongs**

Every process here runs as root: managing system accounts is root's work and is not done as the
customer, and nothing in this task writes into a customer's home. There is no `fork_as_account` in
this file and adding one would be a review reject.

- [x] **Step 4: Run and watch them pass**

- [x] **Step 5: Mutation pass**

Add `maran-sftp` to the login's groups → the group test dies. Put the password on the argv → the
stdin test dies. Make the jail `0777` → the ownership test dies. Use `remove_dir_all` → the
still-mounted test dies. Remove `remove_account_ftps` from `AccountOperations::delete` →
`an_ftps_jail_that_cannot_be_taken_down_stops_the_deletion_before_userdel_removes_the_home` dies.
Each mutation alone, scored against the whole workspace with `--no-fail-fast`, and the file restored
with a fresh mtime and verified with `cmp`.

- [x] **Step 6: Extend the real-host deletion suite — the teardown proven against the machine, not a fake**

The fake's refusal behaviour above (`with_jail_still_mounted`, `EBUSY` on `remove_dir`) is invented
by the test author; the shipped suite `agent/crates/agent/tests/account_deletion_on_a_real_host.rs`
exists to ask the machine instead, and it already proves the database/SFTP/pool halves of this
cascade. Extend its first test: after provisioning, also create an FTPS login for the account
(jail, enabled mount unit, `--non-unique` login in the `maran-ftps` group), then delete the
account and assert on the real host that **no login, no mount, no unit and no jail survives** —
`getent passwd` finds no `alice_files`, `findmnt` shows nothing at the jail's mount point,
`systemctl list-unit-files` lists no `var-lib-maran\x2dftps-…` unit, and `/var/lib/maran-ftps/alice`
is gone — and that a re-created account of the same name inherits none of it. This is where
`systemctl disable --now`, `remove_dir` on a live mount point and the EBUSY semantics are observed
for real. Update the suite's declared total in `scripts/test-baseline.txt` in the same change, or
`maran polygon verify`'s fifth axis fails the suite by name.

---

### Task 10: The contract, the service, and a polygon that can say no

**Files:**
- Modify: `proto/agent/v1/common.proto`, `ftp.proto`, `accounts.proto`, `proto/agent/v1/contract-baseline.txt`
- Create: `agent/crates/agent/src/services/ftps/{ftps_service,ftps_status}.rs` — `ftps_status.rs` holds the area's `to_agent_error`, shaped like `sftp_status.rs`'s; the name matches the `*_status` family `check-structure.sh` already exempts, so it needs no subject-list entry
- Create: `agent/crates/agent/tests/ftps_on_a_real_host.rs`
- Modify: `agent/crates/agent/build.rs`, `src/server.rs`, `src/services/mod.rs`, `tests/handshake.rs`, `agent/crates/agent/src/services/accounts/accounts_service.rs` — the suspension response gains the protocol and the unmanaged count HERE: `SftpLoginSuspensionFact` is built in this file, in the `GetAccountSuspensionState` handler's mapping over `state.logins` (that rpc belongs to `AccountsService`), not in `sftp_service.rs`. **The fifth pass replaced a line-number citation here** — it said "lines 338–341", and the mapping is at line 407 today because Task 5's move and Task 9's cascade both edited this file; a line number in a file two lanes are editing is a phantom with a fuse on it, so the seam is named rather than numbered. A plan that wired the new fields anywhere else would ship `unmanaged_logins` on the wire with nothing ever setting it — an attestation reading 0 forever, the exact blind-gate shape this plan condemns elsewhere. The READING half of these two fields is **Task 14** (`FileTransferLoginSuspensionFactDto`, `AccountSuspensionStateDto.UnmanagedLogins`, and the two attestation handlers): a field written here and read nowhere is the same blind gate one layer up, so neither half ships without the other
- Modify: `docker/polygon/{ubuntu24,alma9}.Dockerfile` (vsftpd and an FTP-capable client), `scripts/test-baseline.txt`

**Interfaces:**
- Consumes: everything from Tasks 4–9.
- Produces: `FtpsService`, answering the rpcs below.

**The proto delta, in full.** Every addition; nothing removed, renamed or renumbered.

```proto
// common.proto — new.
// Which file-transfer daemon a login belongs to. NOT called `Protocol`:
// firewall.proto already declares a top-level `Protocol` in this package and
// protoc refuses a second one. It lives here rather than in ftp.proto because
// accounts.proto reads it too.
enum TransferProtocol {
  TRANSFER_PROTOCOL_UNSPECIFIED = 0;
  TRANSFER_PROTOCOL_SFTP = 1;
  TRANSFER_PROTOCOL_FTPS = 2;
}
```

```proto
// accounts.proto — additive field on an existing message.
message SftpLoginSuspensionFact {
  string username = 1;   // unchanged
  bool locked = 2;       // unchanged
  // Which daemon this login belongs to, derived from the jail its passwd home
  // is. Added 2026-09-08 with FTPS: before it, every fact in this list was an
  // SFTP login by construction, so an absent value reads as SFTP for an old
  // caller and that is the correct reading.
  TransferProtocol protocol = 3;
}

message GetAccountSuspensionStateOk {
  // … existing fields unchanged …
  // Logins sharing this account's uid whose home is NEITHER jail — entries this
  // panel did not create and does not lock. Reported rather than hidden: the
  // enumeration selects by jail, so this is the number it deliberately cannot
  // speak for, and a suspension attestation that did not say so would be
  // claiming more than it observed.
  uint32 unmanaged_logins = 9;  // 1-8 are in use; 9 is the next free number
}
```

```proto
// ftp.proto — a second service beside SftpService. Purely additive.
service FtpsService {
  // Renders, validates and applies the FTPS daemon's configuration, then starts
  // it. Idempotent: an unchanged configuration writes nothing and does not
  // restart a running daemon.
  //
  // Refuses with NOT_FOUND when no certificate material exists for `hostname`,
  // naming the path it looked at. This service never creates certificate
  // material: `ops::ssl` owns the store, the pairing check and the self-signed
  // marker, and a second writer of that directory is how a customer's private
  // key gets destroyed.
  rpc EnableFtps(EnableFtpsRequest) returns (EnableFtpsResponse);

  // Stops and disables the daemon. Logins are NOT removed — disabling a service
  // is not revoking credentials, and an operator who re-enables it expects the
  // same customers to be able to log in. Idempotent.
  rpc DisableFtps(DisableFtpsRequest) returns (DisableFtpsResponse);

  // Reports what the daemon is doing: whether the unit is active, whether the
  // control port answered a greeting, the passive range in force, whether
  // the certificate it serves is the agent's own self-signed placeholder, and
  // which listen mode the live configuration carries (dual-stack, or the
  // IPv4-only fallback the enable path chose on an IPv6-disabled kernel).
  rpc GetFtpsStatus(GetFtpsStatusRequest) returns (GetFtpsStatusResponse);

  // Restarts the daemon so it picks up certificate material that has been
  // replaced underneath it. vsftpd loads its certificate once, at start, so
  // this is a restart and it aborts transfers in flight; the panel calls it
  // only when the SSL module reports new material for the FTPS hostname.
  rpc ReloadFtpsTls(ReloadFtpsTlsRequest) returns (ReloadFtpsTlsResponse);

  // Creates an FTPS login: the account's jail and bind mount if they are not
  // there yet, then a system login on the account's uid with a nologin shell and
  // membership of the FTPS group. Idempotent: an existing login returns
  // ALREADY_EXISTS and its password is deliberately NOT changed.
  rpc CreateFtpsUser(CreateFtpsUserRequest) returns (CreateFtpsUserResponse);

  // Sets an existing login's password. The only thing about an FTPS login that
  // is settable; everything else is derived from the account.
  rpc SetFtpsPassword(SetFtpsPasswordRequest) returns (SetFtpsPasswordResponse);

  // Removes a login, and only the login. The account's files are untouched and
  // the jail stays for the account's other logins.
  rpc DeleteFtpsUser(DeleteFtpsUserRequest) returns (DeleteFtpsUserResponse);
}
```

with `EnableFtpsRequest { string hostname = 1; uint32 passive_port_min = 2; uint32 passive_port_max = 3; string passive_address = 4; uint32 max_clients = 5; }` — the range and client-ceiling values are filled by the PANEL from its `FtpsDefaults` (settled row 3): the wire keeps explicit fields because the agent must not own product defaults, and the panel must be the only place those numbers are decided — the three login requests shaped exactly like their `SftpService` counterparts (`account_username`, `ftps_username`, `password` — and **no** chroot field, now or ever), and every response a `oneof` over `<Rpc>Ok` and `AgentError`.

**The status messages, in full** — this plan's own standard is that values which must be typed
exactly right are written out, and these are the fields four screens and one attestation read:

```proto
message GetFtpsStatusRequest {
  // The hostname whose certificate material the answer describes. Empty means
  // "report the daemon and range facts only" — a panel with no hostname
  // persisted yet still gets an answer instead of an error.
  string hostname = 1;
}

message GetFtpsStatusOk {
  // True when the service manager reports maran-ftps.service active.
  bool running = 1;
  // True when a TCP connect to the control port, made on the host itself,
  // returned a `220` greeting. A LOCAL probe: it bypasses nftables, so
  // "answering here" and "unreachable from outside" are compatible — the
  // panel's screen composes this with the Firewall module's rule list (both
  // already exposed over HTTP) to say which; nothing here reads the firewall.
  bool control_port_answered = 2;
  // Whether certificate material exists for the requested hostname, and
  // whether it is the self-signed placeholder — the marker file's existence
  // beside the material, never a parse (see ops::ssl::certificate_state).
  bool certificate_present = 3;
  bool certificate_is_self_signed = 4;
  // Where the material lives (or would have to be placed), so an operator
  // message can name the path. Operator-facing; never shown to a customer.
  string certificate_path = 5;
  // The passive data range the live configuration carries.
  uint32 passive_port_min = 6;
  uint32 passive_port_max = 7;
  // True when the live configuration is the IPv4-only fallback: the enable
  // path's probe found the kernel refusing an IPv6 listening bind and wrote
  // listen=YES/listen_ipv6=NO (settled row 6). False is the dual-stack
  // default. The screen states it; nothing decides on it.
  bool ipv4_only = 8;
  // True when the LIVE configuration still carries force_local_logins_ssl=YES
  // and force_local_data_ssl=YES — read back from the file the daemon was
  // started against, taking the LAST occurrence of each key, so this is the
  // daemon's answer and not the panel's echo of its own decision.
  bool forced_tls = 9;
}
```

> **AMENDED AFTER EXECUTION — 2026-09-09 (sixth pass): this block named eight fields and the wire
> carries nine.** `forced_tls = 9` shipped on all four `Ok` messages and is recorded in the accepted
> contract baseline. Measured:
>
> ```
> $ for m in GetFtpsStatusOk ReloadFtpsTlsOk EnableFtpsOk DisableFtpsOk; do \
>     awk "/^message $m \{/,/^\}/" proto/agent/v1/ftp.proto | grep -cE '= [0-9]+;'; done
> 9
> 9
> 9
> 9
> $ grep -n forced_tls proto/agent/v1/contract-baseline.txt
> 257:field maran.agent.v1.DisableFtpsOk 9 forced_tls TYPE_BOOL …
> 278:field maran.agent.v1.EnableFtpsOk 9 forced_tls TYPE_BOOL …
> 354:field maran.agent.v1.GetFtpsStatusOk 9 forced_tls TYPE_BOOL …
> 451:field maran.agent.v1.ReloadFtpsTlsOk 9 forced_tls TYPE_BOOL …
> ```
>
> The agent fills it (`agent/crates/agent/src/services/ftps/ftps_status_fields.rs:88`,
> `forced_tls: state.forced_tls.unwrap_or_default()`) on all four responses. **The code did not
> change with this correction; this block was one field short of the contract it quotes.**
> **What this pass deliberately does NOT do:** it does not touch the Known Risks row that reserves
> the *customer- and operator-facing surface* for this field — the DTO member, the screen line and
> the three locales — to the owner. See the note appended to that row: the wire half has landed, the
> surfacing decision has not been made here, and correcting a field count is not a way to make it.

`EnableFtpsOk`, `DisableFtpsOk` and `ReloadFtpsTlsOk` carry that same nine-field list, field for
field and number for number — all four rpcs render the one `FtpsState` the ops layer returns, so
the panel needs no second call to learn what an enable, a disable or a reload just did, and the
four messages cannot drift apart without the shared mapping function in `ftps_service.rs` failing
to compile against one of them.

The file's header comment is corrected in the same edit. It currently says the retired `FtpService`
names are held for "a real FTP daemon … in its own service"; that daemon has arrived, it took
`Ftps*` names rather than the retired `Ftp*` ones so that a reader diffing the history is never
looking at two different messages under one name, and the paragraph says so.

- [x] **Step 1: Make the proto edits and run the contract gate**

Run: `source scripts/dev && maran proto`
Expected: PASS — every change is an addition. Then record it: `maran proto --accept`, **in its own
commit**, so the reviewer sees added inventory lines rather than an opaque refresh. CI never runs
`--accept`.

- [x] **Step 2: Wire the service and extend the handshake test**

`tests/handshake.rs` gains one call per new rpc asserting the service is reachable and answers a
typed error rather than `UNIMPLEMENTED`.

- [x] **Step 3: Write the polygon suite**

`agent/crates/agent/tests/ftps_on_a_real_host.rs`, every test `#[ignore]`d with a reason and every
test refusing — not skipping — when `MARAN_POLYGON` is unset.

```rust
#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_login_reaches_its_own_home_over_tls_and_a_file_it_uploads_belongs_to_the_account() {
    // The test that matters. A real vsftpd, a real TLS session, a real upload,
    // and then `stat` on the file through the account's own home.
}

#[test]
#[ignore = "polygon only"]
fn a_client_that_will_not_negotiate_tls_cannot_log_in() {
    // The owner's decision 2, observed rather than read out of the config file.
    // curl WITHOUT --ssl-reqd speaks plain FTP; the server must refuse the login.
    // Asserting `force_local_logins_ssl=YES` appears in a file would pass on a
    // daemon that never read that file.
}

#[test]
#[ignore = "edits the live config and restarts the daemon: polygon only"]
fn the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen() {
    // Added by the fourth amendment, after the parser's ordering was measured
    // the other way round. vsftpd takes the LAST occurrence of a key, so one
    // appended `force_local_logins_ssl=NO` turns forced TLS off on a config
    // that still parses, still starts and still greets with 220 — and neither
    // of Task 8's two validation layers can see it (they run before the swap
    // and ask liveness questions after it).
    //
    // Four parts, and part 2 is what makes this a check rather than a
    // decoration:
    //   1. After EnableFtps, read /etc/maran/vsftpd/vsftpd.conf — the path the
    //      daemon is started against, not a render held in memory — and assert
    //      every key it contains appears exactly once. Guard the loop's size
    //      against a baseline, or an empty key set passes loudest.
    //   2. THE POSITIVE CONTROL, which is also the witness that the risk is
    //      real on this host and not only in a container transcript: append a
    //      second `force_local_logins_ssl=NO`, restart the unit, and assert
    //      BOTH that the duplicate check now fails by name AND that curl
    //      WITHOUT --ssl-reqd is now ACCEPTED where the test above saw it
    //      refused. A duplicate check nobody has watched fail is a green gate
    //      that certifies nothing.
    //   3. The read-back's own half, on the one live-config fact the wire
    //      already carries: append the IPv4-only listen pair after the
    //      rendered dual-stack one, restart, and assert GetFtpsStatus answers
    //      ipv4_only = TRUE — the mode the daemon now obeys, because the read
    //      takes the last occurrence. A read that took the first would answer
    //      with what the panel rendered and agree with itself forever.
    //   4. Restore by re-running EnableFtps — the whole-file replace is the
    //      repair, which is the constraint demonstrating itself — and assert
    //      the plaintext login is refused again and the mode is back.
}

#[test]
#[ignore = "polygon only"]
fn a_login_cannot_leave_its_jail() {
    // `cd /etc` and `cd /home/<other account>` must both fail in a real session.
}

#[test]
#[ignore = "polygon only"]
fn an_account_that_is_not_in_the_ftps_group_cannot_authenticate_even_with_a_valid_password() {
    // Plants a system login with a password and no group membership, and asserts
    // the PAM stack refuses it. This is the assertion the installer's polygon
    // check explicitly declared UNOBSERVED.
}

#[test]
#[ignore = "polygon only"]
fn root_cannot_log_in_over_ftps_even_when_root_has_a_password() {
    // Gives root a password for the length of the test and takes it away again.
}

#[test]
#[ignore = "polygon only"]
fn suspending_the_account_refuses_its_ftps_login_and_resuming_gives_it_back() {
    // Drives SetAccountLoginsLocked and then tries the credential, both ways.
}

#[test]
#[ignore = "polygon only"]
fn the_advertised_passive_address_appears_in_the_pasv_reply() {
    // pasv_address beside listen_ipv6=YES is a documented-flaky vsftpd pairing,
    // so the NAT path is observed rather than assumed: enable with
    // pasv_address=203.0.113.7, connect with curl -v --ssl-reqd (curl decrypts
    // the control channel, so its verbose log shows the server's replies), ask
    // for a listing with a short timeout, and assert the log carries
    // `227 Entering Passive Mode (203,0,113,7` — the LISTING itself is expected
    // to fail, because the data connection goes to a TEST-NET address, and that
    // failure is not what this test is about. If vsftpd ignores pasv_address on
    // the dual-stack socket, the reply carries the real address and this goes
    // red — which is a finding, not noise.
}

#[test]
#[ignore = "polygon only"]
fn the_rendered_configuration_is_accepted_by_the_real_daemon_and_a_broken_one_is_not() {
    // The inverse control for Task 8's validator, on the real binary: the render
    // must be ACCEPTED, and a config with one unknown key must be REFUSED with
    // the live config left exactly as it was.
    //
    // This is also Task 6 Step 3, and it is the test finding F2 was owed: the
    // render takes its TLS version key spelling from THIS host's adapter, and
    // the wrong family's spelling is refused here — with a named 500 on the RHEL
    // family and with a bare exit 2 and no output on the Debian family, which is
    // why the refusal is asserted as "the daemon exited" and never as a message.
    // It therefore has to be scored on BOTH polygon images; a key that exists on
    // one family and not the other is invisible to a suite that runs on one.
}

#[test]
#[ignore = "polygon only"]
fn an_ftps_login_gets_no_ssh_and_no_sftp_session() {
    // nologin shell and no membership of the SFTP group: `ssh alice_files whoami`
    // must fail rather than run, and so must `sftp alice_files:` — the same
    // nologin mechanism, but the subsystem path is different sshd code and the
    // SFTP refusal additionally rests on the group Match block, so both are tried.
}
```

- [x] **Step 4: Prove the ignored tests fail loudly outside the polygon**

Run them with `MARAN_POLYGON` unset and confirm each refuses. Plan 3 found one of twelve unguarded
and passing quietly outside any container.

- [x] **Step 5: Add the images' packages and the baseline row**

Both Dockerfiles install vsftpd through step 89's own `install_vsftpd_package` (Task 3) and add
`curl`, which speaks FTPS with `--ssl-reqd` and plain FTP without it — the one client that can drive
both halves of the TLS test. `scripts/test-baseline.txt` gains the
`ftps_on_a_real_host [tests/ftps_on_a_real_host.rs]` row with its declared total, or
`maran polygon verify`'s fifth axis fails the job by name.

- [x] **Step 6: Mutation pass on the real host, through `maran mutate --polygon`**

| Mutation | Named polygon test that must go red |
|---|---|
| `force_local_logins_ssl=NO` in the template | `a_client_that_will_not_negotiate_tls_cannot_log_in` |
| Drop `pam_succeed_if` from the installer's PAM stack | `an_account_that_is_not_in_the_ftps_group_cannot_authenticate_even_with_a_valid_password` |
| `chroot_local_user=NO` | `a_login_cannot_leave_its_jail` |
| Create the login without `--non-unique --uid` | the upload-ownership half of the first test |
| Make the live-config read accept a duplicated key (count every key as seen once) | `the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen` — its planted-duplicate half stops failing, which is the whole check |
| Make `get_ftps_status` read the FIRST occurrence of a key instead of the last | the same test's part 3: with the second listen pair planted, the status answers the pair the panel rendered while the daemon obeys the appended one |

A mutation killed on the polygon but surviving locally means the protection has **no local test at
all** and one is owed; a mutation killed locally but surviving on the polygon means the local test
asserts a rendered string rather than the system's answer. Both directions are findings, not noise.

---

## Phase D — the ports

### Task 11: The firewall learns a port range, and FTPS is one rule an operator can see

**Files:**
- Modify: `proto/agent/v1/firewall.proto`, `proto/agent/v1/contract-baseline.txt` (this task's own `maran proto --accept`, in its own commit)
- Modify: `agent/crates/templates/src/nftables/nftables_allow.rs`, `templates/templates/nftables/ruleset.nft.j2`, `templates/tests/golden/nftables/ruleset.nft`, plus a new golden `ruleset_port_range.nft`
- Modify: `agent/crates/ops/src/firewall/` (the rule model and the allow/deny/list operations), `agent/crates/agent/src/services/firewall/`
- Modify: `backend/src/Maran.Modules/Firewall/` (entity, configuration, migration, validator, DTO, controller), `Maran.Agent.Client/Services/FirewallService/`
- Modify: `frontend/src/pages/firewall/`, `locales/{en,ru,hy}/firewall.json`
- Test: the firewall's unit, golden, integration and polygon suites

**Interfaces:**
- Consumes: the shipped `Port` validated type.
- Produces: `FirewallRule` gains `optional uint32 port_to = 4`, `AllowPortRequest` gains `optional uint32 port_to = 7`, `DenyPortRequest` gains `optional uint32 port_to = 7`; `NftablesAllow` gains `port_to: Option<u16>`; the ruleset renders `tcp dport 30000-30099 accept` for a range and is byte-identical to today for a single port.

**The three field numbers, and why they are not the same number.** This plan's own standard is that a
value which must be typed exactly right is written out, and here the obvious answer is wrong in two
messages out of three. `FirewallRule` holds `port = 1`, `protocol = 2`, `source_cidr = 3`, so `4` is
its next free number. `AllowPortRequest` and `DenyPortRequest` each open with `reserved 4; reserved
"ssh_port";` — the single-`ssh_port` field retired on 2026-09-02 — and then hold 1, 2, 3, `panel_port
= 5` and `ssh_ports = 6`, so their next free number is **7**, and an executor who copied `4` across
all three would get a protoc refusal in two of them (`"port_to" uses field number 4, which is
reserved`) rather than a wrong-but-compiling contract. The numbers are per message; nothing here
makes them agree.

```proto
// firewall.proto — three additive fields. Every field's doc comment says the same
// thing because the field means the same thing; only the number differs.

message FirewallRule {
  // … port = 1, protocol = 2, source_cidr = 3 unchanged …
  // Inclusive upper bound of a port RANGE whose lower bound is `port`. Absent
  // means the rule is the single port `port`, which is what every rule written
  // before 2026-09-08 is. Added for FTPS's passive data range: the alternative
  // is a hundred rules for one feature, in a listing an administrator reads.
  optional uint32 port_to = 4;
}

message AllowPortRequest {
  // reserved 4; reserved "ssh_port";  ← already here, which is why this is 7
  // … port = 1, protocol = 2, source_cidr = 3, panel_port = 5, ssh_ports = 6 …
  // Inclusive upper bound of the range. Absent means the single port `port`.
  // Refused when below `port`; the check is in ops, before the template.
  optional uint32 port_to = 7;
}

message DenyPortRequest {
  // reserved 4; reserved "ssh_port";  ← same history, same next free number
  // Inclusive upper bound of the range. Absent means the single port `port`.
  optional uint32 port_to = 7;
}
```

**Why a range and not a hundred rules.** FTPS needs one control port and a block of passive data
ports, one per concurrent data connection. The alternative to a range is a hundred nftables rules for
one feature, in a listing an administrator is supposed to read. And the mechanism that would avoid
the range entirely — the kernel's FTP conntrack helper, which opens passive ports as it sees them
announced — **cannot work here**: it reads the PASV reply off the control channel, and this control
channel is encrypted. That is not a limitation of this implementation; it is what FTPS means.

**Who opens the ports.** The `Firewall` module, on the administrator's instruction, exactly like
every other rule. The FTPS enable screen does not open them silently: it shows the two rules that
FTPS needs (the control port and the passive range, every number supplied by the backend from
`FtpsDefaults`/`FtpsSettings` — the SPA holds no domain constants), says whether each is already
present, and offers to create them — and if the administrator declines, FTPS still enables and the
status screen reports the daemon as listening and unreachable. Silently opening a port from a
different module's screen is the thing `rules/security.md` §10 exists to prevent.

**The mechanism behind "listening and unreachable" — and its inverse — is SPA-side composition,
stated here so no executor invents a backend seam for it.** The `Ftp` module may not read the
`Firewall` module's schema (a cross-module read the architecture tests reject), and the agent's
control-port probe is local, so it bypasses nftables and cannot answer reachability. Both facts the
answer needs are already exposed over HTTP — the FTPS status and the firewall rules list — so the
screen composes its two stores (`stores/ftp.ts` + `stores/firewall.ts`, Task 15) and derives the
two warnings itself:

- daemon answering + no matching allow rules → "listening and unreachable: the firewall has no
  rule for these ports", with the offer to create them;
- daemon **off** + the FTPS rules still present → "FTPS is disabled but TCP 21 and the passive
  range are still open", with the offer to remove them. `DisableFtps` itself never touches the
  rules — disabling a service is not editing the firewall, and the same §10 reasoning applies in
  both directions — so this warning is the only place the leftover surface becomes visible, and it
  is asserted by a Task 15 Playwright spec in both directions (shown when the rules linger, absent
  once they are removed).

- [x] **Step 1: Write the failing golden and the failing ops tests**

```rust
#[test]
fn a_rule_with_an_upper_bound_renders_one_nftables_range() {
    let rendered = NftablesRuleset { allows: vec![NftablesAllow {
        port: 30000, port_to: Some(30099), protocol: NftablesProtocol::Tcp,
        source_is_any: true, source_cidr: String::new(), family_keyword: "",
    }], /* … */ }.render_config().unwrap();
    assert!(rendered.contains("tcp dport 30000-30099 accept"));
}

#[test]
fn a_rule_with_no_upper_bound_renders_exactly_what_it_rendered_before() {
    // The additive law, at the level that matters: an old rule's rendered text
    // must not change because a new optional field exists. The committed golden
    // ruleset.nft is the assertion; this test names why it must not move.
}

#[test]
fn an_upper_bound_below_the_lower_bound_is_refused_before_it_reaches_the_template() {
    assert!(matches!(allow_port(&host, &distro, &request_range(30099, 30000)),
                     Err(FirewallError::InvalidRange)));
}
```

> **AMENDED AFTER EXECUTION — 2026-09-09 (fifth pass), two spellings only.** This task shipped on
> 2026-09-09 and nothing it produced changed. What was wrong in the snippets above: the render method
> is `render_config()`, not `render()` (the house name on every render type), and
> `FirewallError::InvalidRange` is a **unit** variant, so it is matched as `InvalidRange` and not
> `InvalidRange { .. }`, which does not compile against it. Both were corrected against
> `agent/crates/templates/src/nftables/nftables_ruleset.rs` and
> `agent/crates/ops/src/firewall/firewall_error.rs`.

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Implement across the four layers**

Template, `ops::firewall`, the agent service, and the panel's entity plus an **expand-only**
migration (`port_to integer NULL`) — nothing is dropped, renamed or narrowed, so `maran migrate
guard` passes without a `// contract-phase:` line.

- [x] **Step 4: Run, and run `maran proto` and `maran migrate guard`**

- [x] **Step 5: Extend the firewall polygon suite**

Apply a range rule and ask the kernel, with `nft list ruleset`, whether it is there — the same
question the shipped firewall suite already asks of a single port. **What would make this fail:** a
template that renders `30000-30099` as two words, which `nft -f` refuses, aborting the whole
transactional load and leaving the previous ruleset — visible here and invisible to a golden.

- [x] **Step 6: Mutation pass**

Render the range as `dport 30000 accept`, dropping the bound, and confirm the golden and the polygon
test both go red. Remove the `port_to >= port` check and confirm the refusal test dies.

---

## Phase E — the panel

### Task 12: The agent client, the capability, and a pipeline that does something

**Files:**
- Create: `backend/src/Maran.Agent.Client/Interfaces/{IAgentFtpsClient,IFtpsServiceInvoker}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/FtpsService/{AgentFtpsClient,GrpcFtpsServiceInvoker,CreateFtpsUserArguments,FtpsStatusDto}.cs` — `CreateFtpsUserArguments.cs` is named here rather than left to the executor: the type is constructed by name in Step 1's `ToString()` test, and `rules/csharp.md`'s map puts a service client's own shapes in `Services/<Proto>Service/` ("client, seam, DTOs"), so it is one file beside the client that sends it and not a nested type inside it
- Create: `backend/src/Maran.Host/Resilience/ResilientAgentFtpsClient.cs`
- Modify: `Maran.Sdk/Contracts/AgentCapability.cs` (`Ftps`), `Maran.Agent.Client/DependencyInjection.cs`, `Maran.Host/Extensions/ResilienceExtensions.cs`, `Maran.Host.Tests/Composition/ContainerResolutionTests.cs`
- Test: `backend/tests/Maran.Agent.Client.Tests/Services/FtpsService/`

> **AMENDED 2026-09-09 (sixth pass) — `CreatedFtpsUserDto.cs` was a file with nothing to hold, and
> it is struck from this list and from the "Panel — `backend/src/`" table.** Measured three ways.
> `CreateFtpsUserOk` carries exactly one field —
> `awk '/^message CreateFtpsUserOk \{/,/^\}/' proto/agent/v1/ftp.proto` prints
> `string ftps_username = 1;` and nothing else. The shipped SFTP twin answers a single value rather
> than a DTO: `grep -n 'CreateAsync' backend/src/Maran.Agent.Client/Interfaces/IAgentSftpClient.cs`
> shows `Task<Result<string>> CreateAsync(…)`, and `ls
> backend/src/Maran.Agent.Client/Services/SftpService/` holds only `AgentSftpClient.cs` and
> `GrpcSftpServiceInvoker.cs` — no DTO file at all. A one-property record wrapping one string would
> be a type whose only effect is a name. **Nothing about this task's behaviour changes**: the client
> still answers the created login name, and `CreateFtpsUserArguments` — the carrier that keeps the
> password out of a generated `ToString()` — stays, because that one earns its file.

**Interfaces:**
- Consumes: `AgentErrorTranslator` — the single wire-error boundary. No second path around it.
- Produces: `IAgentFtpsClient` with `EnableAsync`, `DisableAsync`, `GetStatusAsync`, `ReloadTlsAsync`, `CreateUserAsync`, `SetPasswordAsync`, `DeleteUserAsync`, resolved already wrapped in its resilience pipeline.

**The capability is not optional.** `AgentCapabilityGuard` derives the required capability from the
client interface name, so `IAgentFtpsClient` without an `AgentCapability.Ftps` value throws at
composition rather than being waved through as the one part of the agent nobody has to declare. The
`Ftp` module's manifest declares `[AgentCapability.Ftps]` and nothing else — in particular **not**
`AgentCapability.Firewall`, because the module does not open ports; it tells the administrator which
ones the `Firewall` module would have to open.

**The password must not leak through a generated `ToString()`.** A C# `record` writes every property
into its `ToString()`, so a request record carrying the generated password leaks it the first time
anything interpolates it into a log line. The carrier type here is
`CreateFtpsUserArguments(string AccountUsername, string FtpsUsername, SensitiveString Password)` in
`Services/FtpsService/CreateFtpsUserArguments.cs` — one file, one type, as everything else here is —
and the password member is the shipped `SensitiveString` rather than a `string`, exactly as
`AgentSftpClient.CreateAsync` already takes one. That is what makes the redaction a property of the
TYPE and not of this record's discipline: `SensitiveString.ToString()` returns `[redacted]`, so the
`record`'s compiler-generated `ToString()` prints `Password = [redacted]` with nothing overridden,
and a future member added to this carrier inherits the same protection only if it is wrapped the same
way. A test proves it, and `AgentErrorTranslator`'s redaction is verified against a
value the panel actually sent — the realistic leak is the agent quoting the credential back in an
error, not a "using password: YES" line.

- [x] **Step 1: Write the failing tests, driving the production entry point**

> **CORRECTED 2026-09-09 (sixth pass) — the three `CreateUserAsync` calls below took a bare `string`
> password while this Step's own prose declares the parameter as `SensitiveString`.** A contradiction
> inside one Step, and the wrapped type is the correct half: it is the whole of the redaction argument
> three paragraphs above, and it is what the shipped twin takes
> (`grep -n 'SensitiveString' backend/src/Maran.Agent.Client/Interfaces/IAgentSftpClient.cs` →
> `SensitiveString password` on both `CreateAsync` and `SetPasswordAsync`). Left as written, the
> samples do not compile and an executor reading only the code would have widened the parameter back
> to `string`, taking the redaction with it. The calls now pass `CreateFtpsUserArguments`, which is
> also the shape the entry point took (`grep -n -A2 'CreateUserAsync' …/Interfaces/IAgentFtpsClient.cs`
> → `CreateUserAsync(CreateFtpsUserArguments arguments, CancellationToken cancellationToken)`).
> No requirement changed; the sample now says what the prose beside it already said.

```csharp
[Fact]
public async Task Create_sends_the_account_the_login_and_the_password()
{
    var stub = new StubFtpsService();
    var client = new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance);

    await client.CreateUserAsync(
        new CreateFtpsUserArguments("alice", "files", new SensitiveString("generated")),
        CancellationToken.None);

    Assert.Equal("alice", stub.LastCreateRequest!.AccountUsername);
    Assert.Equal("files", stub.LastCreateRequest.FtpsUsername);
    Assert.Equal("generated", stub.LastCreateRequest.Password);
}

[Fact]
public async Task Enable_sends_every_field_of_the_configuration()
{
    // Sixteen surviving mutations in plan 3 were request mapping that no test read.
    // Every field, asserted.
}

[Fact]
public async Task The_generated_password_is_never_written_to_the_log_even_when_the_agent_quotes_it_back()
{
    var recorder = new RecordingLogger<AgentFtpsClient>();
    var stub = StubFtpsService.FailingWith(ErrorCode.SystemFailure, "chpasswd: 'alice_files:generated' rejected");
    var client = new AgentFtpsClient(stub, recorder);

    await client.CreateUserAsync(
        new CreateFtpsUserArguments("alice", "files", new SensitiveString("generated")),
        CancellationToken.None);

    Assert.DoesNotContain("generated", recorder.Text);
}

[Fact]
public void The_request_carrier_does_not_print_the_password_in_its_generated_string()
{
    var carrier = new CreateFtpsUserArguments("alice", "files", new SensitiveString("generated"));
    Assert.DoesNotContain("generated", carrier.ToString());
    Assert.Contains("[redacted]", carrier.ToString());
}
```

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Implement the client, the invoker and the decorator**

- [x] **Step 4: Add it to `ContainerResolutionTests` and assert the pipeline *does* something**

Not that it is wired — what it does. Plan 3 found a `DeleteAsync` bypassing its pipeline entirely,
with no timeout, passing 59 of 59 tests. The assertion drives a call through the resolved client
against a stub that never answers and requires the pipeline's timeout to fire.

- [x] **Step 5: Mutation pass**

Every field of every request, dropped and swapped; the decorator bypassed; the redaction removed.

---

### Task 13: The `Ftp` module — the server-level switch, and the certificate it depends on

**Files:**
- Create: `backend/src/Maran.Modules/Ftp/` — `Maran.Modules.Ftp.csproj` and `GlobalUsings.cs` (the skeleton holds folders and `.gitkeep`s only; there is no project file to inherit), `FtpModule.cs`, `FtpManifest.cs`, `Domain/Entities/FtpsSettings.cs`, `Domain/Policies/FtpsDefaults.cs`, `Persistence/{FtpDbContext,DesignTimeDbContextFactory,DesignTimeCurrentUser}.cs`, `Persistence/Configurations/FtpsSettingsConfiguration.cs`, the initial migration, `Commands/{EnableFtps,DisableFtps}/`, `Queries/GetFtpsStatus/`, `Controllers/FtpsServerController.cs`, `Services/FtpAuditJournal.cs`, `Resources/ErrorMessages{,.ru,.hy}.resx`
- Create: `backend/src/Maran.Sdk/Events/CertificateInstalled.cs`
- Create: `backend/src/Maran.Modules/Ftp/IntegrationEvents/Handlers/CertificateInstalledHandler.cs`
- Modify: `backend/src/Maran.Modules/Ssl/` (publish `CertificateInstalled` where material is installed and where a renewal lands), `Maran.Host/Modules/ModuleRegistry.cs`, `Maran.sln`, `Maran.Host.csproj`, `Maran.ArchitectureTests.csproj`, `Maran.Sdk/Contracts/AuditActions.cs`
- Test: `backend/tests/Maran.Modules.Ftp.Tests/` — including `TestSupport/ErrorMessageValues.cs`, the one file behind the locale sweep below — and `backend/tests/Maran.Host.IntegrationTests/` — there is NO per-module `*.IntegrationTests` project shape anywhere under `backend/tests/`, and inventing one here would fork the testing map; the HTTP-surface tests land beside the other `*EndpointTests` in the Host integration project

> **AMENDED 2026-09-09 (fifth pass): two skeleton claims in this task were checked against the tree
> and are false. Neither is a design change; both are work this task has to do and was not told to.**
>
> **`backend/tests/Maran.Modules.Ftp.Tests/` is not a project.** This line called it "the shipped
> unit-test skeleton project"; it is a folder holding one zero-byte `.gitkeep`, with no `.csproj` in
> it and no entry in `Maran.sln`. Every shipped module's test project is a real one
> (`backend/tests/Maran.Modules.Sftp.Tests/Maran.Modules.Sftp.Tests.csproj`, registered in the
> solution beside its module under a solution folder of the module's name). So this task's **Create**
> list also holds `backend/tests/Maran.Modules.Ftp.Tests/Maran.Modules.Ftp.Tests.csproj`, and its
> **Modify** of `Maran.sln` adds BOTH projects — the module and its tests — under one `Ftp` solution
> folder, exactly as the `Sftp` pair is registered. This matters beyond tidiness: `maran test` and
> `dotnet test Maran.sln` measure what the solution contains, so a test project that is written and
> never registered is a suite that silently never runs, which `rules/testing.md` calls the most
> believable false pass there is. Task 14 writes into the same project and inherits this.
>
> **Four of the folders this task and Task 14 file into do not exist in the skeleton.**
> `backend/src/Maran.Modules/Ftp/` holds `Authorization/`, `Commands/`, `Domain/{Events,Interfaces}/`,
> `Errors/`, `IntegrationEvents/{Events,Handlers}/`, `Jobs/`, `Persistence/{Configurations,
> Interceptors,Migrations}/`, `Queries/`, `Resources/`, `Seeders/` and `Services/` — and **not**
> `Domain/Entities/`, `Domain/Policies/`, `Controllers/` or `Common/`, all four of which exist under
> the shipped modules and all four of which this plan places files in (`Domain/Entities/FtpsSettings.cs`
> and `Domain/Policies/FtpsDefaults.cs` here, `Controllers/` here and in Task 14, `Common/` in Task
> 14). They are created by the task that lands their first file, in the place the map already assigns
> — `rules/architecture.md`'s skeleton policy says an empty folder is held by a `.gitkeep` and loses
> it on its first real file, which is the opposite direction and does not apply to a folder that was
> never held at all. Nothing here invents a location; the point of writing it down is that "the
> skeleton holds folders only" reads as *all* the folders, and an executor who trusts it will file
> `FtpsDefaults.cs` somewhere else.

**Interfaces:**
- Consumes: `IAgentFtpsClient`, `ISiteDirectory` (the Sdk window the Sites module already implements, used to check that the chosen hostname is a site this panel serves), `AuthorizationPolicies`.
- Produces:
  - `FtpsDefaults` (`Domain/Policies/`) — settled row 3, landed as code: `PassivePortMin = 30000`, `PassivePortMax = 30099`, `MaxClients = 100`, `ControlPort = 21`, each with the settled reasoning as its doc comment (a hundred ports for a hundred clients, so the range can never be what runs out first; conservative, movable later without touching the shape). This is the ONLY place these numbers exist in the panel or the SPA; the agent receives them on the wire and owns no default.
  - `FtpsSettings` — one row per installation: `Hostname`, `Enabled`, `PassivePortMin`, `PassivePortMax`, `PassiveAddress`, `MaxClients`, `Ipv4Only`. Seeded from `FtpsDefaults` on the first enable — never from the caller — and `Ipv4Only` is written from the enable response's state (settled row 6), so the panel remembers the mode the host chose. Not tenant-scoped and carries no `AccountId`, so it is named in `TenantScopeTests`' exemption list with its reason.
  - `EnableFtpsCommand(string Hostname, string? PassiveAddress)` — the hostname and the one optional operator-typed value, and NOTHING else: the range and the client ceiling are not the caller's to send, which is what keeps a domain number out of the SPA (`rules/vue.md`: a constant holding a domain value belongs on the server) and out of the operator's hands alike.
  - `POST /api/v1/ftps-server/enable`, `POST /api/v1/ftps-server/disable`, `GET /api/v1/ftps-server` — all `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]` (the shipped constant is `AdminOnly`; there is no `AdministratorOnly` member).
  - `FtpManifest` — `LicenceTier.Included` (settled row 5: FTPS is table-stakes compatibility, not a value-add) and `AgentCapabilities: [AgentCapability.Ftps]`, exactly as `SftpManifest` declares its tier.
  - The `GetFtpsStatus` query's `FtpsStatusDto` — the agent's **nine** status facts passed through (running, control-port answer, certificate presence and placeholder-ness, the paths for the operator, the range, `Ipv4Only`, `ForcedTls`), plus `ControlPort` from `FtpsDefaults` **and `Hostname` from the persisted `FtpsSettings` row**, so every value a screen renders is backend-owned. **`ForcedTls` and the count `nine` are the sixth pass's correction: this list said "eight" and stopped at `Ipv4Only`, while the wire carries `forced_tls = 9` on all four `Ok` messages** (`awk '/^message GetFtpsStatusOk \{/,/^\}/' proto/agent/v1/ftp.proto | grep -cE '= [0-9]+;'` → `9`; `grep -n forced_tls proto/agent/v1/contract-baseline.txt` → four rows). Carrying it through the DTO is passing through what the agent already answers, and it is what stops the panel reporting a green daemon that has stopped forcing TLS. **Whether a SCREEN states it is the owner's, not this plan's** — the Known Risks row "A root-level edit of the live config can switch forced TLS off" reserves that, and Task 15's copy is unchanged by this pass. **`Hostname` was added to this list by the fifth pass, and its absence was a real gap, not a wording one:** Task 15's credential dialog is specified to render `ftpsStatus.hostname` — the owner's decision 1 turns on it, because a customer told to connect to their own domain gets a certificate-name mismatch — and the wire carries the hostname only as a REQUEST field, never back in the answer. Nothing but this DTO could have supplied it, and nothing here supplied it. It is nullable, because FTPS may never have been enabled; the screen renders the absence as absence and does not compose a host name of its own.
  - `CertificateInstalled(string Domain)` in the Sdk, published by `Ssl`, handled here.

**What the enable command does, in order.** Validate the hostname as a domain. Ask `ISiteDirectory`
whether the panel serves a site for it, and refuse with a typed error if not — because without one
there is no path by which a real certificate can ever be issued for that name, and the agent will
refuse for the same reason one step later. Compose the agent request from the command's hostname
and passive address plus the range and ceiling from `FtpsDefaults` (or the persisted `FtpsSettings`
row, which was itself seeded from them), and call `EnableAsync`. On `CertificateMissing`, translate
to an operator-facing error code whose resx text names the SSL module as the fix. On success,
persist the settings — including `Ipv4Only` from the returned state — write the audit entry, and
return the status: whether the certificate is the self-signed placeholder decides whether the
operator's customers will see a warning, and the mode line tells an operator on an IPv6-disabled
host what actually got bound.

**Why the certificate event and not a poll.** vsftpd reads its certificate once, at start. A daily
reconcile would leave the daemon serving superseded material for up to a day after a renewal, and —
worse — a reconcile that restarts on a schedule restarts a working daemon for no reason. The Ssl
module already knows the exact moment material lands, for both a manual install and an automatic
renewal, so it says so: `CertificateInstalled` is a Wolverine message, the `Ftp` module handles it,
compares the domain with `FtpsSettings.Hostname`, and calls `ReloadTlsAsync` only on a match. A
module that is not loaded handles nothing and nothing breaks — which is why the message is a
notification and never a request for an answer.

**One spelling of the resource class, and one named file behind the locale sweep.** This module's
error text lives in `Resources/ErrorMessages{,.ru,.hy}.resx`, and the class the code names is
`ErrorMessages` — the strongly-typed class the csproj generates from the neutral file, exactly as
every shipped module does it (`<StronglyTypedClassName>ErrorMessages</StronglyTypedClassName>`). There
is no `FtpErrorMessages`: a module-prefixed second spelling would be a synonym for the same resx
family, and a code asserted as `nameof(ErrorMessages.X)` in one test and swept through a differently
named class in another is two names for one thing that can drift apart without either test noticing.

What the generated class cannot do is answer "give me every value in every locale", which is the
sweep's actual question: it resolves ONE culture at a time through a `ResourceManager`, and a
`ResourceManager` in a unit-test process falls back to the neutral text when a satellite assembly is
absent — so a path pasted into `ErrorMessages.ru.resx` would be swept as its English fallback and
pass. So the sweep reads the three `.resx` XML files directly, which is also how the shipped
`ResourceKeyParityTests` reads them ("it reads the `.resx` files as the source of truth rather than
the compiled satellite assemblies, so drift is caught in the file the author edited"). That reader is
`backend/tests/Maran.Modules.Ftp.Tests/TestSupport/ErrorMessageValues.cs`:
`ByLocale()` locates the module's `Resources/` folder from the test assembly's location, loads
`ErrorMessages.resx`, `ErrorMessages.ru.resx` and `ErrorMessages.hy.resx` with `XDocument`, and
returns three collections of the `<data>` elements' `<value>` text — three, never fewer, because a
missing file is a failed load and not an empty locale, and an empty locale is what the `NotEmpty`
guard in the test exists to refuse.

- [x] **Step 1: Write the failing handler tests**

```csharp
[Fact]
public async Task Enabling_ftps_for_a_hostname_the_panel_does_not_serve_is_refused_with_the_named_code()
{
    // `Error` is (Code, Type) and DELIBERATELY has no Message — the sentence a
    // customer reads lives in the resx, in three languages — so a refusal is
    // asserted as its code and its kind, never as text. The no-paths guarantee
    // is asserted where the text actually lives: the resx test below.
    var result = await handler.Handle(new EnableFtpsCommand("ftp.unknown.test", null), default);
    Assert.True(result.IsFailure);
    Assert.Equal(nameof(ErrorMessages.FtpsHostnameNotServed), result.Error.Code);
    Assert.Equal(ErrorType.Validation, result.Error.Type);
}

[Fact]
public void No_error_message_in_any_locale_carries_a_filesystem_path()
{
    // The string a customer sees is a resx value rendered by culture, so this —
    // not a member of Error — is where "no paths in a customer-facing message"
    // is observable (rules/security.md role-aware errors). All three locales,
    // because a path pasted into the Russian translation alone would pass an
    // English-only sweep. The per-locale NotEmpty is the vacuity guard on the
    // axis that can go blind: a sweep over zero entries proves nothing.
    foreach (var entries in ErrorMessageValues.ByLocale()) // en, ru, hy — read from the three resx files
    {
        Assert.NotEmpty(entries);
        Assert.All(entries, value =>
        {
            Assert.DoesNotContain("/etc/", value);
            Assert.DoesNotContain("/var/", value);
        });
    }
}

[Fact]
public async Task The_agent_receives_the_range_and_ceiling_from_FtpsDefaults_and_never_from_the_caller()
{
    // Settled row 3's flow: the command carries no numbers, so the only way
    // these can reach the wire is the backend's own named constants.
    await handler.Handle(new EnableFtpsCommand("ftp.example.test", null), default);
    Assert.Equal(FtpsDefaults.PassivePortMin, agent.LastEnableRequest!.PassivePortMin);
    Assert.Equal(FtpsDefaults.PassivePortMax, agent.LastEnableRequest.PassivePortMax);
    Assert.Equal(FtpsDefaults.MaxClients, agent.LastEnableRequest.MaxClients);
}

[Fact]
public async Task The_listen_mode_the_host_chose_is_persisted_from_the_enable_response()
{
    // Settled row 6: the host decides, the panel reports — and remembers.
    agent.NextEnableAnswersIpv4Only();
    await handler.Handle(new EnableFtpsCommand("ftp.example.test", null), default);
    var row = await context.FtpsSettings.SingleAsync();
    Assert.True(row.Ipv4Only);
}

[Fact]
public async Task Enabling_ftps_records_an_audit_entry_naming_the_hostname()
{
    await handler.Handle(new EnableFtpsCommand("ftp.example.test", null), default);
    Assert.Contains(journal.Entries, e => e.Action == AuditActions.FtpsEnabled && e.Target == "ftp.example.test");
}

[Fact]
public async Task A_certificate_installed_for_the_ftps_hostname_restarts_the_daemon()
{
    await certificateHandler.Handle(new CertificateInstalled("ftp.example.test"), default);
    Assert.Equal(1, agent.ReloadTlsCalls);
}

[Fact]
public async Task A_certificate_installed_for_any_other_domain_does_not_touch_the_daemon()
{
    // The control that stops this handler becoming "restart vsftpd on every
    // renewal on the box", which on a busy server is a restart a day.
    await certificateHandler.Handle(new CertificateInstalled("shop.example.test"), default);
    Assert.Equal(0, agent.ReloadTlsCalls);
}
```

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Implement the module, its schema and its migration**

`maran module Ftp` is NOT run — the skeleton folder already exists with its `.gitkeep`s, and the
first real file in each folder deletes the `.gitkeep` beside it. Schema `ftp`, per the one-schema-per-
module rule.

- [x] **Step 4: Run, then run the architecture suite**

Run: `source scripts/dev && dotnet test Maran.sln`
`Maran.ArchitectureTests` must show the new module referencing only `Maran.Sdk` and
`Maran.SharedKernel`, declaring its agent capability, and declaring no handler in the Host.

- [x] **Step 5: Mutation pass**

Remove the domain comparison from `CertificateInstalledHandler` and confirm
`A_certificate_installed_for_any_other_domain_does_not_touch_the_daemon` dies. Remove the audit write
and confirm its test dies. Hardcode `30000` in the handler instead of reading `FtpsDefaults` and
confirm the defaults test SURVIVES — then change `FtpsDefaults.PassivePortMin` in the mutation
instead, and confirm the defaults test dies: the pair proves the test reads the constant through
the same symbol the handler does, not a copied literal. Paste `/etc/maran/certificates` into one
`ErrorMessages.ru.resx` value and confirm `No_error_message_in_any_locale_carries_a_filesystem_path`
dies — that is the positive control proving the sweep can see.

---

### Task 14: The `Ftp` module — logins, limits, isolation, and the attestation the account lifecycle reports

**Files:**
- Create: `backend/src/Maran.Modules/Ftp/Domain/Entities/FtpUser.cs`, `Persistence/Configurations/FtpUserConfiguration.cs`, a migration, `Commands/{CreateFtpUser,ResetFtpUserPassword,DeleteFtpUser}/`, `Queries/{ListFtpUsers,GetFtpUser}/`, `Common/{FtpUserDto,CreatedFtpUserDto,FtpUserPasswordDto}.cs`, `Controllers/FtpUsersController.cs`, `IntegrationEvents/Handlers/{AccountDeletingHandler,AccountSuspendingHandler,AccountResumingHandler}.cs`
- Modify: `backend/src/Maran.Modules/Accounts/Domain/Entities/Plan.cs` (+`MaxFtpUsers` — the entity lives in `Domain/Entities/`, not `Domain/`), its EF configuration, `Seeders/PlanSeeder.cs` and a migration. The migration re-introduces a column name this schema has held before — `20260901230348_RenamePlanMaxFtpUsersToMaxSftpUsers` renamed the old `MaxFtpUsers` away — which is legal-additive, and the migration carries one comment saying the new column's meaning (FTPS logins) is not the old one's (what became the SFTP allowance), so nobody reads the history as a revert.
- Modify: `backend/src/Maran.Sdk/Contracts/AccountSnapshot.cs` — the limit check reads the snapshot, not the entity, so the Sdk record gains `MaxFtpUsers` beside `MaxSites`/`MaxDatabases`/`MaxSftpUsers`/`MaxCronEntries`/`MaxPhpWorkersPerPool`/`DiskQuotaMb`; `Maran.Modules/Accounts/Services/AccountDirectory.cs` (the owning-side implementation fills it — **at TWO sites, not one: `AccountDirectory.cs` builds the snapshot in two separate projection lambdas, and a limit threaded through one of them and defaulted in the other is exactly the half-wiring this bullet forbids. `grep -rn 'new AccountSnapshot' backend/src` names them both**) and `backend/tests/Maran.Modules.Accounts.Tests/Services/AccountDirectoryTests.cs`. The record is positional, so EVERY construction site gains the argument — 28 test files across the Backups, Cron, Databases, Monitoring, Sftp, Sites and Ssl test projects construct it today, and `dotnet build` names each one; none may be silenced with a default value, because a defaulted limit is a limit nobody decided. **Do not trust either count from this page: re-run `grep -rn 'new AccountSnapshot' backend/` yourself. `dotnet build` is the authority, and it names what it names.**
- Modify: `Maran.Modules/Sftp/Common/SftpUserDto.cs` (+ the protocol label); `Maran.Sdk/Contracts/AuditActions.cs`
- **The suspension attestation's panel half — the READ side of the two wire fields Task 10 adds.** Create `backend/src/Maran.Agent.Client/Services/AccountsService/LoginTransferProtocol.cs`; rename `backend/src/Maran.Agent.Client/Services/AccountsService/SftpLoginSuspensionFactDto.cs` to `FileTransferLoginSuspensionFactDto.cs` with the type in it; modify `backend/src/Maran.Agent.Client/Services/AccountsService/{AccountSuspensionStateDto,AgentAccountsClient}.cs`, `backend/src/Maran.Modules/Accounts/Commands/SuspendAccount/SuspendAccountCommandHandler.cs` and `backend/src/Maran.Modules/Accounts/Commands/ReactivateAccount/ReactivateAccountCommandHandler.cs`. `AccountSuspensionStateDto` is a positional record, so its new `UnmanagedLogins` parameter is named at **18 construction sites** — one production (`AgentAccountsClient.cs:112`) and 17 across four test files (`Maran.Modules.Accounts.Tests/TestSupport/{UnlockedAgentAccountsClient,FixedObservationAgentAccountsClient,RecordingAgentAccountsClient}.cs` and `Commands/SuspendAccount/AccountSuspensionTests.cs`) — and `dotnet build` names every one of them, exactly as `AccountSnapshot`'s new member does above. None may be silenced with a default, for the same reason. **Sixth pass: `18` and `17` were measured before this task began. They are the count of sites the task must VISIT, not a number to check a finished tree against — executing this task is itself what moves them, and a re-measure taken mid-execution reads higher for that reason and is not a correction. `grep -rn 'new AccountSuspensionStateDto' backend/` and `dotnet build` are the authority; a count in a plan is a starting point.**
- **Verified NOT modified, and said so because a reader will look:** `Maran.Agent.Client/Services/AccountsService/GrpcAccountsServiceInvoker.cs`, `Interfaces/IAccountsServiceInvoker.cs`, `Interfaces/IAgentAccountsClient.cs` and `Maran.Host/Resilience/ResilientAgentAccountsClient.cs`. All four are typed in `GetAccountSuspensionStateResponse` and `Result<AccountSuspensionStateDto>`, and neither of those NAMES changes — a message gaining a field and a positional record gaining a parameter leave every one of these four signatures byte-identical. The renamed DTO appears in none of them.
- Test: `backend/tests/Maran.Modules.Ftp.Tests/` (unit), `backend/tests/Maran.Host.IntegrationTests/FtpAuthorizationTests.cs`, and the attestation's own five files — `backend/tests/Maran.Agent.Client.Tests/Services/AccountsService/AgentAccountsClientSuspensionStateTests.cs` (the mapping) and `backend/tests/Maran.Modules.Accounts.Tests/Commands/SuspendAccount/AccountSuspensionTests.cs` plus the three `TestSupport` doubles named above (the sentence) — the reflection-driven IDOR fixture lives in the Host integration project (prior art: `SftpAuthorizationTests.cs` beside it), and there is no per-module `*.IntegrationTests` project shape to put it anywhere else

**Interfaces:**
- Consumes: `IAgentFtpsClient`, `IAccountDirectory`, `FtpsSettings`.
- Produces: `FtpUser { Id, AccountId, Username, CreatedAt }` — **no password column of any kind**, plaintext or hashed; `GET/POST /api/v1/ftp-users`, `POST /api/v1/ftp-users/{id}/password`, `DELETE /api/v1/ftp-users/{id}`, all `AnyAuthenticated` and all account-scoped; `FtpUserDto` carrying `Protocol = "Ftps"` so the merged screen renders a backend-owned label.

**The three lifecycle handlers, and what each owes.**

- `AccountDeletingHandler` deletes this module's rows. The host-side teardown — logins, mount unit,
  jail — is the agent's `AccountOperations::delete` cascade from Task 9, not a loop here: a list the panel
  remembers can only describe the logins it created, and a login it has forgotten is exactly the one
  still letting a deleted customer's name back in. A failure aborts the deletion.
- `AccountSuspendingHandler` and `AccountResumingHandler` do **nothing new**: `SetAccountLoginsLocked`
  already covers both protocols after Task 5, and the `Sftp` module already drives it. Two modules
  both calling it would lock twice and race on the resume. This is written down here because "the
  handler is missing" and "the handler is deliberately absent" look identical in a folder listing,
  and the module's `README`-less anatomy has no other place to say it.

**The limit and the name collision.** `MaxFtpUsers` is checked in the panel before the agent is
called. The login namespace is shared with SFTP and is owned by the host: `useradd` refuses a
duplicate, the agent returns `ALREADY_EXISTS`, and this module maps that to a validation error
saying the name is taken. It does **not** query the `Sftp` module to find out — that would be a
cross-module read of another module's schema, which the architecture tests reject, and it would still
be wrong, because the authority on whether a system login exists is the system.

**The suspension attestation, finished — and why its panel half is this task's and not Task 10's or
Task 12's.** Task 10 puts two fields on the wire: `TransferProtocol protocol = 3` on
`SftpLoginSuspensionFact` and `uint32 unmanaged_logins = 9` on `GetAccountSuspensionStateOk`, and has
`accounts_service.rs` set them. A field that is written and never read is the same blind gate Task 10
condemns one layer down, so the reader lands here, in the SAME task, for three reasons. First, this
is the only task that already opens the `Accounts` module (`Plan.cs`, `AccountDirectory.cs`, the
seeder and the migration), and the attestation is an account-lifecycle sentence — this task's
declared subject, down to the paragraph above that explains why `AccountSuspendingHandler` does
nothing. Second, Task 12 owns `Maran.Agent.Client` but only the FTPS service folder; splitting the
DTO into 12 and its reader into 14 would leave a real interval — a reviewable commit, a runnable
build — in which the panel carries a field nobody reads, which is the defect being closed rather than
a step towards closing it. Third, the change is one compiler-checked rename plus two members, and it
is finished in one place or it is finished nowhere.

What lands:

- `LoginTransferProtocol { Unspecified, Sftp, Ftps }` in `Services/AccountsService/`, mapped from the
  wire **by number and never by name**, exactly as the sibling `AccountLoginPasswordState` already is
  (`Enum.IsDefined(...) ? (LoginTransferProtocol)(int)wire : LoginTransferProtocol.Unspecified`), so a
  protocol a newer agent knows and this build does not reads as `Unspecified` instead of as whatever
  the cast produced. `Unspecified` is rendered as SFTP by every caller here, and that is not a
  fallback chosen for convenience: before this plan every fact in the list WAS an SFTP login by
  construction, so an old agent's absent value has exactly one correct reading and the proto comment
  says so. It puts a third, unknown, protocol from a FUTURE agent in the sftp column too, and that is
  accepted with its bound rather than papered over: the `GetAgentInfo` handshake covers one release of
  skew (`rules/proto.md`), the count is a breakdown and never a lock decision — the `Locked` flag is
  read per login and is not derived from the protocol — and the alternative, a fourth "unknown"
  bucket in an operator's sentence, would be a column that reads as a defect on every correctly
  skewed pair.
- `SftpLoginSuspensionFactDto` becomes `FileTransferLoginSuspensionFactDto(string Username, bool
  Locked, LoginTransferProtocol Protocol)`, in a file renamed with it. The rename is not tidying: the
  list now carries FTPS logins, and a type whose NAME says otherwise is the same defect as a doc
  comment that describes what the code does not do — the next reader stops looking. Nothing on the
  wire is renamed (`sftp_logins` keeps its number and its name, as the additive law requires); this
  is the panel's own spelling of what it received, which is what the DTO layer is for.
- `AccountSuspensionStateDto` gains `uint UnmanagedLogins` and its `SftpLogins` member becomes
  `FileTransferLogins`, with the doc comment saying what the count is: passwd entries sharing the
  account's uid whose home is NEITHER jail, which this panel did not create and does not lock.
- `SuspendAccountCommandHandler` and `ReactivateAccountCommandHandler` read both.

**Unmanaged logins are REPORTED and never refused on**, and the precedent is in the same method:
`Unobserved` already refuses on a serving vhost and an unsuspended cron entry but deliberately does
not refuse on `CronForeignLines`, because refusing would make an account with one hand-written
crontab line permanently unsuspendable while doing nothing about the line. An unmanaged login is the
same shape — the panel cannot lock what it did not create — so it joins the attestation's
"NOT covered" clause rather than its refusal list.

**The corrected attestation sentence.** `SuspendAccountCommandHandler.DescribeAttestation` today ends
`$"and all {state.SftpLogins.Count} of its sftp logins locked; NOT covered by this suspension: …"`,
which becomes false the day the first FTPS login exists — the operator would be told a customer's
logins were "sftp logins locked" while that customer held a working FTPS credential. It becomes:

```csharp
var unmanaged = state.UnmanagedLogins == 0
    ? string.Empty
    : $", and {state.UnmanagedLogins} login(s) sharing this account's uid whose home is neither "
        + "jail, which this panel did not create and cannot lock";

return $"the host shows the login locked, all {state.Sites.Count} of its vhosts for this account "
    + $"serving the suspended page, all {state.CronEntriesTotal} of its cron entries suppressed "
    + $"and all {state.FileTransferLogins.Count} of its file-transfer logins locked "
    + $"({Count(state, LoginTransferProtocol.Sftp)} sftp, {Count(state, LoginTransferProtocol.Ftps)} ftps); "
    + $"NOT covered by this suspension: the account's databases and the panel's own web "
    + $"login{foreign}{unmanaged}";
```

where `Count` is a private static counting one protocol out of `state.FileTransferLogins`, reading
`Unspecified` as `Sftp` for the reason above. `ReactivateAccountCommandHandler.DescribeAttestation`
takes the same treatment in its own direction (`… of its file-transfer logins unlocked (n sftp,
n ftps)`), and it does NOT gain the unmanaged clause: an unmanaged login was never locked by the
suspension, so a resumption has nothing to say about it that would be true.

**And the two per-login lines, which are the ones an operator acts on.** `Unobserved`'s
`"these sftp logins still authenticate: acme_web"` becomes `"these file-transfer logins still
authenticate: acme_web (sftp), acme_files (ftps)"`, and `StillStopped`'s `"these sftp logins are
still locked: …"` the same way — the protocol beside each name, because the operator's next action
differs by daemon and a bare login name does not tell them which one to look at.

**One correction to the finding this closes, stated rather than silently done.** The review asked for
`CanStillAuthenticate`'s reasoning to speak of file-transfer logins generally. Read against the tree,
that method answers only about the account's OWN passwd entry — it switches on
`state.LoginPasswordState` and touches no login list; the SFTP-specific text is in `Unobserved`'s
enumeration, which is what the paragraph above corrects. So `CanStillAuthenticate` gains one sentence
of doc saying what it does NOT answer ("the account's own entry only; the file-transfer logins are
answered separately by the enumeration below, which covers both protocols"), and its logic is
untouched. Rewriting it to mention protocols would have made the comment misdescribe the code in the
other direction.

- [x] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Creating_a_login_beyond_the_plans_limit_is_refused_before_the_agent_is_called()
{
    Assert.True(result.IsFailure);
    Assert.Equal(0, agent.CreateUserCalls);
}

[Fact]
public async Task The_generated_password_is_returned_once_and_stored_nowhere()
{
    var created = await handler.Handle(new CreateFtpUserCommand(accountId, "files"), default);
    Assert.False(string.IsNullOrEmpty(created.Value.Password));

    var row = await context.FtpUsers.SingleAsync();
    var json = JsonSerializer.Serialize(row);
    Assert.DoesNotContain(created.Value.Password, json);
    // And on the axis a serialization probe cannot see: the entity has no
    // password property at all, which a reflection assertion states outright.
    Assert.DoesNotContain(typeof(FtpUser).GetProperties(), p => p.Name.Contains("Password"));
}

[Fact]
public async Task Another_accounts_login_is_not_found_rather_than_forbidden()
{
    var response = await customerA.DeleteAsync($"/api/v1/ftp-users/{customerBsLoginId}");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task A_login_name_the_host_already_holds_is_reported_as_taken_not_as_a_server_failure()
{
    agent.FailNextWith(AgentErrorCode.AlreadyExists);
    var result = await handler.Handle(new CreateFtpUserCommand(accountId, "web"), default);
    Assert.Equal(ErrorType.Conflict, result.Error.Type);
}
```

And the attestation's four, which are the panel-side twins of the positive controls Task 5 wrote on
the agent side — in `AgentAccountsClientSuspensionStateTests.cs` for the mapping and
`AccountSuspensionTests.cs` for the sentence:

```csharp
[Fact]
public async Task The_protocol_and_the_unmanaged_count_are_carried_off_the_wire_into_the_state()
{
    // The mapping half. Without this, `protocol` and `unmanaged_logins` are set
    // by the agent and dropped on the floor one layer higher — which is the
    // same blind gate as never setting them, and looks identical in a listing.
    invoker.NextOk(new GetAccountSuspensionStateOk
    {
        UnmanagedLogins = 2,
        SftpLogins =
        {
            new SftpLoginSuspensionFact { Username = "acme_web", Locked = true, Protocol = TransferProtocol.Sftp },
            new SftpLoginSuspensionFact { Username = "acme_files", Locked = true, Protocol = TransferProtocol.Ftps },
        },
    });

    var state = (await client.GetSuspensionStateAsync("acme", default)).Value;

    Assert.Equal(2u, state.UnmanagedLogins);
    Assert.Equal(
        [("acme_web", LoginTransferProtocol.Sftp), ("acme_files", LoginTransferProtocol.Ftps)],
        state.FileTransferLogins.Select(login => { return (login.Username, login.Protocol); }));
}

[Fact]
public async Task A_protocol_this_build_has_never_heard_of_reads_as_unspecified_and_not_as_a_cast()
{
    // Skew, on the axis the sibling AccountLoginPasswordState mapping already
    // guards: a newer agent's value must not become a LoginTransferProtocol
    // member by arithmetic accident.
    invoker.NextOk(WithRawProtocolNumber(99));

    var state = (await client.GetSuspensionStateAsync("acme", default)).Value;

    Assert.Equal(LoginTransferProtocol.Unspecified, state.FileTransferLogins.Single().Protocol);
}

[Fact]
public async Task The_attestation_names_the_logins_as_file_transfer_logins_and_counts_them_per_protocol()
{
    // The sentence an operator reads. Before this, it said "all 2 of its sftp
    // logins locked" over a customer holding a working FTPS credential.
    _agent.SuspensionState = SuspendedWith(
        [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp),
         new FileTransferLoginSuspensionFactDto("acme_files", true, LoginTransferProtocol.Ftps)]);

    await _handler.Handle(new SuspendAccountCommand(accountId, null, null), default);

    // Located exactly as the shipped attestation tests locate it: the one
    // progress report whose line carries "NOT covered".
    var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
    Assert.Contains("all 2 of its file-transfer logins locked (1 sftp, 1 ftps)", attestation.Line, StringComparison.Ordinal);
    Assert.DoesNotContain("sftp logins locked", attestation.Line, StringComparison.Ordinal);
}

[Fact]
public async Task An_unmanaged_login_is_named_in_the_attestation_and_does_not_refuse_the_suspension()
{
    // Both halves in one test, because they are one decision: the count is
    // reported for the same reason the foreign crontab lines are, and refused
    // on for the same reason they are not.
    _agent.SuspensionState = SuspendedWithUnmanaged(2);

    var result = await _handler.Handle(new SuspendAccountCommand(accountId, null, null), default);

    Assert.True(result.IsSuccess);
    var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
    Assert.Contains(
        "2 login(s) sharing this account's uid whose home is neither jail",
        attestation.Line,
        StringComparison.Ordinal);
}
```

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Implement, and extend the IDOR fixture**

`Maran.Host.IntegrationTests/FtpAuthorizationTests.cs`, shaped like the `SftpAuthorizationTests.cs`
beside it. The fixture reads routes off the controller by reflection, so a new route that is not
covered fails naming itself.

- [x] **Step 4: Run the whole backend suite and compare the total against the baseline**

Run: `source scripts/dev && dotnet test Maran.sln`
A verdict is named failures plus the collected total per project, compared with the baseline — never
the exit code. A total that dropped is a failed run whatever it printed.

- [x] **Step 5: Mutation pass**

Remove the limit check → its test dies. Map `ALREADY_EXISTS` to a generic failure → the conflict test
dies. Add a `PasswordHash` property to `FtpUser` → the reflection assertion dies, which is the point:
that assertion is on the axis that would otherwise silently go blind.

Three more on the attestation, and they are the ones that matter, because this is the half that was
missing from the plan and a missing reader fails silently by construction. Drop `login.Protocol` from
the `AgentAccountsClient` mapping so every fact arrives `Unspecified` → the per-protocol count test
dies. Hardcode `UnmanagedLogins` to `0` in the same mapping → the unmanaged-clause test dies; that
mutation is the one that reproduces the exact defect this change closes, a field set on the wire and
read as zero for ever. Make the unmanaged count REFUSE the suspension instead of reporting it → the
"does not refuse" half of that test dies, which is the control on the other direction: this behaviour
is a decision, not an omission.

---

## Phase F — the screens, and closing

### Task 15: The screens, and the copy that names the host to connect to

**Files:**
- Create: `frontend/src/types/{ftpUser,ftpsStatus}.ts`, `composables/apis/useFtpApi.ts`, `stores/ftp.ts`, `pages/ftp/FtpUsersPage.vue`, `components/ftp/{FtpUserCreateForm,FtpUserCreatedDialog,FtpsServerPanel}.vue`, `locales/{en,ru,hy}/ftp.json`
- Modify: `frontend/src/router/index.ts` — settled row 4, one screen: the merged "File transfer" screen REPLACES the SFTP screen. `pages/sftp/SftpUsersPage.vue` is deleted with the owner's go-ahead (`rules/git.md`: removing a file that holds real code is never a side effect), and the shipped `/sftp-users` route becomes a redirect to the new route, so a customer's bookmark keeps working.
- Modify: the navigation — ONE "File transfer" entry replacing the SFTP entry, with `frontend/src/utils/moduleNavigationIcon.ts` gaining the `ftp` module's icon mapping.
- Rewrite: `frontend/e2e/sftp/{list,create,credentials}.spec.ts` — the three shipped SFTP specs, rewritten against the merged screen rather than kept against a dead one (settled row 4 says so explicitly); plus the new FTPS specs below.

**Interfaces:**
- Consumes: `useApi` through `useFtpApi`, called from `stores/ftp.ts` only — never from a component.
- Produces: one "File transfer" screen listing SFTP and FTPS logins together, each row carrying the protocol label the backend supplied. **The SFTP rows come from the shipped `stores/sftp.ts`, composed with `stores/ftp.ts` in the page — no new backend seam and no merged endpoint**: two stores, one screen, because the two modules stay separate on the server and the SPA is the layer whose job is composition. Also: a create form whose protocol choice is disabled for FTPS with an explanatory note when the server has FTPS off; the one-time credential dialog; and an administrator-only server panel that renders the two firewall facts Task 11 assigns to this layer (daemon answering with no matching rules → "listening and unreachable"; daemon off with the FTPS rules still present → "disabled but the ports are still open") by composing `stores/ftp.ts` with `stores/firewall.ts`, and states the listen mode — one line, from `ftpsStatus.ipv4Only`, saying the host has no IPv6 and the daemon is bound IPv4-only (settled row 6). **Every number on these screens — the control port, the passive range — arrives in the DTOs the backend fills from `FtpsDefaults`/`FtpsSettings`; the SPA holds no domain constant** (`rules/vue.md`), and the enable form sends only the hostname and the optional passive address.

**The copy is the deliverable here, not the layout.** The owner's decision 1 has a customer-visible
consequence and the interface is the only place it can be delivered. The credential dialog shows,
for an FTPS login, five facts and no others: the **host** (`ftpsStatus.hostname`, never the
customer's own domain and never `window.location.host`), the **port** (`ftpsStatus.controlPort`, backend-supplied like every other number here), the **protocol** ("FTPS —
explicit TLS"), the **username**, and the **password**, once. Beneath them, one sentence that is not
decoration: connect to this host name exactly as written, because the server's certificate is issued
for it and a client pointed at any other name will report a mismatch. When
`ftpsStatus.certificateIsSelfSigned` is true, a second sentence says the certificate is a placeholder
and the client will warn until a real one is issued.

All of that text is backend-owned where it names a domain value and locale-owned where it is prose,
per `rules/vue.md`: the SPA holds no domain constants and does not compose the hostname itself.

- [x] **Step 1: Write the Playwright specs first — this is the SPA's only test layer**

```ts
test('the FTPS credential dialog names the panel host and never the customer domain', async ({ page }) => {
  // The regression this exists for: a dialog that helpfully filled in the
  // customer's own site domain would send every customer into a certificate
  // mismatch. Assert the rendered value equals the server's hostname, and
  // assert the customer's domain does NOT appear.
});

test('FTPS cannot be chosen while the server has it turned off, and says why', async ({ page }) => { /* … */ });

test('the placeholder-certificate warning appears only when the status says the certificate is self-signed', async ({ page }) => {
  // Both directions. A warning that is always shown is not a warning.
});

test('the server panel states the IPv4-only mode exactly when the status reports it', async ({ page }) => {
  // Settled row 6's last mile: the host decided, the agent reported, the panel
  // persisted — this is the line the operator actually reads. Both directions.
});

test('the firewall warnings follow the composed state in both directions', async ({ page }) => {
  // Task 11's SPA-side composition, asserted: daemon answering + no matching
  // rules shows "listening and unreachable"; daemon off + rules still present
  // shows "disabled but the ports are still open"; each warning is absent in
  // the opposite state, because a warning that is always shown is not one.
});
```

The three shipped SFTP specs (`frontend/e2e/sftp/{list,create,credentials}.spec.ts`) are rewritten
in the same pass against the merged screen — same behaviours, new route and roles — and one of them
gains the bookmark assertion: navigating to the old `/sftp-users` path lands on the merged screen.

Assert on roles and text that the components actually render — a spec asserting a heading on a screen
whose title renders as a `<p>` matched nothing on either side of the behaviour it claimed to
distinguish.

- [x] **Step 2: Run and watch them fail**

- [x] **Step 3: Build the data layer, the store and the screens**

`const` arrow functions only; UI comes from `components/ui`; icons from `lucide-vue-next`; no raw
markup; member order shape-before-behaviour.

- [x] **Step 4: Run the SPA gates**

Run: `cd frontend && npm run lint && npm run typecheck && npm run build && npx playwright test`

> **HAZARD, MARKED BY THE SIXTH PASS — this Step names no verdict and no baseline, and this task is
> the one that moves the baseline most.** Not a change to what the task must deliver; a warning about
> how its result is scored, so an executor does not read a green `npx playwright test` as the gate.
> Three measured facts:
>
> 1. **`npx playwright test` is not what CI gates on.** `rules/testing.md` names `maran test spa`,
>    which runs the same suite and scores it through `suite_compare` on the harness's printed
>    `TEST VERDICT: OK` line — *"a run that exits 0 without printing it fails, and a run that prints
>    it and then exits non-zero fails too"*. `.github/workflows/frontend.yml` runs that, not this.
>    The bare command is fine for the inner loop; it is not the answer this task reports.
> 2. **The baseline is keyed one row per spec file, and this task adds, rewrites and orphans rows.**
>    `grep -c 'spec.ts' scripts/test-baseline.txt` → `73`. This task creates new FTPS specs and
>    rewrites `frontend/e2e/sftp/{list,create,credentials}.spec.ts` against a screen at a new route.
>    `suite_compare` **names a target that VANISHED and names a target that is new since the
>    baseline rather than silently accepting it** — so a rename or a move of a spec file fails the
>    lane by name until Task 16 Step 3 records it. Expect that failure; it is the gate working.
> 3. **The baseline is already mid-migration on exactly these three files.**
>    `sed -n '15,17p' scripts/test-baseline.txt` carries a comment recording
>    `sftp/list.spec.ts at 1 passed against 7`, `sftp/credentials.spec.ts at 10 against 13` and
>    `sftp/create.spec.ts at 3`. Whoever executes this task inherits that state and must say what it
>    was before and after, per `rules/testing.md`'s "a total that dropped is a failed run whatever the
>    exit code says".
>
> **Also under-specified here, stated so it is seen before an hour is spent on it:** the wire now
> carries `forced_tls = 9` and Task 13's `FtpsStatusDto` passes it through, but this task's copy
> specifies the credential dialog's "five facts and no others" and the server panel's lines without
> it. That is not an oversight to fix here — the Known Risks row reserves the surfacing decision to
> the owner. Ask before adding a line; do not add one silently, and do not quietly drop the DTO
> member because no screen reads it.

- [x] **Step 5: Locale parity**

`en`, `ru`, `hy` in parity; `maran structure` checks it and fails naming the missing key.

---

### Task 16: The Definition of Done pass, and the count that proves the run happened

> **PLAN-WIDE CHECKBOX STATUS — SUPERSEDED 2026-09-11 by the SECOND Definition-of-Done pass
> by the second Definition-of-Done pass.** The first pass recorded 84 unticked boxes and declined to
> tick Tasks 1-15 because its own tree was not quiet enough to score: the backend lane was running
> over a peer's live tenant-filter mutant, `maran format --check backend` was red on a peer's import
> ordering, and no polygon run existed. All three have since been measured clean, so the second pass
> ticked Tasks 1-15 against the tree and its own green gates. What a tick means, and what it does
> not, is stated in the CHECKBOX STATUS note at the head of this file — read that before trusting
> one. Full evidence was kept only in the second DoD pass's working report (and the first pass it
> builds on), neither committed.

**Files:** whatever the pass finds. No new behaviour.

- [x] **Step 1: Walk the Definition of Done for every surface this plan added**

> **DONE 2026-09-11** — walked in the first DoD pass's working report (not committed). All five DoD items are met for
> the seven rpcs and the HTTP routes, with one defect (the **six** audit action names — corrected
> 2026-09-12; this annotation said five, which is the File-structure table's stale count repeated
> rather than re-measured, and the plan's own head block already records it as six —
> `/usr/bin/grep -c 'public const string' backend/src/Maran.Modules/Ftp/Services/FtpAuditJournal.cs`
> prints `6`, and `/usr/bin/grep -c Ftp backend/src/Maran.Sdk/Contracts/AuditActions.cs` prints `0`,
> so the names are on
> `FtpAuditJournal` rather than `Maran.Sdk/Contracts/AuditActions.cs`, which that type's own doc
> comment records and STILL correctly records) and one stale count in this step's own text: the tree has EIGHT HTTP endpoints,
> not seven — `ftp-users` carries five (list, get, create, password, delete), not four. All eight are
> covered. The count is left as written because a task's instruction is the owner's to change.

For each of `EnableFtps`, `DisableFtps`, `GetFtpsStatus`, `ReloadFtpsTls`, `CreateFtpsUser`,
`SetFtpsPassword`, `DeleteFtpsUser`, and each of the seven HTTP endpoints (three on `ftps-server`, four on `ftp-users`): a unit test for the
handler's logic, an integration test of the real surface, the IDOR test for every tenant-scoped
route, an asserted audit event, and `en`/`ru`/`hy` keys. A row missing any of the five is unfinished
work, not finished work awaiting tests.

- [x] **Step 2: Run every gate, and quote the numbers**

> **DONE 2026-09-12 — every gate in this step's list was RE-MEASURED on this tree, and both
> blockers the second DoD pass left open are measured CLEAR. Evidence, per-gate verdict lines and
> per-target counts were kept in a working report (task-16-final-gates, not committed). Nothing below is transcribed from
> that pass; each line is a run made here, after `source scripts/dev` (dotnet 9.0.317, cargo 1.98.0,
> node v24.14.0, libprotoc 36.0 — no `NETSDK1045`).**
>
> `maran structure` → `STRUCTURE-OK`. `maran proto` → `PROTO-OK`. `maran migrate check` →
> `MIGRATIONS-OK`. `maran migrate guard` → `MIGRATIONS-ADDITIVE — 26 migration(s) read, 0 destructive
> statements.` `maran api` → `29 endpoints / 90 members checked, 62 not covered (no JSON body), 0
> failures.` `maran lock selftest` → `LOCK SELFTEST OK` (`cases passed: 7   failed: 0`).
> `maran format --check backend` → `FORMAT VERDICT: OK`. `maran licenses --check` → `NOTICES-OK`.
> `maran handshake` → `HANDSHAKE-OK`. Frontend: `npm run lint` → `LINT VERDICT: OK — 4 checks
> declared, 4 run, 4 passed, 0 failed`, `npm run typecheck` and `npm run build` both clean.
>
> The counting gates, with TARGET counts and not only totals — a run that reads fewer targets is the
> false pass rules/testing.md names: `maran test backend` → `19 targets: 2969 passed / 0 failed / 0
> ignored`, `TEST VERDICT: OK` (19 targets = 19 baseline rows, none vanished; this run builds the
> solution with `TreatWarningsAsErrors`, so the list's `dotnet build -warnaserror && dotnet test
> Maran.sln` is covered by a gate that also reconciles the counts). `maran test rust` → `27 targets:
> 1838 passed / 0 failed / 112 ignored`, `TEST VERDICT: OK`. `maran test spa` → `71 targets: 359
> passed / 0 failed / 1 ignored`, `TEST VERDICT: OK` (this is the list's `npx playwright test`,
> scored per spec file).
>
> **Blocker 1 is CLEAR:** `maran agent check` → `AGENT-CHECK OK — fmt, clippy, test and doc all ran
> and all passed`. The `write-with-newline` error in `sftp_control_session.rs:191` is gone.
>
> **Blocker 2 is CLEAR — the polygon is harness-scored, both families, separately quoted.** Each
> family's image was rebuilt with `--no-cache` and a recorded fingerprint, then stamped before the
> containers started (`MARAN-POLYGON-IMAGE family=ubuntu24 … built-from=c07cced3a0e9` and
> `family=alma9 … built-from=69643743a15e`), and the two capability-split runs per family were scored
> by `maran polygon verify`:
> - **Ubuntu 24.04** → `POLYGON VERDICT: OK`, `13 suites discovered, 111 tests executed in total —
>   111 tests executed, exactly the 111 the baseline declares`, with
>   `ftps_on_a_real_host [tests/ftps_on_a_real_host.rs]	15	0	0`.
> - **AlmaLinux 9** → `POLYGON VERDICT: OK`, `13 suites discovered, 111 tests executed in total —
>   111 tests executed, exactly the 111 the baseline declares`, with
>   `ftps_on_a_real_host [tests/ftps_on_a_real_host.rs]	15	0	0`.
>
> `ftps_on_a_real_host` was found BY NAME from tree discovery on both families with its declared
> total, which is what this step asks for. Two notes for the next reader rather than defects:
> `maran polygon build` and `maran polygon run` are not subcommands — `maran polygon` offers `verify`
> and `stamp` only — so the running was done by the per-family commands in `docker/README.md` and
> only the scoring by the harness, exactly the split `scripts/lib/polygon-ci.sh` documents. And
> `docker/polygon/**` + `installer/**` is a LIVE lane (an uninstall-census lane was editing it during
> this pass), so the currency answer above is true as of the scoring run and a later edit there will
> correctly require a rebuild rather than indicating a defect.
>
> **One row is IN FLIGHT and is deliberately not claimed here:** `Maran.ArchitectureTests.dll` read
> 58 against a declared 57, with that project's files being written while the run executed. It is
> the architecture-tests lane's row, not this pass's, and it is a GROWTH — the harness accepts it and
> `TEST VERDICT: OK` still holds.

```
source scripts/dev
maran structure
maran proto
maran migrate check && maran migrate guard
dotnet build Maran.sln -warnaserror && dotnet test Maran.sln
maran agent check
maran handshake
maran licenses --check
cd frontend && npm run lint && npm run typecheck && npm run build && npx playwright test
maran polygon build && maran polygon run && maran polygon verify
```

> **HAZARD, MARKED BY THE SIXTH PASS — three required merge gates are missing from this list, and
> two of the commands in it are not the ones CI scores.** The list is not rewritten: what a task must
> run is the owner's to set, and an executor must find the instruction they were given. What follows
> is the measurement, so the gap is a decision rather than an accident.
>
> - **`maran api` appears nowhere in this plan** (`grep -c 'maran api' <this file>` → `0`), and
>   `rules/testing.md` names it a required merge gate: *"the request contract … it reads every SPA
>   request body and the command each endpoint binds it to, and fails when they have drifted apart"*,
>   existing precisely because endpoints now bind their command object directly and *"nothing but this
>   gate observes the seam"*. This plan adds seven HTTP endpoints and an SPA that calls them. A
>   deliberate change needs `--accept` against `scripts/api-contract-baseline.txt`, in its own commit,
>   the way `maran proto --accept` is handled in Task 10 Step 1.
> - **`maran test` appears nowhere either** (`grep -n 'maran test' <this file>` → no hits).
>
>   > **CORRECTION, 2026-09-13. "Appears nowhere" is false for both, and the second one is false in a
>   > way that matters.** Re-measured with `/usr/bin/grep` (the shim agrees — a tracked plan file is
>   > fully visible to both):
>   >
>   > ```
>   > $ /usr/bin/grep -n 'maran api'  <this file> | cut -d: -f1   ->  14 3210 3272 3286 3297   (the last two are this block)
>   > $ /usr/bin/grep -n 'maran test' <this file> | cut -d: -f1   ->  18 19 20 996 2602 3135 3217 3220 3221 3279 3287 3290 3291 3307 3309
>   > ```
>   >
>   > **`maran test` is named by a TASK**, at `:996` — Task 5 Step 1's `Run: source scripts/dev &&
>   > maran test`, with `Expected: the identical rows and totals as the committed baseline`. So the
>   > claim that a required counting gate is absent from this plan's instructions was wrong when it was
>   > written, and the bullet below it — that the plan's bare `dotnet test Maran.sln` and
>   > `npx playwright test` "produce numbers a human then has to reconcile by hand" — is true of those
>   > two Steps and not of the plan as a whole.
>   >
>   > **`maran api` survives in the narrower form only.** Its four hits are the checkbox-status header
>   > at `:14`, the DoD verification block at `:3210`, and this hazard block's own two — so no TASK
>   > names it, which is the real gap and the one the owner still has to rule on. Adding a gate to a
>   > task's list is adding work and is still not done here.
>   >
>   > Why the original said otherwise: both figures were taken with `grep -c` rather than `grep -n`,
>   > which cannot distinguish a hit in a task from a hit in the sentence reporting it, and one of them
>   > was evidently taken before `:996` existed and never re-derived. This is the plan's own rule
>   > about counts, turned on the audit that wrote it: **cite the command AND read its output — a
>   > count with no line numbers is not a measurement of where a thing is.**
>   `rules/testing.md`: `backend.yml` runs `maran test backend` and `agent.yml` runs `maran test rust`,
>   *"each gated on the harness's printed `TEST VERDICT: OK` line rather than on a status"*, and
>   `frontend.yml` runs `maran test spa`. The bare `dotnet test Maran.sln` and `npx playwright test`
>   below produce numbers a human then has to reconcile by hand — which is the reconciliation the
>   harness exists to do and the one that has already been got wrong here.
> - **`maran polygon verify` scores an already-executed run from its logs and runs nothing itself**;
>   it is in the list and correctly ordered after `maran polygon run`. No change owed.

Report `N passed / N failed / N skipped` per project and the sum, name every failure, and compare the
sum against `scripts/test-baseline.txt`. `maran polygon verify` must find the
`ftps_on_a_real_host` suite by name with its declared total — its suite list is discovered from
`agent/crates/*/tests`, so a suite that no `--test` list runs fails this job rather than vanishing
from it. **Both polygon images, scored separately, and the pair of scores quoted** — one family's
green is not this plan's answer, and finding F2 is the demonstration: a TLS option key that exists
on the RHEL family and not on the Debian one produced a daemon that would not start on Ubuntu while
every RHEL-side check stayed green.

- [ ] **Step 3: Update the baseline in its own commit**

`scripts/test-baseline.txt` gains the new rows. A baseline refreshed in the same commit as the code
it counts is a baseline nobody reviewed.

> **STATUS 2026-09-12 — the ROWS ARE DONE; the box stays unticked because the COMMIT is what this
> step names and a commit is the owner's. Evidence:
> a working report (task-16-final-gates, not committed).**
>
> Read as written, this step is not "the rows are right" — it is titled *in its own commit* and its
> whole rationale is the separation ("a baseline refreshed in the same commit as the code it counts is
> a baseline nobody reviewed"). Nothing on this branch is committed and `rules/git.md` reserves that
> to the owner, so the step cannot be closed here without stretching it. The row half is finished:
>
> - Every row was verified against a measured run, not transcribed. Two were stale and are landed:
>   `rust  handshake [tests/handshake.rs]  5 -> 6` (6 `#[tokio::test]` in the file, run row `6 0 0`)
>   and `backend  Maran.Host.IntegrationTests.dll  321 -> 324` (3 `[Fact]` in
>   `PanelToAgentFtpsWireShapeTests.cs`, run row `324 0 0`). Each count was taken twice — from the
>   source and from the run — and made to agree, with the reasoning written into the baseline's own
>   header.
> - **NO COUNT FELL**, on any stack: 27 rust / 19 backend / 71 spa targets all reported, none
>   vanished, and every other digit unchanged. The spa rows needed no edit (`71 targets: 359 passed /
>   0 failed / 1 ignored` matches them exactly).
> - `Maran.ArchitectureTests.dll` 57 -> 58 was NOT recorded: it is a live peer lane, and a digit
>   taken off a half-written project is the false alarm that gets a real fall dismissed.
> - The ignored column was re-derived rather than carried: the thirteen `*_on_a_real_host.rs` files
>   hold 5/12/1/6/6/10/15/5/3/15/4/13/16 = 111 `#[test]` and 111 `#[ignore]`, plus maran_ops's one =
>   the 112 the rust run reports; all thirteen are still named in `docker/README.md` and in
>   `.github/workflows/agent.yml`.
> - **The baseline still BITES**, proven rather than asserted: `#[cfg(any())]` planted above the new
>   handshake case (a mutation that compiles and removes exactly one test) made a full, unfiltered
>   run print `TEST VERDICT: FAILED — findings: rust: handshake [tests/handshake.rs] collected 5
>   passed against a baseline of 6`, plus the declared-total and collected-total drops. Restored from
>   a copy with `cp` (never `git checkout --`), `touch`ed, and verified identical with `cmp`.
>
> **What the owner owes this step:** commit `scripts/test-baseline.txt` on its own, separately from
> the code it counts. Then it can be ticked.

- [x] **Step 4: Verify the threat note exists and still says OUTSTANDING**

> **DONE 2026-09-11.** `docs/superpowers/notes/2026-09-09-ftps-threat-note.md` exists (30324 bytes),
> predates the privileged files, states OUTSTANDING at four points, and covers its stated Task 2
> scope plus four amendments. **New finding: the note is UNTRACKED in git** — one of ten untracked
> notes — so a reviewer who pulls this branch sees the privileged change and none of its argument.

The note was WRITTEN in Task 2 Step 0 — `rules/security.md` requires it before the privileged
change, and scheduling the writing here, after four phases of privileged code, would have made it
exactly the after-the-fact justification the rule names as the failure mode. This step only
verifies: `docs/superpowers/notes/2026-09-09-ftps-threat-note.md` — **the sixth pass resolved the
`2026-09-XX` placeholder against the tree; Task 2 Step 0 wrote it on 2026-09-09, and a verification
step that names a file by a wildcard cannot fail when the file is missing, which is the whole of what
this step is for** — exists, predates the privileged
files (Task 2 Step 0 lists what it must contain), still states the second-reviewer requirement as
**OUTSTANDING**, and covers everything that actually shipped — a surface added mid-execution that
the note does not mention is a finding here, not a shrug. The branch must not merge to `main` until
the review happens, and the owner is told at hand-off that it is carrying one.

- [x] **Step 5: Correct the documents this plan makes untrue**

> **STATUS 2026-09-11 (second DoD pass) — DISCHARGED except one owner-gated item, so this step is
> now ticked.** Residues 1, 3 and 4 were closed by earlier passes. The two main documents are now
> CORRECTED in the tree, which is what the first pass named as the largest remaining piece of work:
> `README.md:226` reads "**An FTP daemon IS installed, and it is switched off.**" and :233 "Nothing
> is listening: no port 21, no data port, no configuration file even rendered, until an
> administrator turns FTPS on"; `CHANGELOG.md:85` reads "**FTPS ships, and this entry used to say it
> did not.**" and names the retraction rather than deleting the sentence, exactly as this step
> requires. Residue item 2 remains and is the owner's: `ls -a agent/crates/ops/src/ftp/` is still a
> lone `.gitkeep`, and `rules/git.md` asks for the go-ahead before removing it.

`README.md` says "**File access is SFTP only.** No FTP daemon is installed and no second port is
opened. FTPS is a separate provisioning path with its own certificate story and is tracked as issue
#20." `CHANGELOG.md` carries the same claim in its own words ("**No FTPS and no web database
manager.** File access is SFTP only — there is no FTP daemon installed and no second listening port
— … FTPS is tracked as issue #20"). Both become false with this plan and are corrected in it — a doc
comment that describes what the code does not do is a defect of the same severity as the behaviour it
misdescribes. Neither may simply lose the sentence: FTPS ships **installed and off**, so the corrected
text says that, and says that turning it on is an administrator action with firewall consequences.

> **AMENDED 2026-09-09 (fifth pass) — three of the five documents this step named have already been
> corrected, by the tasks that made them untrue, and the sentences quoted here no longer exist.**
> Re-grepped against the tree:
>
> - **`agent/crates/ops/src/sftp/mod.rs` does not contain the quoted sentence, or the words "FTP
>   daemon", at all.** Its area comment now opens "SFTP logins: system accounts served by the host's
>   OpenSSH daemon, each one chrooted into a root-owned jail that its account's real home is
>   bind-mounted into." That is already true and this step must not go looking for text to fix in it.
>   Nothing is owed here.
> - **`rules/rust.md` carries neither "stays for a future FTP daemon and holds nothing" nor "A future
>   FTP daemon would get its own `ftp/` beside it".** Both were reworded by the tasks that landed the
>   folders: the `services/sftp/` entry now says "`ftps/` sits beside it for the vsftpd daemon — a
>   separate service, not a mode of this one", and the `ops/` area block already describes `ftps/` and
>   `logins/` in full. **So the layout's new rows are present and this step's verification of them is
>   the only work left on that file** — plus the two residues below, which are real and still there.
>
> **SIXTH PASS — RE-MEASURE BEFORE YOU ACT ON THE FOUR ITEMS BELOW. Two of them are already done in
> the working tree, and this is the exact failure this amendment was written to stop happening twice.**
> Re-grepped 2026-09-09, after the fifth pass:
>
> - **Item 1 is CLOSED.** `grep -n 'accounts,sites,php,db,sftp' rules/rust.md` prints line 255 as
>   `{accounts,sites,php,db,sftp,ftps,logins,files,cron,firewall,ssl,` — the token is already `ftps`,
>   and `logins` was added beside it. (`git show HEAD:rules/rust.md | grep -n 'accounts,sites,php,db,sftp'`
>   still shows the old `…,sftp,ftp,…`, so this is uncommitted work in the tree, not a
>   misreading.) Verify before editing; do not re-apply.
> - **Item 3 is CLOSED.** `grep -nic occurrence rules/rust.md` prints `2`, not the `0` the item
>   quotes as its measurement — `rules/rust.md:679` now opens
>   *"Reading a live config back: take the LAST occurrence of a key, never the first."* Verify before
>   editing; do not write a second copy of the rule.
> - **Items 2 and 4 are still owed.** `ls -a agent/crates/ops/src/ftp/` prints only `.gitkeep`, and
>   `grep -n user_config rules/rust.md` still shows the `vsftpd/{user_config,vsftpd_daemon_config}.rs`
>   row at line 353.
>
> **Nothing on this list is withdrawn as a requirement** — each item stays exactly as written, because
> what a task is asked to deliver is the owner's and this audit corrects facts only. What is added is
> the measurement, so an executor checks the file instead of trusting a count taken a pass ago.
>
> What this step DOES still owe on the map, and it is smaller than the paragraph above described:
>
> 1. `rules/rust.md`'s ops area list still reads `{accounts,sites,php,db,sftp,ftp,files,cron,firewall,ssl,backup,monitor}/`
>    — the token is `ftp`, the folder that exists and holds code is `ftps`, and the two are one letter
>    apart in a map a reader uses to decide where a file goes. It becomes `ftps` when the empty
>    `ops/src/ftp/` goes. **(Sixth pass: already done in the tree — see the re-measure above.)**
> 2. `agent/crates/ops/src/ftp/` is still a lone `.gitkeep`. It is removed with the owner's go-ahead
>    (`rules/git.md`: removing a file that holds real code is never a side effect — this one holds
>    none, which is why the go-ahead is cheap and still asked for), because a placeholder whose stated
>    purpose is now filled elsewhere is a map entry that misleads.
>    **(STILL OPEN 2026-09-12 — `ls -a agent/crates/ops/src/ftp/` is a lone zero-byte `.gitkeep`.
>    CITATION CORRECTED: it is not `rules/git.md` that reserves this. That rule guards "a file that
>    holds real code", and a zero-byte placeholder is the case it explicitly does not cover, so read
>    alone it would make this a free deletion. The rule that actually binds is
>    `rules/architecture.md` "Skeleton policy", whose first line is that the full canonical skeleton
>    exists from day one **"(owner's decision)"**, and whose disposal rule is "Delete the `.gitkeep`
>    when the folder gains its first real file" — a condition this folder will never meet, because
>    the protocol question it was reserved for was answered as FTPS. Removing it is therefore an
>    amendment to a day-one decision of the owner's, not the discharge of a condition, which is a
>    STRONGER reason to ask than the one cited and reaches the same answer. The requirement is
>    unchanged; only its authority is.)**
> 3. **`rules/rust.md` carries no last-occurrence rule for a live-config read.** Task 8's status
>    read-back rests on it — read the LAST occurrence of a key, never the first, so that where the
>    file and the daemon could disagree the agent answers with what the daemon obeys — and today that
>    rule exists only in this plan and in the doc comments of `get_ftps_status.rs` and `ftps_state.rs`
>    (measured at the fifth pass: `grep -ni occurrence rules/rust.md` found nothing. **Re-measured at
>    the sixth: it now finds two, at `rules/rust.md:679`, so this item is closed — see the re-measure
>    block above.** (a working task-08 report,
>    PHANTOM 3, which reported it rather than editing `rules/` from outside its scope). It belongs in
>    the config-write section beside the write protocol, because the next area that reads a config
>    file back will otherwise re-derive it or, worse, not.
> 4. `rules/rust.md`'s templates block still lists `vsftpd/{user_config,vsftpd_daemon_config}.rs`.
>    **(SATISFIED — re-measured 2026-09-12. The brace form is gone:
>    `/usr/bin/grep -c 'user_config,vsftpd_daemon_config' rules/rust.md` prints `0`, and
>    `rules/rust.md:348-356` now reads `vsftpd/vsftpd_daemon_config.rs — plus
>    vsftpd/user_config.rs, which is a PLANNED home and not a file`, names "Deferred
>    deliberately" as where the three features are recorded, and adds "Nothing renders a
>    `user_config_dir` today" — which is the change this item asked for, changed rather than
>    deleted. The requirement below is NOT withdrawn; only its measurement is dated, so the
>    next executor checks the file instead of hunting for a brace list that no longer exists.
>    The STATUS block at the head of this step already said items 1, 3 and 4 were closed; this
>    is the item-level note it lacked.)**
>    `user_config.rs` is not built by this plan and its three would-be features are in "Deferred
>    deliberately"; the row is **changed rather than deleted** — it keeps naming the file as where
>    per-login read-only access, bandwidth limits and quotas would land, and says they are deferred —
>    so the deferral stays visible in the map instead of becoming a silent gap.

---

## Open questions — what this plan could not settle, and who settles it

Nothing here is papered over with a default. Each row names the decision, what it blocks, and whose
decision it is.
Rows 1 and 2 were put to the owner on the day the plan was written and are settled; they are kept
here rather than deleted so the evidence that led to each question survives beside its answer.

1. **The virtual-user mechanism (the owner).** This plan implements every property issue #20's scope
   sentence asks for except one — the password lives in the host's shadow file rather than in a
   database only vsftpd's PAM stack reads — because `pam_userdb`'s backing library and its writing
   tool differ between AlmaLinux 9 and AlmaLinux 10 (measured; see "The one place this plan departs
   from issue #20"). The evidence is in the plan and the argument is made, but the deviation is from
   the issue's own words and the owner should say yes before Task 1 starts. **Settled by the owner, 2026-09-09: yes — system logins with the PAM group gate. No longer blocks anything.**
2. **Whether the FTPS hostname must be a site the panel serves (the owner).** This plan says yes:
   `install_certificate` requires a vhost, HTTP-01 requires one anyway, and the alternative is a
   second writer of the certificate store — which is where a customer's private key was destroyed
   once already. The consequence is that an operator cannot point FTPS at the panel's own vhost
   hostname, whose certificate lives at `/etc/maran/tls/panel.crt` with no marker beside it and
   outside `ops::ssl`'s store. If the owner wants the panel's own hostname usable directly, that is a
   different design and needs the site-less certificate path this plan deliberately did not build.
   **Blocks: Task 8 and Task 13.**
   **Settled by the owner, 2026-09-09: the hostname must be a site the panel serves; the
   site-less path stays unbuilt.**
3. **The concurrency ceiling (the owner / product).** `max_clients=100` and a hundred passive ports.
   That is a number chosen so that the port range can never be what runs out first, not a number
   derived from what a Maran host is expected to serve. If the target is a busy shared host, both
   numbers move together and the firewall rule with them. **Blocks: the exact values in Task 6, 8 and
   11; not their shape.**
   **Settled by the owner's criterion (maximum quality and security, change volume no objection),
   2026-09-09: the shape stands and the values stay 100/100 — conservative, stated as named
   constants with this reasoning beside them, and movable later without touching the shape.
   Folded into the tasks: the constants are `FtpsDefaults` in Task 13, the ONLY place the numbers
   exist — the enable command carries none of them, the agent receives them on the wire and owns
   no default, and the screens render them from backend-filled DTOs (Tasks 10, 11, 15).**
4. **Whether the merged "File transfer" screen replaces the shipped SFTP screen or sits beside it
   (the owner).** Replacing it changes a route customers may have bookmarked and rewrites the
   shipped SFTP e2e specs; keeping both means two screens listing overlapping things. This is a
   product decision, not a technical one. **Blocks: Task 15's routing only.**
   **Settled 2026-09-09, by the cleanliness criterion: one screen. The merged "File transfer" list
   replaces the SFTP screen; the old route redirects to the new one so a bookmark keeps working,
   and the shipped SFTP e2e specs are rewritten against the merged screen rather than kept against
   a dead one. Two screens listing overlapping logins is the untidy option and loses.
   Folded into Task 15: the replacement, the redirect, the navigation change and the three named
   spec rewrites are in its file list and steps.**
5. **Licence tier (the owner / product).** `Sftp` is `LicenceTier.Included`. FTPS is the alternative
   for operators who specifically need it, which is exactly the shape of a paid module — or exactly
   the shape of a thing that should never be a reason to lose a customer. **Blocks: one line of
   `FtpManifest`.**
   **Settled 2026-09-09: `LicenceTier.Included`. FTPS is table-stakes compatibility, not a
   value-add — a customer who needs it will read a paywall on it as the panel lacking FTP. One
   line, reversible the day the owner decides otherwise.**
6. **IPv6-disabled hosts (the owner, or a follow-up issue).** The config uses
   `listen=NO`/`listen_ipv6=YES`, which is what both distributions' own shipped configuration does and
   which serves IPv4 clients through the dual-stack socket. On a host where IPv6 is disabled at the
   kernel, the daemon will not bind and Task 8's post-swap probe reports `NotListening` — correctly,
   loudly, and with no way for the operator to fix it from the panel. Whether that deserves an
   IPv4-only fallback setting is a judgement about who Maran's operators are. **Blocks: nothing;
   the failure is visible and reported.**
   **Settled 2026-09-09, by the quality criterion: a visible failure the operator cannot fix from
   the panel is not the maximum-quality answer. Task 8's enable path gains a deterministic probe:
   bind an IPv6 listening socket the way vsftpd would; if the kernel refuses, write the config
   with `listen=YES`/`listen_ipv6=NO` (IPv4-only) instead, and carry which mode was chosen in the
   status answer so the screen states it. No new setting — the host decides, the panel reports.
   Folded into the tasks: the template mode and its own golden (`vsftpd_ipv4_only.conf`) are Task 6, the probe and
   `ListenMode` are Task 8, the wire field is Task 10's `GetFtpsStatusOk.ipv4_only`, the
   persistence and DTO are Task 13, and the screen's line is Task 15.**
7. **The second reviewer (the owner must name one).** `rules/security.md`'s escalation rule applies
   three times here: a new listening port, a new PAM stack, and a new privileged installer step. An
   agent session cannot dispatch a reviewer. The rule's exact shape is followed — the threat note is
   written first, states the requirement as OUTSTANDING, and the branch does not merge to `main`
   until a human has read it. **Blocks: the merge, not the work.**

## Known risks that are not open questions

Each of these has a decision and a named observation that would catch it going wrong. They are listed
so nobody re-derives them mid-execution.

- **`seccomp_sandbox`.** vsftpd's own default is taken. Its known failure is a session dying with
  `500 OOPS: priv_sock_get_cmd` or a child killed by `SIGSYS`. Task 10's real-host transfer is what
  observes it; the fix, if it appears, is one line plus the measured message and kernel in the
  template's comment plus a golden update.
- **An Ed25519 certificate under a directive called `rsa_cert_file`.** The name is historical;
  vsftpd hands the file to OpenSSL's generic loader. Measured on 2026-09-08: an Ed25519
  certificate/key pair loads on both families, and a missing one is refused with
  `500 OOPS: SSL: cannot load RSA certificate`. Task 10's polygon renders and validates with real
  material, so a build that stops accepting it fails there.
- **The validator's ephemeral port.** Task 8's layer 1 binds `127.0.0.1:0`, reads the port and drops
  the socket before spawning. A collision in that window refuses a good config — fail-safe, nothing
  swapped — and the refusal says so.
- **A restart aborts transfers.** `ReloadFtpsTls` is a restart because vsftpd loads its certificate
  once. It fires only on a `CertificateInstalled` for the FTPS hostname, which is at most once a
  renewal period, and the panel's copy says so.
- **A root-level edit of the live config can switch forced TLS off, and nothing would say so.**
  Added by the fourth amendment, once the parser was measured to take the LAST occurrence of a key.
  `/etc/maran/vsftpd/vsftpd.conf` is root-owned under `/etc/maran` and the agent is its only writer,
  so this is not a customer's reach: it is an operator with a text editor, another tool, a restored
  backup, or a future Maran feature that "just adds a line". One appended
  `force_local_logins_ssl=NO` leaves a daemon that parses, starts, greets with `220` and accepts
  plaintext credentials. **The decision is to detect and report, not to defend:** the file already has
  exactly one writer and a whole-file replace on every apply, and hardening it further (an immutable
  attribute, a watcher, a re-render-and-`cmp` on every status call) would spend real machinery on a
  threat that already requires root. **The named observation:** Task 10's
  `the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen`, which asserts on
  the served file, plants the duplicate itself to prove the assertion can fail, and shows the
  plaintext login being accepted while it is planted — plus Task 8's rule that every live-config read
  takes the last occurrence, so the status the panel renders is the daemon's answer and not the
  panel's own echo. What this deliberately does NOT buy, stated so nobody assumes it: a customer- or
  operator-facing indicator that the running daemon is not forcing TLS. That would need a ninth field
  on `GetFtpsStatusOk` and its three siblings, a DTO, a screen line and three locales; it is a design
  decision for the owner rather than something an amendment should slip in, and it is only worth
  making once the detection above has ever fired.
  **Corrected by the fifth pass — the detection is no longer hypothetical, and how far it reaches is
  now exact.** Task 8 shipped `FtpsState::forced_tls: Option<bool>`: the live-config read-back
  already answers, per status call, whether the file the daemon was started against still forces TLS
  on both the control and the data channel, held up by
  `a_planted_force_local_logins_ssl_no_is_reported_as_forced_tls_switched_off` and
  `a_planted_force_local_data_ssl_no_is_reported_as_forced_tls_switched_off`. So the sentence above
  understated what exists: what is missing is **not the detection but its last mile** — the ninth
  wire field, the DTO member, the screen line and the three locales. The decision stands unchanged
  (detect and report, do not defend, and do not slip a customer-facing surface in by amendment); what
  changes is its price, which is now one additive proto field and its readers rather than a
  mechanism. The owner decides, and this is the row they decide from.
  **Sixth pass — a fact this row is now missing, and a scope question it raises. THE DECISION BELOW
  IS UNCHANGED AND IS STILL THE OWNER'S.** The additive proto field this row prices has *already
  landed in the tree*: `forced_tls = 9` is on `GetFtpsStatusOk`, `EnableFtpsOk`, `DisableFtpsOk` and
  `ReloadFtpsTlsOk`, is filled on all four by
  `agent/crates/agent/src/services/ftps/ftps_status_fields.rs`, and is recorded in the accepted
  `proto/agent/v1/contract-baseline.txt` (four rows). Verify with
  `grep -n forced_tls proto/agent/v1/contract-baseline.txt`. So of the four things this row lists as
  outstanding — wire field, DTO member, screen line, three locales — the first is done and the
  second is in flight in Task 13's lane. **This audit did not decide that and does not ratify it:**
  it corrects the plan's field counts, which were wrong against the contract either way, and puts
  the question in front of the owner in the row built for it. What remains genuinely undecided is
  the *customer- and operator-facing* half: whether the merged screen states forced TLS, and in
  which words. Task 15's copy is unchanged by this pass and still specifies five facts in the
  credential dialog, none of them this one.
- **`pasv_address` beside a dual-stack listener.** The pairing is documented-flaky in vsftpd, so it
  is observed rather than assumed: Task 10's
  `the_advertised_passive_address_appears_in_the_pasv_reply` asks the real daemon what it puts in
  the `227` reply, and a daemon that ignores the directive on the IPv6 socket goes red there as a
  finding. On a host the probe put in IPv4-only mode the question does not arise.

## Deferred deliberately, so the deferral is visible rather than buried

- **phpMyAdmin** (spec §11) — deferred from the same plan as FTPS, still with no issue of its own. A
  separate deployable with its own vhost, its own authentication and its own licence question.
- **Implicit FTPS on port 990.** Explicit FTPS on 21 is what RFC 4217 standardised and what every
  current client speaks; adding 990 means a second port for a deprecated mode.
- **Per-login read-only access, bandwidth limits and quotas.** vsftpd can express all three through
  `user_config_dir`, which this plan does not enable. When one of them is genuinely wanted, that
  directory and `templates/src/vsftpd/user_config.rs` are where they go — which is what the canonical
  layout in `rules/rust.md` originally anticipated, and why the row is being changed rather than
  simply deleted.
- **Anonymous FTP.** Never. `anonymous_enable=NO` is in the template and is not a setting.

## Self-review

Run once against the spec and the issue after the plan was written, with what it found and what was
changed inline.

- **Every scope item in issue #20 has a task.** vsftpd installed and configured by the installer as
  a new `installer/lib/8x-ftps.sh` → Task 2, numbered `89-ftps.sh`. Proven by the polygon the way the
  other installer steps are → Task 3, running the step's own functions and never repeating them.
  Logins on the account's uid, chrooted, password never in the panel → Tasks 4, 5, 9, with the
  mechanism deviation argued and flagged for ratification. The TLS decision → the owner's decision 1,
  plus Tasks 7, 8 and 13. A `Protocol` distinction the committed `ftp.proto` does not model → Task
  10's `TransferProtocol`, additive, in `common.proto`.
- **Three things the issue says that this plan found to be otherwise**, each corrected in the text
  above: the prerequisite plan's filename is `2026-09-01-maran-databases-sftp.md`, not
  `…-databases-ftp.md`; the additive proto edit is not confined to `ftp.proto` — `common.proto`,
  `accounts.proto` and `firewall.proto` all need one; and the TLS question the issue leaves open
  ("reuse the account's site certificate … or gets its own") was already answered by the owner before
  this plan, in a third way the issue does not list: one certificate for the panel host, consumed
  from the SSL module's store and never written by FTPS.
- **One defect found in shipped code while planning, and it is a task rather than a note.**
  `ProcessSftpHost::account_logins` selects logins by "passwd home equals the SFTP jail", so the
  shipped `SetAccountLoginsLocked` would not lock an FTPS login and a suspended customer would keep a
  working write credential. Task 5 closes it before an FTPS login can exist. The Global Constraints
  section originally claimed the opposite — that suspension already covered FTPS "by uid" — and that
  sentence was corrected after the code was read.
- **Types and names are consistent across tasks.** `FtpsUserName`, `FtpsJail`, `PassiveAddress`,
  `FtpsConfiguration`, `FtpsState`, `FtpsError`, `LoginProtocol`, `AccountLogin`, `AccountLoginSet`,
  `CertificateState`, `VsftpdTlsVersionKeys`, `TransferProtocol`, `IAgentFtpsClient`, `CreateFtpsUserArguments`, `FtpUser`,
  `FtpsSettings`, `LoginTransferProtocol` and `FileTransferLoginSuspensionFactDto` are each
  defined in exactly one task and used with the same spelling afterwards. There is one resource class
  per module and it is spelled `ErrorMessages`, as the shipped csproj generates it. Four inconsistencies were
  found and fixed in this pass: the file-structure table claimed six new adapter methods where Task 1
  adds two; `unmanaged_logins` was numbered 12 where 1–8 are in use and 9 is free; `ops/src/ftps/mod.rs`
  and the systemd-escaping extraction were needed by Task 4 but listed under Tasks 8 and 5; and the
  owner's decision 1 pointed at "Task 14" for the screens, which are Task 15.
- **No placeholders.** Every config value, path, mode, port, PAM line and unit file is written out.
  The four things this plan does not know are in Open Questions with a name against each, and the four
  it knows but cannot pre-empt are in Known Risks with the observation that would catch each.
- **Ordering holds the brief.** The three tasks that can only fail on a real host — the adapter facts,
  the installer step, and the polygon that proves it — are Tasks 1, 2 and 3, and every panel task
  depends on something they established. The threat note is Phase A's first privileged-work step
  (Task 2 Step 0), because the rule requires it before the change, not after; and the composite the
  whole plan rests on — PAM gate, `--non-unique` uid, chroot, bind mount, forced TLS, together —
  is hand-proven end to end in Task 3 Step 5, before Phase B writes a line that depends on it. The
  first line of C# is written in Task 12, after the daemon has been proven to start, refuse a
  plaintext login and confine a real session on both families.

### Amended 2026-09-09 — closing the adversarial review

An adversarial pre-execution review (kept only as a working report, not committed: 6 BLOCKER, 9 MAJOR,
15 MINOR) was executed against this plan and every finding is folded into the text above; none is
contested. The substance: the threat note moved to Task 2 Step 0 (written before the privileged
change, as the rule requires, with Task 16 Step 4 reduced to verification); settled rows 3, 4 and 6
now live in the task bodies, not only in their rows (`FtpsDefaults`, the merged-screen replacement
and redirect, the IPv6-bind probe with its `ListenMode` threaded template → ops → proto → panel →
screen); the deletion cascade and the suspension attestation now name the seams the tree actually
has (`AccountOperations::delete` in `account_operations.rs` with a subsequence order test, and
`accounts_service.rs` for the `SftpLoginSuspensionFact`/`unmanaged_logins` mapping — the previously
named `delete_account.rs` and `sftp_service.rs` seams do not exist in those roles); Task 13's
refusal test asserts `Error.Code` + `ErrorType` and the no-paths guarantee moved to the resx values
in all three locales, because `Error` deliberately has no message; `ReloadFtpsTls` gained its ops
operation and the two misnamed ops files took their mechanical rpc names (`enable_ftps.rs`,
`get_ftps_status.rs`); the composite is hand-proven in Phase A (Task 3 Step 5); the real-host
deletion suite is extended with an FTPS login; the merged screen's sources, the limit's full
thread (`AccountSnapshot` and its construction sites), the Host-integration test placement, the
SPA-side firewall composition, the installer check's honest blind spot, `ftps_status.rs`'s family
name, and `AccountLoginSet`'s file under `logins/model/` are all stated in the tasks that own them.
Every file path written in this amendment was re-verified against the working tree.

### Amended 2026-09-09 (second pass) — closing the verification's five findings

A verification of the amended plan (kept only as a working report, not committed) confirmed all 30
original findings closed and raised five NEW ones — 1 BLOCKER, 1 MAJOR, 3 MINOR. All five are folded
into the task bodies above; none is contested, and one is implemented with a stated correction.

- **N1 (BLOCKER) — the suspension attestation's panel half was in no task.** Task 10 put
  `TransferProtocol protocol = 3` and `uint32 unmanaged_logins = 9` on the wire and had
  `accounts_service.rs` set them, and nothing read them: `SftpLoginSuspensionFactDto` is
  `(Username, Locked)` today and `SuspendAccountCommandHandler`'s attestation says
  `all {state.SftpLogins.Count} of its sftp logins locked` — a sentence that becomes FALSE the day an
  FTPS login exists, telling an operator a customer is fully locked while that customer holds a
  working credential. The reading half is now **Task 14**, in that task's Files list and in a body
  section with four tests and three mutations: it is the only task that already opens the `Accounts`
  module, the attestation is an account-lifecycle sentence, and splitting the DTO into Task 12 and its
  reader into Task 14 would have left a buildable interval carrying the very defect being closed.
  `SftpLoginSuspensionFactDto` is renamed `FileTransferLoginSuspensionFactDto` and gains `Protocol`;
  `AccountSuspensionStateDto` gains `UnmanagedLogins` and renames `SftpLogins` to
  `FileTransferLogins` (18 construction sites, all compiler-named); both attestation sentences and
  both per-login lines speak of file-transfer logins with the protocol beside each name.
  `GrpcAccountsServiceInvoker`, the two interfaces and `ResilientAgentAccountsClient` are verified
  UNCHANGED and the plan says so, because a reader will look. One correction is stated rather than
  silently made: `CanStillAuthenticate` answers only about the account's OWN passwd entry, so it gains
  a sentence of doc naming what it does not answer instead of the protocol vocabulary the finding
  asked for — the SFTP-specific text was in `Unobserved`'s enumeration, which is where the correction
  landed.
- **N2 (MAJOR) — a file name that fails `maran structure`.** `templates/src/vsftpd/daemon_config.rs`
  holding `VsftpdDaemonConfig` is renamed `vsftpd_daemon_config.rs` in the file-structure table and in
  Task 6, with the gate's own sentence quoted: `check-structure.sh` check 16 derives the expected
  basename from the single `pub struct`, `daemon_config` is in neither the `subject_named` allow-list
  nor the `*_service|*_status` family, and the shipped prior art is folder-prefixed and exact
  (`nftables_allow.rs`/`NftablesAllow`). Every other new `.rs` file this plan creates was checked
  against the same rule and passes: the `ops/src/ftps/` operations are named after their rpcs, the
  `model/` types after themselves, `ops/src/logins/`'s six after theirs, and `services/ftps/`'s two
  are the `*_service`/`*_status` family the script exempts by name.
- **N3 (MINOR) — two produced types with no file.** `CreateFtpsUserArguments` is named in Task 12's
  Create list under `Services/FtpsService/`, where `rules/csharp.md`'s map puts a service client's own
  shapes, and its password member is the shipped `SensitiveString` rather than a `string` — which is
  what makes the redaction a property of the type instead of this record's discipline. The second
  spelling is resolved rather than given a file: there is no `FtpErrorMessages`; the resource class is
  `ErrorMessages`, as every shipped module spells it, and the locale sweep reads the three `.resx` XML
  files through one named test fixture, `Maran.Modules.Ftp.Tests/TestSupport/ErrorMessageValues.cs`,
  for the reason `ResourceKeyParityTests` reads them that way — a `ResourceManager` in a test process
  falls back to the neutral text, so a path pasted into the Russian file would be swept as its English
  fallback and pass.
- **N4 (MINOR) — `port_to` had no field number, and the obvious one is refused.** The three numbers
  are written out, per message and with the evidence: `FirewallRule` holds 1–3 so `port_to = 4`;
  `AllowPortRequest` and `DenyPortRequest` each carry `reserved 4; reserved "ssh_port";` and hold 1,
  2, 3, 5, 6, so both take `port_to = 7`. An executor copying `4` across all three would get a protoc
  refusal in two of them.
- **N5 (MINOR) — one residual double-listing.** Task 5 no longer creates `logins/mod.rs` or
  `logins/systemd_escape.rs`; Task 4 creates both (it needs the escaping rule for the jail types) and
  Task 5 modifies the `mod.rs`. Task 5 also gains `rules/rust.md` in its Modify list for the
  `ops/src/logins/model/` layout row, which Task 16 Step 5 verifies and which no task had been told to
  write.

Every file path, line number, field number and shipped sentence quoted in this second pass was
re-verified against the working tree on 2026-09-09, including the 18 `AccountSuspensionStateDto`
construction sites, the two `reserved 4` clauses, and `check-structure.sh`'s allow-list and family
exemption.

### Amended 2026-09-09 (third pass) — two facts measured on real hosts, and one that two tasks disagree about

Phase A (Tasks 1–3) has been **executed and proven on both families**, which is how these surfaced;
Tasks 4–16 are unexecuted. A working task-03 report's Step 8 raised three findings.
F1 (the jail base's mode) belongs to `installer/**` and its owner and is not touched here. The other
two are folded into the task bodies above, and the third item below is deliberately NOT closed.

- **F2 — the TLS version keys are a distro fact and this plan had the RHEL one.** Measured
  2026-09-09: the Debian family's vsftpd spells them `ssl_tlsv11` / `ssl_tlsv12` / `ssl_tlsv13`, the
  RHEL family's `ssl_tlsv1_1` / `ssl_tlsv1_2` / `ssl_tlsv1_3`; `ssl_tlsv1` is the same word on both.
  Task 6's template wrote the RHEL spelling, and on Ubuntu that is an unrecognised variable and
  vsftpd exits 2 **with no message at all** — the silent refusal Task 8's two-layer validation is
  built around, giving an operator nothing to read. The fact now sits where the plan's other
  per-family facts sit — `DistroAdapter`, measured, attributed to the host it came from — as a third
  method, `vsftpd_tls_version_keys()`, returning `VsftpdTlsVersionKeys` (three `&'static str`s, one
  per key the template writes; no `tls_v1_3` field, because the template writes no such key).
  **The method is added by Task 6, not by re-opening the executed Task 1**, and the argument is
  written inside Task 1 as an AMENDED AFTER EXECUTION note: a new step inside a finished task is an
  instruction nobody will run, and the failure would then appear as Task 6 rendering a config
  Ubuntu's daemon refuses silently. Task 1 keeps the **fact** (its evidence table gained the F2 row,
  and call-out 4 says why this difference — unlike `secure_chroot_dir` and `Type` — reaches the
  agent); Task 6 gets the **code**. Threaded through: the Global Constraints value table (the old
  `ssl_tlsv1_2=YES …` row is replaced by a per-family row and a family-invariant `ssl_sslv*` row);
  the file-structure tables (`adapter.rs` now says three methods and says which task adds which; a
  row for `distro/src/vsftpd_tls_version_keys.rs`; a fourth golden); Task 6's Files, Interfaces,
  template body, new Step 0, Steps 1–4; Task 8's `Consumes` (the single reader of the method);
  Task 10's real-daemon test; Task 16 Step 2 (both images, scored separately). Task 3's assertions
  were checked one by one and **name no TLS key**, so none of them carried the wrong spelling; only
  its Step 5 instruction did, and it now says to use the container's own family's spelling.
  The template's comment claiming *"There is no ssl_tlsv1_3 option"* was **false on both families**
  and is replaced: the option exists, is spelled two ways, and is deliberately left unset because
  both families were observed negotiating TLSv1.3 with it unset (Task 3 Step 5).
  **The goldens: one added, not three.** `vsftpd_rhel_tls_keys.conf` renders the otherwise-default
  config with the RHEL spelling, so the diff against `vsftpd.conf` is exactly the two lines the
  families disagree about (the 1.1 and 1.2 keys; `ssl_tlsv1` is the same word on both); a per-family
  pair of all three goldens would show that same two-line
  diff three times, against `rules/testing.md`'s point that the golden diff is the review artifact.
  A named test asserts the two renders differ in two lines **and no others**, so a later template
  change that makes the families diverge elsewhere fails by name instead of being absorbed.
- **F3 — a rendered config must be rewritten and never appended to.** ~~vsftpd's parser takes the
  first occurrence of a key~~ — **the premise this pass wrote here was BACKWARDS and is corrected by
  the fourth pass below: the parser takes the LAST occurrence.** The sentence is struck rather than
  deleted so a reader who met the third-pass text sees what changed. The rule it supports did not
  change; its reason inverted and its stakes rose. Everything else in this bullet still holds:
  every step of this plan was checked for an append, on a first
  run and on a re-run, and for two sources that could both emit a key. **Nothing appends, and that is
  stated plainly rather than fixed:** `vsftpd.conf` is written only through
  `ops::safe_write::render_validate_swap`, which writes the whole rendered text to a temporary file
  and renames it over the target (verified in `agent/crates/ops/src/safe_write/render_validate_swap.rs`
  — the same code path on the first apply and on every re-apply), and `/etc/maran/vsftpd/vsftpd.conf`
  has exactly one writer: the installer's step 89 creates the *directory* and never the file. The
  only place two sources emit one key is deliberate and already measured — the unit's
  `-obackground=NO` and the Task 8 validator's `-o…` flags, which are applied after the file is read
  and override it, which is what Task 8's own measurement table observes. (~~This pass called those
  flags an *exception* to the rule;~~ under the corrected ordering they are the ordinary case of it —
  applied after the file is read means last, and last wins. The fourth pass rewrote every place that
  named them as an exception.) The finding is recorded as
  a Global Constraints bullet, as a line in Task 6 where it was asked
  for, and as a strengthening of Task 8's status read-back, whose "check which listen pair the file
  contains" is only sound because nothing can have appended a second pair — ~~in front of~~ **after** —
  the real one, and which the fourth pass made a last-occurrence read for that reason.
  The installer's marker-delimited blocks are unaffected: they replace their block, they do not
  append, and they write no vsftpd configuration.
- **CONTESTED and deliberately left open: the `.wants` symlink under a mask.** Task 2 measured that
  the Debian `multi-user.target.wants/vsftpd.service` link is created *even with the mask in place*;
  Task 3 re-measured the same two images the same day and got the opposite, with a mechanism
  (`deb-systemd-helper` skipping the enable when it sees the `/dev/null` link) and a second
  observable (the `dsh-also` record). **No side is picked here.** Both measurements, both dated, are
  written into the CONTESTED note under Task 1's fact table, the fact-table row itself now says
  CONTESTED and "do not build anything on this row", and Task 2's mask paragraph carries a
  cross-reference so a reader arriving from either direction sees the dispute. The note says what
  would settle it — one transcript per Debian image covering all four cells of mask × image, with
  the `.wants` entry, the `dsh-also` record, the `init-system-helpers` version, and whether the mask
  existed BEFORE `apt-get install` ran, which is the most likely difference between the two runs —
  and records that a third measurement is being taken by the owner of `installer/`. Nothing in the
  plan rests on the row: on both readings the control is the mask symlink, which is what Task 2's
  step and Task 3's executed assertion both check, and Task 3's script prints the `.wants` state
  with `NOT THE CONTROL either way:` in front of it. When the third run lands, one edit closes the
  row, this bullet, Task 2's note and the *"mask or no mask"* sentence in
  `installer/lib/89-ftps.sh`'s comment block, which today states Task 2's reading as settled fact
  and is the only place in the repository where the disagreement is invisible.

Every path, file name, adapter method, crate dependency and shipped sentence quoted in this third
pass was verified against the working tree on 2026-09-09: `distro/src/adapter.rs` really does carry
`vsftpd_binary` and `ftps_group` and no third vsftpd method (so Task 1 is executed exactly as its
amendment note says), `agent/crates/templates/Cargo.toml` really does depend only on `askama` and
`thiserror` (so the key spelling has to arrive as plain values), `distro/src/family.rs` is the
precedent for a shared type at that crate's root, and `render_validate_swap.rs`/`write_config_set.rs`
really do write-then-rename rather than append.

### Amended 2026-09-09 (fourth pass) — the parser takes the LAST occurrence, and an earlier pass had it backwards

One correction, threaded everywhere it was relied on, plus the one thing that now needs checking
because of it. Nothing else in the plan is touched; Phase A (Tasks 1–3) stays executed and its
amended text says so in its own place.

- **The claim that was wrong.** The Global Constraints, Task 6 Step 4 and Task 8 all carried
  *"vsftpd's parser takes the first occurrence of a key and ignores every later one"*, taken in good
  faith by the third pass from finding F3 in a working task-03 report.
- **What measured it.** A working jail-mode report's Step 4b, 2026-09-09, with
  controls on both families. A duplicated `listen_port` — `2121` first, `2122` appended — bound
  **2122** (`ss` showing `0.0.0.0:2122`), with single-value controls binding 2121 and 2122
  respectively, so neither answer is a coincidence. A duplicated `force_local_logins_ssl`, driven with
  a plaintext `USER`/`PASS` and scored on the daemon's own reply, behaved as the **appended** value in
  both directions: `YES` then `NO` behaves like a lone `NO`, `NO` then `YES` like a lone `YES` — on
  `ubuntu:24.04` (3.0.5-0ubuntu3.1) and `almalinux:9` (3.0.5-8.el9). **vsftpd takes the LAST
  occurrence.**
- **The rule survives; its reason inverts and its stakes rise.** A rendered `vsftpd.conf` is still
  rewritten and never appended to. Under "first wins" that was hygiene, because an appended line was
  inert — a no-op that looks like a change. Under what is actually there, an appended
  `force_local_logins_ssl=NO` **silently turns off the mandatory TLS this feature exists to enforce**
  (spec §11, the owner's decision 2) on a config that still parses, still starts and still greets with
  `220`. That is the mechanism by which the product's promise could be lost with no error anywhere.
- **The `-o` flags stop being an exception.** The third pass recorded the unit's `-obackground=NO` and
  Task 8's validator flags as an *exception* to first-wins. Under last-wins they are not an exception,
  they are the ordinary case: applied after the file is read means set last, and last wins. Measured
  with its inverse control in the same report — file `background=YES` plus `-obackground=NO` keeps the
  watched process alive; the same file with no `-o` exits, which under `Type=simple` is a dead unit.
  Every place that called them an exception now says this instead, so nobody "fixes" a working
  override or, worse, learns a special case that does not exist and applies it to a second key.
- **Where it was corrected** (anchors verified before each replace, region re-read after): the Global
  Constraints rewrite-never-append bullet; the values table's `force_local_logins_ssl` /
  `force_local_data_ssl` row, which now names the two keys a single appended line can switch off;
  Task 6 Step 4's one-line F3 statement; Task 8's `FtpsState` read-back argument; a new stated blind
  spot in Task 8's two-layer validation section; Task 3's amended-after-execution note, because that
  task's report is where the wrong premise was raised and a reader who arrives there must not leave
  with it; and the third-pass amendment bullet, whose wrong sentence is **struck through rather than
  deleted** so a reader who met the earlier text sees what changed and why.
- **Task 8's read-back argument did not survive unchanged, and it is rewritten rather than patched.**
  The old argument was that under first-wins an appended second listen pair could make the read answer
  with whichever pair it found first "regardless of which one the daemon obeyed". Under last-wins the
  failure is worse-shaped: a read that matches the pair the template rendered would report the mode
  the panel *intended* while the daemon obeyed the pair appended after it — a check that agrees with
  the panel's own decision no matter what the daemon is doing, which is the blind gate
  `rules/testing.md` spends a section on. The conclusion survives, because nothing appends and the
  agent is the only writer; the rule the correction adds is that **every live-config read takes the
  LAST occurrence, never the first**, so where the file and the daemon could disagree the agent
  answers with what the daemon obeys.
- **What now needs a check it did not need before, and what deliberately does not.** Neither of Task
  8's validation layers can see an edit made after the swap — layer 1 validates the agent's own
  render, and layer 2's questions (unit active, `220` greeting) are answered perfectly by a daemon
  that has stopped forcing TLS. That gap was uninteresting while an appended line was inert and is
  not now. What closes it, in order of cheapness: (1) the last-occurrence read above, which costs one
  rule on a file the agent already opens; (2) **one new polygon test**,
  `the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen` (Task 10 Step 3),
  which reads the served file rather than a render, asserts every key appears exactly once, and —
  this is the part that makes it a check rather than a decoration — **plants the duplicate itself**
  and shows the plaintext login being accepted while it is planted, which is both the positive
  control `rules/testing.md` requires and the witness that the behaviour is real on a supported host
  and not only in a container transcript; (3) two rows in Task 10's mutation table, so the check is
  shown to die when it stops looking. The risk itself is written into "Known risks that are not open
  questions" with its decision: **detect and report, do not defend.** The file is root-owned under
  `/etc/maran` with exactly one writer and is replaced whole on every apply, so the threat is an
  operator, another tool or a restored backup — already root — and an immutable attribute, a watcher
  or a re-render-and-`cmp` on every status call would be machinery out of proportion to it. What is
  deliberately NOT added, and is named so nobody assumes it: an operator-facing indicator that the
  running daemon is not forcing TLS. That needs a ninth field on `GetFtpsStatusOk` and its three
  siblings, a DTO, a screen line and three locales — a design decision for the owner, and one worth
  making after the detection above has ever fired, not before.

Nothing in this pass changes a rendered template, a golden, a proto field number or a task's
checkbox state. The two new Task 10 mutation rows and the one new polygon test are the only additions
to a task's work; everything else is a sentence that was wrong and is now right, with the earlier
wording left visible where a reader could have learned it.

### Amended 2026-09-09 (fifth pass) — what execution proved this plan wrong about, task by task

Tasks 1–11 are executed. Executing them found statements in this document that the tree does not
have, and this pass corrects every one of them **against the tree rather than against a report**.
The distinction matters twice over: Tasks 12–16 are still unwritten, so a false statement here is a
defect waiting to be built (`rules/architecture.md`: a document describing what the code does not do
is a defect of the same severity as the behaviour); and Tasks 1–11's text is now a **record** as much
as an instruction, so every correction to an executed task says AMENDED AFTER EXECUTION inside the
task, says what shipped, and says that the code did not change — a reader must never come away
thinking a task shipped something it did not.

**Corrected in Tasks 1–11 (records — the code is right, this document was not):**

- **Task 9, the cascade order.** This plan stated the shipped order as
  `databases → sftp → pools → crontab → userdel`. It is
  `databases → sftp → crontab` (under the cron lock) `→ pools → require_existing → userdel`: the pool
  sweep was deliberately moved to be the LAST step before `userdel`, and a final `require_existing`
  was added. The insertion point this task named — directly after the SFTP teardown — was unaffected
  and is where the step went.
- **Task 9, the ordering test.** `FakeCascadeHosts::with_account(…).order()` does not exist and
  cannot: the cascade is driven by four separate host fakes with no shared clock, so a subsequence
  over one vector is unwritable. What shipped proves the order **causally** — make one step refuse
  and observe what did and did not happen, which is strictly stronger, because a step that ran after
  `userdel` could not have prevented `userdel`. Four named tests replace the one, and the plan now
  specifies the method rather than annotating the dead fixture.
- **Task 9, `set_ftps_password`.** The signature written here carried no account, so it could take
  neither the account lock, nor an ownership check, nor the shadow re-assert — and would have
  reintroduced for FTPS the suspension bypass repaired for SFTP the same day (`chpasswd` replaces the
  whole shadow field, clearing a suspension's `!`). The shipped entry point takes the account and
  does all three; the crate-private `…_under_lock` half exists only for `create_ftps_user`. **This is
  a requirement on Task 10's rpc, and it is written where Task 10 and everything downstream will
  read it.**
- **Task 9, the `FtpsHost` seam.** Task 8 shipped `FtpsHost` with daemon methods only and a `run`
  that takes no stdin. Every login-lifecycle method is Task 9's, so "modify `ftps_host.rs`" was most
  of that task's work rather than a touch-up, and the plan now says so.
- **Task 8, `AgentPaths::FTPS_LOG_PATH`.** It cannot live there: structure check 20 would demand it
  in the agent unit's `ReadWritePaths=`, and the agent never writes that file — vsftpd does, under
  its own unit. It is a documented private const beside `enable_ftps`. `VSFTPD_CONFIG_PATH` (under
  `/etc`, excluded) and `FTPS_UNIT` (not a path) stayed.
- **Task 8, `FtpsState` and `FtpsError`.** Seven state fields, four of them `Option` because a status
  taken while the daemon is off has no live file to read a range, a mode or a TLS answer out of and a
  fabricated `0`/`false` would be the blind gate this plan condemns; eight error variants, not six;
  four `model/` files, not three. And one field this plan did not know existed —
  **`forced_tls: Option<bool>`** — which makes the "detect and report" decision in Known Risks cost
  one additive wire field and its readers rather than a mechanism. That row now says so; the decision
  is unchanged and stays the owner's.
- **Task 5, `inspect_account_logins`.** Never created, and should not be: it would have been a second
  name for `account_logins` with an identical body. The RPCs kept their wire names; the ops function
  is `account_logins`. `LoginsError` has a fourth variant, `AccountBusy`, without which the moved
  write could not keep taking the account's lock.
- **Task 6 and Task 11, two spellings.** Render types spell it `render_config()`, not `render()`;
  `FirewallError::InvalidRange` is a unit variant and does not match `InvalidRange { .. }`.
- **Task 10, a line number.** `SftpLoginSuspensionFact` is not built at "lines 338–341" — it is at
  407 today, because Task 5's move and Task 9's cascade both edited that file. The seam is now named
  and not numbered; a line number in a file two lanes are editing is a phantom with a fuse on it.

**Corrected in Tasks 12–16 (instructions — these would have become defects):**

- **`backend/tests/Maran.Modules.Ftp.Tests/` is a folder holding one `.gitkeep`, not "the shipped
  unit-test skeleton project".** No `.csproj`, no `Maran.sln` entry, and no task created either — so
  Tasks 13 and 14 would have written a suite into a project that does not build and that
  `dotnet test Maran.sln` never collects, which `rules/testing.md` names as the most believable false
  pass there is. Task 13 now creates the csproj and registers both projects in the solution under one
  `Ftp` folder, the way the `Sftp` pair is registered.
- **Task 15 consumes a DTO member Task 13 did not produce.** The credential dialog is specified to
  render `ftpsStatus.hostname` — the owner's decision 1 rests on naming the panel host and never the
  customer's domain — and `FtpsStatusDto`'s field list held the agent's eight facts (**nine since the
  sixth pass corrected the count against the proto**) plus
  `ControlPort` and no hostname. The wire carries the hostname only as a request field and never
  echoes it, so the only possible source is the persisted `FtpsSettings.Hostname`; Task 13's Produces
  list now says so. This is the cheapest class of defect to fix in a plan and the most expensive to
  fix in code — a name produced differently from how a later task consumes it.
- **Four folders Tasks 13 and 14 file into are absent from the `Ftp` skeleton** —
  `Domain/Entities/`, `Domain/Policies/`, `Controllers/`, `Common/` — while the plan's "the skeleton
  holds folders only" reads as *all* of them. They are created by the task landing their first file,
  in the place the map already assigns; nothing is filed loosely and nothing new is invented.
- **Task 16 Step 5 asked for three corrections that are already made.** The sentence it quoted from
  `agent/crates/ops/src/sftp/mod.rs` does not exist there (nor do the words "FTP daemon"), and
  neither of the two `rules/rust.md` sentences it quoted exists either — the tasks that landed the
  folders reworded both files as they went. Left standing, that step would have sent its executor
  hunting for text to fix and, finding none, either inventing a change or reporting a false clean.
  What it still owes is named exactly: the `ftp`→`ftps` token in the ops area list, the empty
  `ops/src/ftp/` skeleton, the `vsftpd/user_config.rs` row changed rather than deleted, and — the one
  addition this pass makes to a task's work — **the last-occurrence rule for a live-config read
  written into `rules/rust.md`**, which Task 8's status read-back depends on and which today lives
  only in this plan and in two doc comments. The README and CHANGELOG sentences the step names ARE
  still there and are quoted verbatim now.

**Checked and found sound, so nobody re-checks them:** Task 14's two counts against the tree — 28
test files constructing `AccountSnapshot` across exactly the seven named projects (**30 sites with
the production ones: the fifth pass wrote 29 and there are TWO production sites, both in
`AccountDirectory.cs`, at its two projection lambdas — `grep -rn 'new AccountSnapshot' backend/src`
prints `AccountDirectory.cs:63` and `AccountDirectory.cs:113`, and
`git show HEAD:backend/src/Maran.Modules/Accounts/Services/AccountDirectory.cs | grep -c 'new AccountSnapshot'`
prints `2`, so this was wrong at the fifth pass and not moved by the work since**), and 18
`AccountSuspensionStateDto` sites, one production plus 17 across the four
named test files; the shipped `SftpLoginSuspensionFactDto(string Username, bool Locked)`,
`AccountSuspensionStateDto.SftpLogins`, `CronForeignLines`, `CanStillAuthenticate` answering only
about the account's own passwd entry, and the attestation sentence ending
`all {state.SftpLogins.Count} of its sftp logins locked`; `SensitiveString`,
`AgentSftpClient.CreateAsync` taking one, `AgentErrorTranslator`, `ISiteDirectory`,
`AuthorizationPolicies.AdminOnly`, `LicenceTier.Included`, `SftpManifest`'s shape,
`ContainerResolutionTests`, `SftpAuthorizationTests`; Task 15's `pages/sftp/SftpUsersPage.vue`, the
`/sftp-users` route, `moduleNavigationIcon.ts`, `stores/sftp.ts` and the three
`frontend/e2e/sftp/*.spec.ts` files; and Task 11's `NftablesAllow.port_to`, the
`ruleset_port_range.nft` golden and the three proto field numbers.

Every correction in this pass was made over a verified anchor and the region re-read afterwards; no
task's checkbox state, template, golden, proto field number or settled decision changed.
