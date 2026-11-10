# Server migration from a remote server (issue #197): threat note

`rules/security.md` requires a threat note, written first, for a change that touches the agent's
privileged paths and adds a new credential. This is the larger of the two migration notes: root-level
SSH from the root agent to a host the administrator names, parsing of an untrusted inventory, a stored
credential and a long-running parallel transfer engine. It depends on
`2026-11-07-account-move-threat-note.md` (#127), whose restricted dump loader, job-caller primitive and
rollback it reuses; read that note first. It is written BEFORE the code: every statement below is a
requirement the code must meet. Where the code lands and differs, the code is wrong or this note is
amended in the same change.

**Second review: OUTSTANDING (#331) - must not merge to main.** The work is carried out by agent
sessions, which cannot obtain a second reviewer. The branch carrying this change MUST NOT merge to `main`
until a human reviewer has read it and checked every item under "What a reviewer must check"
(rules/security.md "Sensitive change escalation"). Nothing here discharges the review. The owner is told
at hand-off that this surface carries an outstanding review.

The design comes from `research_notes/plan-127-197.md`, checked here against the tree as of 2026-11-07
(HEAD `05e58e81`, branch `feature/146-425-security`). **Every decision taken from the plan's recommended
option is listed under "Owner decisions this note assumes" and none is approved yet.** If the owner
chooses differently, this note is amended before the code.

## What is added

1. Agent service `TransferService` (`proto/agent/v1/transfer.proto`): generate a keypair, scan a host
   key, test a connection, read an inventory (closed adapters), pull one account's files, stream one
   database, cancel a transfer. A proto addition and a `PROTO_VERSION` bump (20 in the tree; the number
   is the next free one at merge time).
2. Outbound SSH from the ROOT agent to an administrator-registered host: the first network client the
   agent has. Spawned `ssh`, `ssh-keyscan`, `ssh-keygen` and `rsync` from the distro allow-list; no SSH
   library.
3. A registry of remote servers in the Migration module: a per-source ed25519 keypair (private key
   encrypted at rest) and a pinned host-key fingerprint.
4. A job engine: jobs, per-account transfers, concurrency, bandwidth, pause, resume, retry, cancel. The
   engine owns its state and MIRRORS progress into Tasks; it does not extend Tasks (a read-only journal
   with no cancel) into a scheduler.
5. A verify step that fetches each migrated site from the panel's own web server with a Host header, and
   a DNS helper that resolves the job's domains.
6. A new agent capability `Transfer` in `AgentCapability`, so the guard accounts for the door
   (rules/security.md item 13).

Not added: no daemon, no listening port, no root-password flow, no discovery or scanning of any network,
no write to a generic Linux source. A Maran source is driven through the pull of #127, so the same import
path and the same rollback apply.

Checked against the tree on 2026-11-07, none of this exists: no `transfer.proto`, no `agent/crates/ops/src/transfer`,
no `ssh`, `ssh-keyscan` or `rsync` allow-list entry in the distro crate (`file_transfer_facts.rs` names
only `vsftpd` and the sshd facts), no `Transfer` value in `AgentCapability`, no `Migration` module. The
agent today makes no outbound connection.

*Amended 2026-11-09:* that was true on 2026-11-07. The tree now holds phase 1 (see "Amendment: phase 1 as
built" at the end); the lines above describe what the note was written against and are kept as written.

## Rules this change touches, and what they say today

- **rules/security.md item 10 (surface).** "No new outbound calls ... requires a spec change first, not
  a PR." This adds an outbound SSH client in the ROOT process. The spec amendment and the item 10 /
  rules/architecture.md Agent section / rules/rust.md "Process execution" edits (allow-list gains
  `ssh`, `ssh-keyscan`, `rsync`) are drafts for the owner's approval and land BEFORE code
  (rules/README.md: rules change only with the owner's approval).
- **rules/security.md item 3 (shell).** "No shell strings anywhere ... argv arrays only ... no `sh -c`."
  `ssh` hands a command STRING to the remote shell; section 4 below is how this is reconciled and it is
  the part of the note a reviewer should spend longest on.
- **rules/security.md item 12 and rules/architecture.md (agent).** The command set is closed and typed;
  "MUST NOT add any RPC that executes caller-supplied programs, shell strings, or templates"; file
  operations on customer data run under the account's UID, never as root; distro facts live only in the
  `distro` crate behind `DistroAdapter`.
- **rules/architecture.md (agent).** "The agent MUST stay stateless" - the private key is therefore sent
  per call and never kept by the agent.

## 1. What an attacker could do with this surface, and why it is safe now

### 1. The source is hostile (or becomes hostile)

Everything the source sends is untrusted: the inventory, file names, file contents, symlinks, SQL. A
source that was honest when registered may be compromised later; the target does not rely on it.

- **Inventory** is parsed from FIXED read-only commands by parsers with bounds on bytes, lines, fields
  and name lengths. Every name that becomes a panel object passes the same validator as a hand-typed
  one; anything that does not validate is reported as a warning and skipped, never repaired. A
  malformed vhost is a warning, never a crash.
- **Files.** rsync's receiver runs as the account uid (`fork_as_account`), into an empty directory,
  with `--no-specials --no-devices --safe-links --no-hard-links`, `--max-size`, a total byte ceiling
  and a `--chmod` that clears setuid and setgid. A hostile symlink can only point somewhere the account
  could already point. `--safe-links` is a client-side promise about links inside the transfer; the
  descriptor-walk containment of rules/security.md item 2 is the control for the write, and the
  reviewer checks the receiver reaches it rather than a string check.
- **Databases.** The streamed remote `mysqldump` output is loaded through the restricted
  single-database loader of #127, never as the superuser. #127's open spike S1 therefore gates this
  issue too: if the restricted loader is not delivered, this issue has no database path.
- **Nothing from the source is executed on the target.** The target never runs a script supplied by the
  source.
- **Include chains.** A vhost file that includes other files is followed only for files inside the
  fixed directories the closed command set lists; the cost and the loop behaviour of this parser are
  not verified (see below).

### 2. The credential that opens the source

The private key can open a root shell on the source. It is generated on the target (per source,
ed25519), stored encrypted in PostgreSQL by `IEncryptionService`, never returned by any API (not even to
the administrator who added the source), never logged, and removed from the database when the source is
removed (the UI also tells the administrator to remove the public key from the source's
`authorized_keys`; the target cannot do that for them and says so).

- **Handing it to the agent.** The panel sends it per call (the same pattern as S3 credentials in
  `BackupDestination`); the agent writes it to a root-only file in a tmpfs directory of its own, mode
  0600, and removes it on every path including cancellation and a killed child. The agent keeps no copy.
- **The receiver problem.** The plan's default places the key in a 0700 directory owned by the ACCOUNT
  uid so an rsync receiver running as that uid can read it. That puts a root-capable credential in a
  directory a customer's process can read for the duration of the transfer. The alternative (a
  root-side bridge with a `fork_as_account` receiver over pipes) keeps the key out of the account's
  reach at the cost of more code. Which one lands is decision D18 and spike S2; **this note does not
  accept the default as safe**, and the reviewer treats it as an open finding until the spike result is
  attached.
- **No root password in v1** (D14). The operator pastes our public key on the source. "A password
  discarded after use" would be a password in a request body, the panel's memory and possibly a log;
  the safest storage is none.
- **The recommended `authorized_keys` line** (shown by the UI) restricts the key to the target's
  address and disables port, agent and X11 forwarding and a pty. It CANNOT restrict the key to read-only
  because the rsync server and the inventory commands need a shell; this is stated in the UI, not
  hidden. A leaked key is therefore a root key to the source from the target's address until removed.
- **A DTO census.** A test serialises every DTO and response of the registry and greps for the key
  bytes, with a positive control that finds a planted sentinel.

### 3. Host key pinning

First connect shows the fingerprint (SHA256 form), the administrator must confirm it as a dedicated
step, and it is stored. Every later connect uses `StrictHostKeyChecking=yes` with a known_hosts file the
agent writes from the stored key and a `UserKnownHostsFile` of its own; a changed key is refused with a
typed error and a message that names a possible interception. There is no "ignore host key" control
anywhere. `BatchMode=yes`, `IdentitiesOnly=yes`, `-F /dev/null` and a `ConnectTimeout` are set on every
invocation, so no user or system ssh configuration is read.

TOFU is stated as TOFU: the first confirmation is only as good as the channel the administrator compared
it over.

### 4. The remote command set is closed

`ssh` hands a command STRING to the remote shell, which is the thing rules/security.md item 3 forbids us
to build. The reconciliation is that the agent has a closed enum of remote operations, each a constant
string with typed holes whose values are validated types (`RemotePath`, `RemoteDatabaseName`,
`AccountName`). A hole accepts no character a shell treats specially: allow-list `[A-Za-z0-9._/@:-]`, no
leading `-`, no `..`. No rpc takes a command, a path template or a flag from a caller. The argv of every
LOCAL spawn is an array. This is a stated deviation from "no string-built commands" for the remote half
only; it is the single point where the rule is bent and it is why the item is escalated, not waved
through.

### 5. Outbound policy

The panel and the agent connect ONLY to hosts the administrator registered (and to the target panel's
own web server for verification). The host is validated as a DNS name or address; loopback, link-local
and the host's own addresses are refused. The SSH port is explicit. There is no discovery, no scanning
and no "try these ports". Unlike #127's pull, a private-range source is a normal case here; that is
stated, and it means an administrator can make the ROOT agent attempt an SSH connection to any internal
host. The attempt sends only an SSH handshake and, if the pinned key matches, our public key; it is
bounded by the registration (one host, one port, a confirmed fingerprint) and audited. Whether that is
acceptable is an owner question (below).

### 6. Parallelism and resource exhaustion

Concurrency is bounded (1 to 8, default 2), bandwidth limited with `--bwlimit` (changeable while running
by restarting rsync from the last good state, which it supports), disk checked per account before the
transfer (free-space floor plus the plan's quota), and a transfer that fills the disk stops with a typed
error and rolls its account back without touching the others. Pause is a signal to the transfer process
group, resume a continue, cancel a kill followed by reclaiming the partial state; each is idempotent.

### 7. Verification and DNS helper

The comparison fetches `http://127.0.0.1:<port>/` with `Host: <migrated domain>` against the panel's OWN
nginx only; the target address is fixed in code, never taken from the source or the inventory (no SSRF
primitive). The domain in the Host header has been validated as a domain. The DNS helper resolves names
with the system resolver, only for domains in the job (D19), and returns A, AAAA, NS and TTL as text; it
never connects to what it resolved. The summary export neutralises spreadsheet formulas with the shared
`CsvField` utility.

### 8. Cut-over writes to the source only on the administrator's explicit act, and only for a Maran source

(D15). A generic Linux source is never written to by Maran: the UI prints the commands to run. A Maran
source is suspended by the existing "Suspend account" on that panel, by its own administrator.

### 9. Per-account failure isolation and rollback

Each account is one durable step chain. A failed account is rolled back through the #127 rollback
(only the account the job created, `RolledBackIncomplete` if it cannot finish) and never blocks the
others. Job limits live on the job row and are re-read by the scheduler every tick. The job runs under
the #127 job-caller primitive, so a demoted or disabled initiator halts it, and messages carry ids only,
never the key.

### 10. Tenant boundary

Administrator-only (D6). The `RemoteServer` and job entities are administrator bookkeeping and not
tenant rows; the exemption in `TenantScopeTests` carries its reason. Customers and resellers get 404, an
API key is refused, view-as is `Refused`, and each new code is in the Sdk kind census.

## What a reviewer must check

1. No rpc takes a command, flag or path template; the remote command set is a closed enum; the typed
   holes accept only the allow-list; every local spawn is an argv array with a program from the distro
   allow-list; `ssh` is invoked with the pinned options of section 3 on every call.
2. A changed host key refuses with the typed error; there is no way to skip confirmation; the
   known_hosts file is the agent's own and is written from the stored key.
3. The private key appears in no DTO, log, response, audit line, error message, Wolverine envelope or
   database column other than the encrypted one; it is removed from the tmpfs on every path (a probe
   that finds a planted sentinel confirms the check can fail).
4. Where the key sits while rsync runs (D18 / spike S2), and that the choice does not leave a
   root-capable credential readable by the account's own processes.
5. A transfer cannot write outside the account home: descriptor-walk containment at the write, setuid
   and setgid cleared, specials and devices refused, byte ceilings enforced.
6. Dumps are loaded only through the restricted loader (the #127 review item 1 holds for this path
   too); there is no superuser fallback.
7. Inventory parsers are bounded and tolerant: hostile fixtures (huge line, NUL, deep include loop,
   malformed vhost) produce warnings, never a panic or an unbounded allocation; every name that becomes
   a panel object passes the ordinary validators.
8. Outbound reach: only registered hosts and the target's own loopback web server; the verification
   target is a constant; the DNS helper never connects.
9. Concurrency, bandwidth and disk limits hold; a full disk fails one account cleanly.
10. Rollback deletes only what the job created and reports an incomplete rollback; a demoted initiator
    halts the job.
11. Every new endpoint declares its permission, customers get 404, view-as is `Refused`, new error
    codes are in the kind census, and the new rpcs are classified in the agent audit lists with a
    stated cancellation class.
12. Both distro families: the `ssh`, `ssh-keyscan` and `rsync` binary paths come from `distro` for
    Debian AND RHEL (rules/architecture.md "Supported systems"), from each family's package file list,
    not from the developer's machine.

## What the author could not verify

- **No code exists and nothing was run.** This note is written before the change from the plan and a
  read of the current tree.
- **The rsync-as-account key handling** (D18, spike S2): where the key lives while a receiver running
  as the account uid reads it, and whether the root-side bridge is necessary.
- **`ssh` option behaviour** on OpenSSH 8.9 (Ubuntu 22.04), 9.2 (Debian 12) and 8.7 (AlmaLinux 9), and
  on Debian 13, Ubuntu 24.04 and the EL10 releases that rules/architecture.md lists with unverified
  cells: `StrictHostKeyChecking`, `UserKnownHostsFile` and `-F /dev/null` are standard, but the exact
  effect of `UpdateHostKeys` and how `ssh-keyscan` orders algorithms were not checked against each
  family's documentation.
- **The cost of parsing vhost files that include other files**, and whether a pathological include
  chain can be bounded without refusing legitimate setups.
- **Resumability of rsync after a bandwidth restart** with `--partial` on large files under the
  account's quota.
- That a restricted-loader dump stream from a remote `mysqldump` of a different server version loads on
  every target version (depends on #127 spike S1; version skew between source and target is a second
  variable).
- That the shared hot-spot files (`contract-baseline.txt`, `PROTO_VERSION`, `server.rs`,
  `AgentCapability.cs`, `ModuleRegistry.cs`, `navigationTree.ts`) merge cleanly with #127, #177 and #178;
  the working tree at writing carries uncommitted edits to several of them.

## Owner decisions this note assumes (plan's recommended option; owner NOT yet approved)

| # | Assumed |
|---|---|
| D1 | No rename on collision: a taken system user name is reported as a conflict (narrows #197's "rename on collision" to "report it"). |
| D6 | Administrators only. |
| D10 | One `Migration` module for both issues. |
| D13 | The job-caller primitive is accepted in SharedKernel and Host. |
| D14 | No root-password flow in v1; the operator pastes our public key. |
| D15 | A Maran source is suspended by its own administrator; a generic Linux source is never written to. |
| D16 | Mail is not migrated (no mail module until #177); the report warns "mail not migrated". |
| D17 | A Maran source authorises per-account pull grants listed on one screen of the source; a source-level scope is a later change. |
| D18 | Default: receiver as the account uid, key in a 0700 directory under the agent's tmpfs, removed on every path; the alternative is the root-side bridge. Decided by spike S2; see section 2, where this note declines to call the default safe. |
| D19 | The DNS helper uses the system resolver, only for domains in the job, documented as the one DNS-query exception. |
| D20 | Concurrency 1 to 8, default 2; bandwidth per job. |

*Amended 2026-11-09:* none of the rows is approved yet; the rows phase 1 applied from the recommended
option, and the ones it did not reach, are listed under "Amendment: phase 1 as built".

Also needing the owner before code: the spec amendment (outbound SSH to registered hosts, the agent's
first network client) and the edits to rules/security.md item 10, rules/architecture.md (Agent) and
rules/rust.md "Process execution".

## Open questions for the owner

1. Is the agent being a root SSH client acceptable at all, given it is the only root process and its
   whole design is "no outbound anything"? The alternative is to run the SSH and rsync in a panel-side
   helper, which breaks "the API spawns nothing"; neither is free.
2. D18: accept the account-uid receiver with a readable key, or require the root-side bridge?
3. May an administrator register a source on a private address (the ROOT agent then connects to it)?
4. Is the "authorized_keys cannot be read-only" limit acceptable to state and ship, or must a
   constrained forced command on the source be required?
5. Naming: `TransferService` / "transfer" collides with `2026-11-07-account-transfer-threat-note.md`
   (moving an account between resellers, #369) and `2026-09-09-transfer-login-deletion-threat-note.md`.
   Should the agent service and tasks be named for migration instead?

## Website docs needed (for the owner; agents do not touch website/ or GitLab)

- Operator guide "Migrate a whole server": preparing the source (the public-key line with the
  recommended restrictions and the statement that it cannot be read-only), host key confirmation,
  discovery and mapping, parallel transfers and the bandwidth limit, verification report, DNS helper and
  TTL advice (en, ru, hy).
- A security section: what is stored (an encrypted per-source key, a pinned fingerprint), what is never
  requested (a root password), and how to remove the key from the source afterwards.
- CHANGELOG entry text for #197.

## Amendment: phase 1 as built (2026-11-09)

This section is added to the note; nothing above was rewritten, except the two dated pointers. Where the
code differs from a statement above, this section is the current one. It was written by the same agent
session that wrote the code, so, as the note says of itself, it closes nothing.

### What phase 1 builds

The registry of remote servers and the read-only discovery of what a generic Linux source serves, and
nothing that moves data. Administrator only. In order of the issue:

1. **A key per source.** The agent generates an ed25519 pair (`ssh-keygen -t ed25519` into a root-only
   scratch directory, both halves read, the directory removed on every path) and hands both halves back to
   the panel once over its local socket. The panel keeps the private half encrypted at rest and shows the
   administrator the public key as an `authorized_keys` line. The agent keeps neither half.
2. **A host key the administrator confirms.** `ssh-keyscan` reads the keys the source offers; the agent
   computes each SHA256 fingerprint itself from the decoded key (no extra spawn). The administrator picks one and
   retypes it as a step of its own; the backend compares it, case-sensitive, to the stored candidates and
   pins it. Re-scanning replaces the candidates and never pins.
3. **A connection test.** Through the pinned channel the agent runs the constant probes (effective user,
   the filesystem that holds `/home`, os-release, database server, web server, mail store, Maran version)
   and answers with typed facts. A changed host key is a typed failure that flips the server to
   `HostKeyChanged`; so are a refused key, an unreachable host and a refused address.
4. **A read-only inventory.** Accounts, their sites (nginx and Apache), databases, cron entry counts, mailbox
   counts and disk usage, streamed account by account and stored by the panel as discovered rows. Mail is
   counted, never read, and an account whose mail is found carries the warning `MAIL_NOT_MIGRATED` (D16).

Where it lives:

- Agent: `proto/agent/v1/transfer.proto` (the four rpcs and their messages; `PROTO_VERSION` 24);
  `agent/crates/ops/src/transfer/` (the connection half, the closed `RemoteCommand` enum, `ssh_argv.rs`,
  `choose_source_address.rs`, `write_known_hosts.rs`) and `transfer/inventory/` (the parsers of untrusted
  text); `agent/crates/agent/src/services/transfer/` (the four rpcs, all in the audit list of mutating rpcs,
  `ReadSourceInventory` with cancellation class "stop"); the validated types `RemoteHost`, `HostKey`,
  `HostKeyFingerprint`, `SourceUserName`, `SourcePrivateKey` and `RemotePath` in `agent-core`;
  `AgentPaths::TRANSFER_SCRATCH_DIRECTORY` (`/run/maran/transfer-scratch`); `CallClass::SourceRead` (900 s);
  `ssh`, `ssh-keyscan` and `ssh-keygen` on the distro allow-list for both families.
- Panel: the `ServerMigration` module (schema `servermigration`, administrator only, capability `Transfer`; named `Migration` when this was written) and
  `Maran.Agent.Client`'s `TransferService` folder with `ResilientAgentTransferClient`; REST under
  `api/v1/remote-servers`; the SPA pages under `/migration/remote-servers`.
- Installer: the OpenSSH client package on both families (`openssh-client` on the Debian family,
  `openssh` and `openssh-clients` on the RHEL family), proved present by `assert_agent_tooling`. The agent
  unit needed no new directory: the scratch directory is a child of `/run/maran`, which `RuntimeDirectory=maran`
  creates and `ReadWritePaths=` already lists, and `RestrictAddressFamilies=` already admits `AF_INET` and
  `AF_INET6`.

### Owner decisions applied

Every one is the plan's recommended option and **none is approved**. If the owner chooses otherwise, this
note and the code are amended together.

| # | Applied in phase 1 |
|---|---|
| D1 | No rename: a taken name is a conflict to be reported by the dry run, which is not built yet. Phase 1 stores names as discovered. |
| D6 | Administrators only: customers and resellers get 404, API keys are refused, view-as is `Refused`. |
| D10 | One `Migration` module. |
| D14 | No root-password flow. The operator pastes our public key; the UI gives the line and says the key cannot be limited to read-only. |
| D15 | Never a write to a generic source. Phase 1 writes nothing to any source. |
| D16 | Mail is not migrated: the inventory counts mailboxes and warns. |
| D18 | **Not reached.** There is no rsync and no receiver in phase 1, so the question of where the key sits while a receiver runs as the account uid does not arise yet. Section 2 still stands: this note does not call the default safe, and the question returns with the transfer engine. |
| D19 | **Deferred.** The DNS helper is not built. |
| D17, D20 | Deferred with the Maran adapter and the transfer engine. |

Resolved by this build: the key is generated on the target and the private half never leaves the panel's
database except per call to the agent; there is no root password anywhere; the three binaries are
allow-listed per family from `distro`. Registering a source on a **private address is allowed and stated**
(open question 3): the policy refuses loopback, link-local, unspecified, multicast and this host's own
addresses, judged on the resolved address, and nothing else.

### The agent resolves the registered host's name

A registered host given as a DNS name is resolved by the agent, with the system resolver, once per call, and
the connection is made to the resolved literal under a fixed `HostKeyAlias` and never to the name again. That
resolution is an outbound DNS query made by the root agent to the host's configured resolvers, bounded by
the resolver's own timeouts and not by the agent. It is made only for the one host an administrator
registered, on an administrator's act. A name that resolves to even one refused address is refused whole.
The query tells the resolver which name the administrator registered; that is the cost of accepting names
and not only literals, and the alternative (literals only) was not chosen.

### What the review must additionally check

13. Every `ssh` run carries the pinned options of section 3 through `ssh_argv.rs`, which is the only
    builder; the key file is `0600` in a `0700` root directory that the operation checks (owner and mode)
    before it writes, and `TransferScratch` removes it on every path including a failed spawn.
14. The private key reaches no log, audit line, error value, DTO or response. A `SourcePrivateKey` is a
    `SecretString`; the panel's `AgentSourceCredential` prints `[redacted]`; a test serialises every DTO of
    the registry against a planted sentinel with a positive control.
15. `RemoteCommand` has 17 variants, every one read-only, every hole a `RemotePath` or `SourceUserName`; the
    grammar of a hole excludes every shell metacharacter, and each hole is also single-quoted.
16. The inventory parsers are bounded (accounts 5000, 1 MiB and 20 000 lines per output, line length,
    include depth 4 with a visited set); the hostile fixtures are in
    `agent/crates/ops/tests/fixtures/transfer/synthetic_inventory/`. They are **hand-written, not captured**:
    no source host was available, so no parser has met the output of a real nginx, Apache or MariaDB.
17. Every name that becomes a discovered row passed the ordinary validator; one that did not is a
    `NAME_REFUSED` warning and is skipped.
18. The address policy runs on the resolved address, fails closed when this host's own addresses cannot be
    listed, and the connection is to the literal.
19. The `Transfer` capability is declared by `Migration` and by no other module (item 13 of
    rules/security.md).

### What phase 1 does not build, and why

Everything below depends on the #127 BACKEND, which is not in the tree. Only the #127 agent half is
(`ExportBackup` and `ImportBackup` in `transfer.proto`); the Migration importers, the per-account grants, the
job-caller primitive and the restricted-loader import path are absent. Nothing here is stubbed or faked,
and no dead code stands in for it:

- mapping, plan, IP assignment and dry run;
- the parallel two-phase transfer engine with rsync, streamed dumps, pause, resume, retry and cancel, and its
  concurrency and bandwidth limits (D20);
- the verification and comparison report, the DNS helper (D19) and cut-over;
- the Maran adapter (a source that is a Maran panel, D17) and the polygon source container.

Sections 1, 6, 7, 8 and 9 of this note describe that deferred work and remain requirements for it. Section 1
applies to phase 1 as to the inventory only. The phase 1 build touches no customer file and no database.

### What the author of this amendment could not verify

- **Nothing was built or run when this was written.** The code was produced under a coding phase that forbids
  builds, tests and lints; the lead's gate runs them. The statements above come from reading the files, and this
  line is amended with the gate's result.
- `ssh` and `ssh-keyscan` option behaviour (`HostKeyAlgorithms` with an RSA key through its SHA-2 signature
  names, `UpdateHostKeys=no`, `ClearAllForwardings`, `-n -T`) on OpenSSH 8.7, 8.9, 9.2 and the releases the
  matrix lists with unverified cells; and whether `ssh-keyscan` offers all three algorithms on each.
- The RHEL and SUSE package names (`openssh-clients` and `openssh` for the RHEL family, which was read from
  each package's file list and not installed here; the SUSE names `openssh-clients` and `openssh-common`
  are a guess marked UNVERIFIED in the installer).
- How the system resolver behaves under the agent unit's hardening (`getaddrinfo` through
  `RestrictAddressFamilies=` and the system-call filter) on each family.
- That the parsers' tolerance matches real vhost files: nothing was captured from a real source.

### Open questions for the owner, unchanged and still open

The five questions above are unanswered; phase 1 proceeds on the recommended answer to 3 (private addresses
allowed), 4 (the limit is stated, not engineered away) and 1 (the root agent is the SSH client), and the
naming of question 5 stayed `TransferService`. Each is a decision for the owner, and the code changes with
his answer.

## Amendment: the #127 backend half is now in the tree (2026-11-09)

Written by the same agent session that wrote the code; it closes nothing and nothing was built or run.

- **The module was renamed `ServerMigration`** (namespace, folder, schema `servermigration`, manifest id
  `servermigration`) because the old name shadowed EF Core's `Migration` base class. Every statement above
  that says `Migration` module means this module.
- **What phase 2 can now use**, all described in `2026-11-07-account-move-threat-note.md` under "the backend
  half as built": the per-module `IAccountMigrationExporter`/`IAccountMigrationImporter` pairs (Accounts, Sites,
  Databases, Sftp, Ftp, Cron) with their ordering contract; the job-caller primitive (`IJobCallerScopes`,
  rebuilt from the user row on every step); the import path through the agent's restricted loader (the
  artifact is marked imported by `ImportBackup`, and `IMigrationArtifactService.RestoreImportedAsync` restores
  it); and the rollback that refuses an account older than the job (`IAccountMigrationRollback`).
- **Still absent, and still this note's requirements for phase 2**: the pull endpoint and per-account grant
  transport (grants exist as rows and can be issued and revoked, but no route accepts one), the transfer engine
  (rsync, streamed dumps, pause, resume, retry, cancel), the verification report, the DNS helper, the Maran
  adapter and the polygon source container.
- **Review item 6 above** (dumps load only through the restricted loader, no superuser fallback) now has a
  panel-side half to read: `MigrationArtifactService` records the artifact as imported-by-the-agent and sends the
  ordinary restore request; it passes nothing that selects a loader.

## Amendment: phase 2 as built, the transfer engine (2026-11-09)

Written by the same agent session that wrote the code; it closes nothing, and nothing was built or run. The
sections above remain the requirements; this one says what the code does about them.

### What phase 2 builds

Agent (`PROTO_VERSION` 26): two rpcs on `TransferService`, both audited as mutating.

- `PullAccountFiles` (server-streaming): `rsync` over the pinned SSH channel into one account's home, in
  time-sliced runs. Initial phase never deletes; final phase (`--delete`) is the delta at cut-over.
- `StreamRemoteDatabase` (server-streaming): the remote `mysqldump` (variant 18 of the closed `RemoteCommand`
  enum, hole `RemoteDatabaseName`) is written to a root-only file on the disk, bounded in bytes and time, and
  loaded through the existing restricted loader (`ops::backup::load_dump_restricted`, composed in the service
  crate because areas never import each other). A dump the scanner refuses is refused; there is no superuser
  fallback.

Panel: `MigrationJob` and `MigrationJobAccount` (mapping, dry run, parallel two-phase engine, pause, resume,
retry, cancel, limits, cut-over, comparison report, DNS helper, summary file) and the SPA under
`/migration/jobs`. A source that is a Maran panel is moved by the #127 package path instead (pull, import, same
rollback).

### Owner decisions applied (recommended option; none approved)

| # | Applied in phase 2 |
|---|---|
| D1 | No rename: a taken name, a taken domain and a database that cannot keep its name are conflicts of the dry run. |
| D15 | Nothing is written to a generic source: `PullAccountFiles` is a read, and the closed command set gained one more read. |
| D16 | Mail stays unmigrated; the dry run warns. Cron entries are only counted by discovery, so they are warned about, not carried. |
| D17 | A Maran source uses one pull grant per account, entered per account on the job screen. |
| D18 (a) | The receiver runs as the account's uid; the key sits in a `0700` directory owned by that uid under `TRANSFER_RECEIVER_DIRECTORY`, removed on every path. |
| D19 | The DNS helper resolves only the domains of the job, with the system resolver, A and AAAA only. |
| D20 | Concurrency 1 to 8, default 2, bandwidth per job, both re-read while running. |

### How D18 (a) was reconciled with `fork_as_account`

`fork_as_account` kills its child after two minutes, and a copy takes hours. `rsync` therefore runs in slices of
100 seconds: each slice is one `fork_as_account_for_bytes` whose child runs `rsync` through `run_unthreaded`
under a 100 second deadline and brings back at most two bytes (ended with status N, or reached its deadline). A
killed slice keeps its partial files (`--partial`) and the next slice continues; a changed bandwidth limit, a
pause and a cancel all take effect at a slice boundary. The pull is cancellation class "stop" with the partial
state kept on purpose.

### Findings a reviewer must weigh (additional review items)

20. **The key is readable by the account's own processes for the length of a slice.** It is in a directory the
    account owns. The account is newly created and serves nothing before the cut-over, but a hostile source's
    files, once copied, are the account's own and could include a process the account starts; the note's earlier
    statement that the default is not safe still stands, and this is the stated cost of choosing it.
21. **`rsync` builds the remote command itself.** The closed enum covers every command the agent composes; the
    `rsync --server --sender ...` line is composed by `rsync` from fixed flags, the validated `RemotePath` and
    the `--rsh` transport (`rsync_transport`, built from `ssh_client_options`, the same pinned options as every
    other call, minus `-n`). It is the one remote command string this agent does not render itself.
22. **Remote-side `rsync` must exist on the source.** A missing one is a failing status, reported as a pull
    failure; the agent cannot tell it apart from other failures because it reads no `rsync` output.
23. **Failure classification of a pull is by exit status only.** Status 11 is reported as "no room"; 24
    (vanished files) is success. The connection reasons (changed host key, refused key, unreachable) come from
    the read-only size probe that runs first over the same pinned channel, not from `rsync`.
24. **The dump file lives on disk** (`BULK_SCRATCH_ROOT/transfer-dumps`, `0600`, root-only chain), bounded by
    the request and the free-space floor, removed on every path; a stalled agent restart leaves it for the
    unit's scratch emptying.
25. **Restricted loader fidelity.** A source database with triggers, routines, events or views is refused
    (`MigrationDumpRefused`); the dump command passes `--skip-triggers` so triggers do not make every database
    fail, but views and routines still do. This is the plan's D4 consequence.
26. **Verification is limited and says so.** The report states that the final `rsync` converged and what the
    source home measured; it holds no second measurement of the home on this server. Sites are fetched from
    the panel's own web server on the loopback address with the domain in the `Host` header (the address is a
    constant, the DNS of the domain is never consulted).
27. **Pause and cancel are stoppable only at boundaries.** The importers (several modules creating one
    account) run to completion once started, so a pause takes effect after them; a half-created account
    (a restart in the middle) is rolled back and started over, not resumed.
28. **Concurrency gate is process-local.** The scheduler's gate and the registry of workers are in memory; an
    account the database calls running with no worker (the panel restarted) is recovered by pause then resume.

### What the author could not verify

- Nothing was built or run: not the agent, not the panel, not the SPA, not the polygon image.
- That `/run/maran-host` is traversable by an account's uid inside the agent's mount namespace on every family
  (read from the unit: `BindPaths=` makes the mount point `0755`), and that `CAP_CHOWN` suffices for the hand-over.
- `rsync`'s handling of `--chmod=a-s,a-t`, `--safe-links` and `--max-size=64G` across the versions the matrix
  ships (3.1 to 3.4), and that its `--rsh` splitting accepts the generated option list.
- That `mysqldump` and `mariadb-dump` on every source accept `--skip-triggers --hex-blob
  --default-character-set=utf8mb4`.
- The EF migration, its designer and the model snapshot were written by hand.
- Website documentation is the owner's to write.

Second review: OUTSTANDING (#331)
