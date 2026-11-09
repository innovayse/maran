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
