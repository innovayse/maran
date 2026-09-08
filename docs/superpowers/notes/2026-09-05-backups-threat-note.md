# Threat note — backups (plan 6): archiving, the root-only scratch, restore, and the object store

rules/security.md: "Changes to auth, session/token handling, the agent's privs module, license
verification, or the installer's privileged steps require a second reviewer and an explicit threat
note in the PR description: what could an attacker do with this surface, and why is it safe now."

**The second-reviewer requirement is OUTSTANDING.** No second reviewer has looked at any part of
the backups work. This note is the written half only, and a note by the author of a change closes
nothing — it buys the reviewer an argument to read instead of a diff.

**This note is LATE, and that matters.** rules/security.md, as amended today, requires the note to
be written **first**, before the change, precisely because a note written afterwards is a
justification for a decision already taken rather than a check on it. The backups work is landed in
the working tree; this note was written on 2026-09-07 against code that already exists. The reader
should treat every "why it is safe" section below as a claim tested after the fact, and should give
the measured paragraphs more weight than the argued ones. Writing it late is also how the finding
in §3 was found at all — which is an argument for measurement, not an argument for late notes.

Scope: `agent/crates/ops/src/backup/**`, `agent/crates/agent-core/src/agent_paths.rs`
(`BULK_SCRATCH_ROOT`), `agent/crates/agent/src/services/backup/**`,
`installer/systemd/maran-agent.service`, `installer/lib/40-user.sh` (which is also where
`/var/backups/maran` is created — there is no `89-backups.sh` in the tree; the plan's Task 15 step
has not landed), `backend/src/Maran.Modules/Backups/**`.

Measurement environment: this workstation has no root. Everything with real uids in it was measured
in a `ubuntu:24.04` container as root, driving the agent's *actual* directory-creation code
(a small Rust binary reproducing `open_scratch`'s `DirBuilder::new().recursive(true).mode(0o700)`
under `UMask=0027`), and the real `tar` with the product's real flag list. Kernel:
`fs.protected_symlinks=1`, `fs.protected_hardlinks=1`. Anything not measured is labelled UNPROVEN
and says what would prove it.

---

## 1. FINDING (HIGH, new, measured) — the panel uid can reach the plaintext dumps, and can make the root daemon write anywhere

This is the item the cross-cutting review asked to have stated in one place. The review's belief was
that the panel user cannot read the dumps. **Measured: that belief is true of the modes, and false
as a security boundary.**

### The four facts, together

1. `installer/lib/40-user.sh:28` — `install -d -o panel -g panel -m 0750 /var/lib/maran`. The
   directory is **owned by the unprivileged `panel` uid**, which is what `maran-api` runs as.
2. `AgentPaths::BULK_SCRATCH_ROOT` = `/var/lib/maran/scratch` (agent_paths.rs:104), i.e. *inside*
   that panel-owned directory. `backup_scratch_dir(id)` = `/var/lib/maran/scratch/backup/<id>`.
3. `create_backup.rs:open_scratch` does `let _ = remove_dir_all(scratch)` on the **leaf** only, then
   `DirBuilder::new().recursive(true).mode(0o700).create(<scratch>/databases)`.
4. `installer/systemd/maran-agent.service:214` sets `UMask=0027`; line 38 sets
   `ExecStartPre=-/bin/rm -rf /var/lib/maran/scratch`, which runs **once, at daemon start**.

### What was measured

Case A — pristine chain, the daemon creates everything:

```
drwxr-x--- panel:panel /var/lib/maran
drwx------ root:root   /var/lib/maran/scratch
drwx------ root:root   /var/lib/maran/scratch/backup
drwx------ root:root   /var/lib/maran/scratch/backup/b1
drwx------ root:root   /var/lib/maran/scratch/backup/b1/databases
-rw-r----- root:root   .../databases/shop.sql
panel: cat -> Permission denied (rc=1);  ls scratch -> Permission denied (rc=2)
customer uid 5001: cat -> Permission denied (rc=1)
```

So on a chain the daemon built end to end, **the reviewer's belief holds**: `0700` at every level
under `scratch`, the dump itself `0640 root:root` (`0666 & ~0027`), and neither the panel uid nor a
customer uid can read it. `DirBuilder`'s `mode` does apply to intermediates it creates — measured,
not assumed.

But `panel` owns the *parent*, and nothing checks the ancestors:

- Case B — `panel` plants a symlink at the `scratch` name before the daemon uses it: **root follows
  it** and creates the tree wherever it points. `create_dir_all` does not check for symlinks. The
  dumps stayed unreadable only because the directories root created inside it are still `0700`.
- Case C — `panel` pre-creates `/var/lib/maran/scratch` as a real directory it owns, mode `0777`:
  root leaves that mode alone (`create_dir_all` never corrects an existing directory) and carries
  on. Dumps still unreadable, again by the leaf mode alone.
- **Case E — the one that works.** `panel` owns `scratch/` and `scratch/backup/`. The daemon runs
  `open_scratch` (creates `backup/b1` and `backup/b1/databases`, `0700 root`). `panel` then
  `mv`s `b1` aside and puts a symlink to a directory it owns in its place — which needs write
  permission on `scratch/backup` **only**, and it owns that. The next dump write follows the
  symlink. Measured output:

  ```
  -rw-rw-rw- 1 panel panel 43 /var/lib/maran/loot/databases/shop.sql
  panel reads it: "-- INSERT INTO users VALUES('secret-row');"   rc=0
  ```

  The `0700` on `b1` never mattered: the attacker never had to enter it, only to rename it.
- **Case F — worse.** Same setup, with the dump's own filename a symlink to `/root/victim.txt`
  (`0600 root:root`). Measured: root's dump write **truncated and overwrote the root-owned file**.
  Generalises to any path on the host, with content the attacker partly controls (the panel uid can
  create databases and rows). That is a panel → root escalation, the same shape as the installer
  artifact-staging escalation found today.

`fs.protected_symlinks` does not help: it only covers symlinks in world-writable **sticky**
directories, and none of these are sticky.

The `.partial` artifact path is *not* reachable this way — it lives under `/var/backups/maran`
(`root:root 0700`, outside `/var/lib/maran`), is created `create_new` with an explicit mode, and
`publish()` re-`stat`s the finished inode for uid, mode and `nlink` before the rename. That part is
right, and it is the discipline the scratch does not have.

**Attacker:** the `panel` uid — i.e. any RCE or file-write in the C# API, the largest attack surface
the product exposes. Not a customer uid: customers cannot write `/var/lib/maran` (`0750
panel:panel`, measured EACCES for uid 5001).
**What stops it today:** nothing. The `ExecStartPre` `rm -rf` runs at daemon start only; the
attacker plants after it.
**What is merely believed to stop it:** the `0700` modes. They are real and they are not a boundary
when the attacker owns an ancestor.

### The precise change this needs (agent/ and installer/ are not writable in this session)

Either of these closes it; the first alone is enough and is the smaller diff.

- **Move the bulk scratch out of the panel-owned tree.** Change
  `AgentPaths::BULK_SCRATCH_ROOT` from `/var/lib/maran/scratch` to **`/var/lib/maran-scratch`** —
  directly under `/var/lib` (`root:root 0755`), the same remedy applied to the installer's artifact
  staging today. Update `installer/systemd/maran-agent.service`'s `ExecStartPre=` and its comment
  block, `installer/lib/40-user.sh` (create it `root:root 0700` with `install -d`), the uninstaller,
  and `BULK_SCRATCH_ROOT`'s doc comment and tests. No unprivileged uid can then place an entry at
  that name at all.
- **And/or give `open_scratch` the discipline `publish()` already has.** Before use, `remove_dir_all`
  the whole `BULK_SCRATCH_ROOT` and re-create it with `install`-equivalent semantics, then
  `symlink_metadata()` each level of the chain it is about to write into and **refuse the operation**
  (`BackupError::ScratchUnusable`) unless every level is a real directory, `uid == 0`, and
  `mode & 0o777 == 0o700`. A check that fires is worth more than a mode that is assumed: the
  installer audit's own lesson today.
- Either way, add a test in the same shape as the installer's: plant a symlink at the scratch name,
  run the operation, assert it refuses — plus the inverse control that a clean chain is accepted.

---

## 2. The scratch moved from tmpfs to disk today — what that bought and what it cost

`/run/maran/scratch` (tmpfs) → `/var/lib/maran/scratch` (disk), with
`ExecStartPre=-/bin/rm -rf /var/lib/maran/scratch` standing in for what a reboot used to do for
free. The scratch holds **full plaintext copies of every database in the account** for the duration
of a backup, and on restore it holds the archive's dumps *and* the pre-drop rollback dumps at once
(`scratch-ceiling-report.md` measured the peak: `Σd_i` on create, `Σd_i + Σr_i` on restore, with no
per-database cleanup anywhere).

**What it bought, and the reason is sound.** `/run` is 10% of RAM (measured: 1.5 GiB on a 16 GiB
host). The 8 GiB per-dump ceiling is enforced *after* the client has finished writing, so on tmpfs
it protected nothing: the process hit `ENOSPC` or the OOM path long before the check. At no
realistic server size did a single dump at the ceiling fit. Staging a customer's database in kernel
memory on a root daemon was the wrong currency.

**What it cost, stated plainly:**

- **Persistence.** Plaintext dumps now survive a crash, a power cut and a reboot, on disk, until the
  daemon next starts. The tmpfs cleared itself for free and with no code; the disk clears only if
  `ExecStartPre` runs. An operator who stops the agent and does not start it, or who takes a disk
  image / snapshot / filesystem-level backup of the host in that window, has customer databases in
  the clear in that image. The tmpfs version could not produce that artefact.
- **A new neighbourhood.** On tmpfs the parent was `/run/maran`, `root:root`, recreated by the
  unit's `RuntimeDirectory=`. On disk the parent is panel-owned. **The escalation in §1 did not
  exist before this move.** That is the honest accounting: the move fixed a real capacity defect and
  introduced a privilege boundary defect, and both were introduced the same day.
- **Disk exhaustion is now a disk problem** — better than an OOM, and still an unbounded one: the
  8 GiB ceiling is per-dump and post-hoc, `N` databases are all resident at once, and the restore
  side does not bound the archive's dumps at all (only the compressed archive, at 64 GiB, which
  decompresses to an unbounded multiple). A hostile or merely large account can fill the filesystem
  holding `/var/lib`, which is the filesystem the panel's own database and logs live on.

UNPROVEN: that `ExecStartPre=-/bin/rm -rf` actually runs and clears the tree on a booted host of
each family. It is a `-`-prefixed line, so a failure is ignored *silently* — the one shape today's
installer audit found lying twice. Proof: on a real Ubuntu 24.04 and AlmaLinux 9 host, plant a file
in the scratch, `systemctl restart maran-agent`, and observe it gone.

---

## 3. `tar` over a hostile home — measured, with real flags

Creation runs `tar` **as root**, entered into the account's home. Restore runs a second `tar` inside
`fork_as_account`, at the customer's uid, into a staging directory. Measured against the product's
exact argv (`--create --use-compress-program=/usr/bin/gzip --numeric-owner --one-file-system
--sparse --transform 's|^\.|home|S'`):

| Hostile thing in the home | Measured result |
|---|---|
| `etclink -> /etc` | Stored **as a symlink**. Not followed, `/etc` not archived. `-h`/`--dereference` is absent and `ArchiveSpec` has no field that could set it. Holds. |
| A fifo (`blocker`) | `tar rc=0`, no hang. Stored as a fifo's metadata; tar never opens it. The "a fifo blocks the reader forever" worry is **not real for `tar --create`**. |
| Hard link to a root-owned file | The customer **could not create it**: `ln` → "Operation not permitted" (`fs.protected_hardlinks=1`). But where such a link *does* exist, tar archives the **content** — measured: `/etc/pseudoshadow`'s bytes came out of the archive. So the defence here is a kernel sysctl, not anything this product does. |
| Device node (`c 1 3`) | Archived as a node on create. On restore **as the account** the whole extraction fails: `tar: dev: Cannot mknod: Operation not permitted`, `rc=2`. |
| 4 GiB sparse file | Archive stayed 2517 bytes (`--sparse` works). `measure_home_bytes` counts apparent size, so the manifest's `home_bytes` reads 4 GiB. |

Three things follow:

- **`fork_as_account` on the restore side does exactly what it is claimed to do on the write side**
  and nothing on the read side (R3, plan 5's Ruling 15): the account's own uid writes the files, so
  a hostile archive can only write where the account already could. It does **not** stop the account
  reading what it is handed — hence the dumps being extracted root-side into the root-only scratch.
- **A device node makes an account's own backups unrestorable, permanently and silently.** The
  create side accepts it; the failure appears only at restore time, when the customer needs the
  backup. It fails *before* the first `DROP DATABASE`, so the account is left intact — the failure
  is safe, and it is still a defect: the customer's backups were never restorable and nobody was
  told. **Owner decision / suggested change:** refuse or skip non-regular, non-symlink members at
  create time (`--exclude` by type is not a tar flag; the honest fix is a pre-walk that refuses, or
  documenting it), and either way state it in the panel.
- **Hard links are defended by `fs.protected_hardlinks`, which the product neither sets nor
  checks.** UNPROVEN: that it is `1` on every supported host. It is the distribution default on both
  families, and an operator can turn it off. Proof: assert `fs.protected_hardlinks=1` in the
  installer's preflight (and say so), or accept it as a documented host requirement.

Restore's `tar` also carries `--no-same-owner`, `--no-same-permissions`, no `--absolute-names`, and
`--strip-components=1` rather than a transform, and `scan_members` refuses the whole archive — not
the member — for anything outside `manifest.json`, `home`, `databases`, `home/…`, `databases/…`.
A `../` member and an absolute member both fail that rule; the refused name is escaped before it
enters an error (rules/security.md item 4's mechanism, applied to a log line). I read these; I did
**not** run a `..`-carrying archive against the real operation. UNPROVEN by me; the unit tests in
`scan_members_tests.rs` assert it and were run (`cargo test --workspace`: 1374 passed / 0 failed).

---

## 4. Restore is the dangerous direction

**Attacker A: someone who can supply the artifact.** Today nobody can: artifacts are written by root
into `/var/backups/maran` (`root:root 0700`), there is no upload endpoint and no download endpoint.
The archive is customer-*derived* but agent-*produced*, and a restore checks the artifact's SHA-256
against the sidecar the panel recorded, refuses a manifest that disagrees with the sidecar, refuses
another account's archive, refuses an unknown manifest version, and refuses a database the panel no
longer knows. That set of refusals is what makes the artifact trusted; **the moment an
operator-supplied or remote-fetched artifact becomes possible, every one of them is load-bearing in
a way it is not today** — and §5 is exactly that moment.

**Attacker B: the account's own owner.** They control the home the archive was made from, so they
control most of the archive's contents. What they get from a restore is: their own files back, as
themselves. The device-node case above is the one thing they can do that changes an outcome, and it
denies their own restore.

**Point of no return: the first `DROP DATABASE`.** Steps 1-5 (verify digest, pre-scan, manifest,
extract dumps root-side, extract home as the account) leave the account untouched and are undone by
doing nothing. Step 6 drops and reloads each database in turn, taking a rollback dump immediately
before each drop; a failure reloads in reverse order and reports FAILED naming what was put back and
what was not. Step 7's home swap is two renames with a microsecond window in which the account has
no home, and a failed second rename is reversed at once; if the reversal also fails the error names
the parked path.

What an interrupted restore leaves, plainly: **a killed agent between the drop and the load leaves a
database that does not exist**, with its only copy the rollback dump inside the scratch — which,
after §2's move, at least survives the reboot, and which the daemon's own `ExecStartPre` then
deletes on the next start. That combination is worth the owner's attention: the crash-recovery value
of the disk scratch is destroyed by the thing that makes the disk scratch acceptable. UNPROVEN and
important: **no interrupted-restore recovery has been executed**. Proof: on a polygon host, `SIGKILL`
the agent between a drop and its load and record what the account and the panel then show.

A running application can also lose writes made during a restore (dumps are taken at step 6, the
application keeps writing). That is stated in the plan as accepted.

---

## 5. The S3 client — the first outbound TLS dependency in the only root process

**Today's surface is small and should be described honestly as *refused, not absent*.**
`remote_destination_refused()` (agent/crates/agent/src/services/backup/remote_destination_refused.rs)
answers `ErrorCode::NotImplemented` at the service boundary, and it is the only thing standing
between an S3 destination and a backup silently written to the local disk and reported as remote.
`ObjectStoreHost` has no caller anywhere in the workspace (`seam-wiring-report.md` reproduced this
independently), so `S3ObjectStoreHost` is never constructed in production. But the code, and the
`object_store`/`hyper`/`rustls` dependency tree it drags in, **is linked into the root daemon
today**. rules/security.md item 10 ("no new outbound calls") is satisfied by behaviour, not by
absence of capability, and item 11 (dependency audit) applies now, not when it is wired.

**The surface the moment someone wires it:**

- Outbound TLS from a root daemon to an operator-supplied endpoint. `https://` is enforced
  (`REQUIRED_SCHEME`), which is right and is the only transport control.
- The credentials arrive **in the RPC** (`proto/agent/v1/backup.proto:108,112`), so the secret
  crosses the panel↔agent socket on every operation and lives in the C# process and its database.
  They are typed `SecretString` in the agent and the provider's error text is passed through
  `redact()` before it can reach a log. **What redaction does not cover:** it searches for the
  *access key id* (and only when ≥ 8 chars); it does not search for the secret key, it cannot
  redact what a dependency logs on its own, and it does nothing about the credential in the panel's
  own database or in a gRPC-level trace. UNPROVEN: that no `object_store`/`hyper` log target emits
  a signed URL or an `Authorization` header at any level the daemon enables. Proof: run the client
  against a local endpoint with `RUST_LOG=trace` and grep the output for the key id and for
  `X-Amz-Signature`.
- `MAXIMUM_RETRIES = 0` is deliberate and right (no retry storm, no provider lockout, no three-minute
  Save button).
- Artifacts land in a bucket **unencrypted** (see §6) and their confidentiality becomes a property
  of a third party's ACLs. `PublicReadVerdict::Unproven` exists because the public-read probe cannot
  always answer; an `Unproven` verdict on a destination holding every customer's data should be
  surfaced to the operator as an unresolved question, not as a neutral state.
- 16 MiB of a customer's archive is in the root process's memory per part, by design and bounded.

**Owner decision:** whether the remote arm ships at all before there is client-side encryption
(§6). Shipping it turns "an operator can read every customer's data" into "an operator, a cloud
provider, and anyone who obtains one API key can".

---

## 6. Artifacts are not encrypted at rest — the exposure, stated so the owner can decide

A backup artifact is: every file in the customer's home, plus a full plaintext SQL dump of every
database they own, gzipped, with a manifest naming the databases. It is `0600 root:root` under
`/var/backups/maran` (`root:root 0700`), verified inode-by-inode at publish (uid, mode, `nlink == 1`).
There is no download endpoint. So **on the running host, the artifact is readable by root and by
nothing else**, and that was measured indirectly by the same mode checks as §1's Case A.

Who can read one anyway:

- **root, and anything that becomes root.** Not a boundary the product can defend.
- **the server's operator** — legitimately, and this is the point worth naming: the operator of a
  multi-tenant panel can read every tenant's database in the clear, offline, with `tar` and `less`.
- **anyone holding the artifact bytes** — a host disk image, a hypervisor snapshot, a filesystem
  backup of `/var/backups`, a decommissioned disk, and (when §5 is wired) a bucket, its API keys,
  and its provider.

The plan decided against client-side encryption deliberately, and the reasoning is not weak: a lost
key means a customer's backups are gone at the moment they are needed, which is a worse and far more
likely outcome than the exposure above; and rules/security.md item 9 forbids home-grown crypto, so
the alternative is a real key-management story (where the key lives, who escrows it, what happens at
restore on a rebuilt host), not an `age`-shaped afternoon.

**This is a product call the owner has not made.** The facts to decide with are the two paragraphs
above. A middle position exists and is worth considering: no encryption for the local destination
(where the operator can read the live databases anyway, so encryption buys almost nothing), and
mandatory encryption for the remote destination (where the data leaves the operator's control and
the exposure is genuinely new) — which also means the remote arm cannot be wired until that decision
is taken.

---

## 7. The remaining items the plan's own checklist named, and their state today

- **The two-extraction split (R4).** `ExtractSpec::identity()` *derives* who extracts from which
  member — manifest and `databases/` as root into the root-only scratch, `home/` as the account —
  so a call site cannot pair them the wrong way round. The escalation it closes is the wrong
  pairing: root unpacking a customer-supplied archive into a customer's home, where a member's own
  path decides where root writes. Two fields would be two chances; one derivation is none. The
  split is also why the dumps must be root-side at all: the root-side `mysql` loader reads them, and
  a dump staged in account-writable space can be swapped between extraction and load. **§1 is
  precisely the failure of that premise** — the scratch is not as root-only as the split assumes.
- **Decision 3's refusal list**, each with what it prevents: `https://` only (credentials and a
  whole account in the clear on the wire); no retries (a provider lockout, and a Save button that
  blocks for minutes to say nothing); the manifest-version refusal (an archive from a future agent
  interpreted by an older one); the account-mismatch refusal (restoring one tenant's data over
  another's); the sidecar-vs-manifest disagreement refusal (which is what makes editing the cheap
  outside copy useless); the unknown-database refusal (a restore creating databases the panel does
  not know it owns); `scan_members`' whole-archive refusal (a hostile member's *neighbours* being
  unpacked anyway, which is what skipping would do).
- **The public-bucket probe's `Unproven` state** is not "fine": it means the panel could not
  determine whether a bucket holding every customer's data is world-readable. It should read as an
  open question in the UI, and today it is one more thing an operator can click past.
- **Task 13's residue exemption** (`Backup` rows of kind `PreDeletion` exempt from the account
  cascade and from the residue auditor) — the plan calls it the single most dangerous line in the
  plan, and rightly. **Re-measured 2026-09-07: the exemption still does not exist, but the kind now
  does.** `grep -rn PreDeletion backend/src` (excluding `obj/`/`bin/`) now returns exactly one line
  — `BackupKind.PreDeletion = 2` in
  `backend/src/Maran.Modules/Backups/Domain/Enums/BackupKind.cs` — and
  `ModuleAccountResidueAuditor.cs` still carries no exemption. So it is a hole to review when it is written, not one to review now, and the reviewer
  should require the widening mutation (exemption applied to *all* kinds) to go red before it lands.
- **Accepted gaps, as gaps:** no client-side encryption (§6); no artifact download endpoint (which
  is also today's best confidentiality control, and removing it is a security change, not a
  feature); full backups only, so every backup costs a full copy of the home and every database, and
  retention is the only thing between that and a full disk; a restore does **not** restore vhosts or
  certificates, so an account restored after a site was reconfigured comes back with files and
  databases and the wrong web configuration; and a running application may lose writes made between
  its rollback dump and the end of the restore.

---

## What a second reviewer must check

1. **§1 first.** Reproduce Cases E and F, then check the fix. This is the only finding here that is
   an exploitable escalation, and it was found by construction, not by reading.
2. That `ExecStartPre=-/bin/rm -rf` really empties the scratch on both families (`-` hides failure).
3. That no `object_store`/`hyper`/`rustls` log path can emit a credential or a signed URL.
4. An interrupted restore, killed between a `DROP` and its load, on a polygon host.
5. Whether `fs.protected_hardlinks=1` should be an installer preflight assertion rather than an
   assumption.
6. The `cargo audit` state of the `object_store` dependency tree that is now linked into the root
   daemon, wired or not (rules/security.md item 11).

## What the author could not verify

- Anything requiring a booted systemd or a real host: the unit's `ExecStartPre`, `UMask=`,
  `ReadWritePaths=`, the installer step's `stat` assertions, the uninstaller leaving artifacts.
  Substituted with a faithful reproduction of the daemon's own umask and directory-creation calls in
  a container.
- Any behaviour of a real `mysql`/`mysqldump`: neither binary exists on this workstation. The dump
  file's mode was modelled as `0666 & ~umask` (which is what `--result-file` does); if the client
  opens it more tightly, §1's Case A is unchanged and Case E is unchanged (the attacker owns the
  pre-created file there).
- The whole remote arm end to end — it has no caller to run.
- The device-node restore failure against the product's own `extract_home_as_account`, rather than
  against the same argv driven by hand.

## Decisions the owner must make

1. **Fix §1 before this commit lands.** It is an escalation from the panel uid to root. My
   recommendation is the path move (`/var/lib/maran-scratch`) plus the ancestor check, and a test in
   the installer-audit shape with both the refusal and the inverse control.
2. **Encryption at rest for artifacts** — none, everywhere, or remote-only (§6).
3. **Whether the remote arm ships at all** in this release, given (2) and the credential-in-the-RPC
   shape (§5).
4. **Non-regular files in a home** (§3): refuse at create, or accept that such an account's backups
   are not restorable and say so in the panel.
5. **A bound on total scratch usage**, not just per-dump — and a per-database cleanup — so a large
   account cannot fill the filesystem `/var/lib` sits on (§2).
6. Whether a branch carrying an outstanding second review may be handed on at all
   (rules/security.md: it may land on a branch, it MUST NOT merge to `main`).

---

## 8. UPDATE 2026-09-07 — §1 is closed, and how that was measured

**The second-reviewer requirement is still OUTSTANDING**, for this change as much as for the work it
fixes. rules/security.md is explicit that a note by the author closes nothing; what follows is an
argument for a reviewer to attack, and the branch carrying it must not merge to `main` until one has.
This section is also, unlike most of the note above, written about a change made in the same
session — so its measured paragraphs are the ones to trust, and its arguments are the ones to check.

### What changed

1. **The scratch left the panel-writable tree.** `AgentPaths::BULK_SCRATCH_ROOT` is now
   `/var/lib/maran-scratch`, a SIBLING of `/var/lib/maran` rather than a child of it. The choice was
   made against the evidence and not by copying §1's suggestion: `/var/lib` is `root:root 0755` on
   both families (measured in the container: `drwxr-xr-x root root`), and the new root appears in no
   `ReadWritePaths=` the panel process has — `maran-api.service` lists `/var/lib/maran`,
   `/var/log/maran` and its socket directory, none of which is a prefix of it. The panel uid
   therefore cannot place an entry at that name at all, which is the property §1's Case E needed and
   did not have.
2. **`open_scratch` now states the whole chain against the inodes.** Before creating anything it
   walks every level from `/` down to the scratch it is about to build, with `symlink_metadata` and
   never `metadata`, and refuses unless each level is a real directory owned by the expected uid —
   with the levels ABOVE the scratch root additionally required to carry no group- or other-WRITE
   bit, and the scratch root and everything below it required to be exactly `0700`. The two rules
   differ because the halves differ: `/`, `/var` and `/var/lib` are distribution-owned directories
   that must stay traversable by everyone, and write is the bit that decides who can put the next
   name down. The walk runs a second time after the directories are made, on the finished chain,
   including the `databases/` directory the dumps go in — the discipline `publish()` already had and
   the scratch did not. It observes the path the dumps are actually written into, never the constant
   they were derived from (rules/testing.md).
3. **The installer creates the new root the way the artifact-staging fix creates its staging
   directory** (`installer/lib/50-artifacts.sh`, today's other escalation): `rm -rf --` first, so a
   stale directory or a planted symlink is unlinked rather than followed, then
   `install -d -o root -g root -m 0700`, then a gate — `assert_root_only_directory` in
   `installer/lib/40-user.sh` — that aborts the install on a symlink, a non-directory, an owner that
   is not uid 0, or a mode that is not `700`. `installer/uninstall.sh` removes it (a killed run's
   leftovers there are plaintext customer databases, the worst residue an uninstall could leave).
   `installer/systemd/maran-agent.service` keeps `ExecStartPre=-/bin/rm -rf` on the new path and
   lists `/var/lib/maran-scratch` in `ReadWritePaths=`. **Two properties were checked by eye and are
   intact:** `EnvironmentFile=` (line 25) still precedes `Environment=PATH=` (line 55), and
   `maran structure`'s check 20 — which compares every absolute-path constant in `agent_paths.rs`
   against the unit's writable set — reports `STRUCTURE-OK`.

### The measurement

`ubuntu:24.04`, as root, `umask 0027` (the unit's `UMask=`), real `panel` and customer uids, driving
a harness that embeds the agent's REAL `open_scratch` source verbatim — the new one and the old
unchecked one side by side, so the same harness reproduces the attack and then tries it against the
fix. Full transcript: `.superpowers/sdd/2026-09-05-maran-backups/exploit-run.txt`, with the harness
and its driver beside it.

**Both exploits reproduced first, on the old layout and the old code**, which is what makes the
"after" columns mean anything:

| | Measured |
|---|---|
| Case E, before | `panel` pre-creates `scratch/` and `scratch/backup/` (it owns the parent), the daemon builds `b1` and `b1/databases` `0700 root`, `panel` renames `b1` aside and symlinks its own directory in, pre-creating `shop.sql` mode `0666`. Root's dump write: OK. `panel: cat -> "-- INSERT INTO users VALUES('secret-row');" rc=0`, file `-rw-rw-rw- panel panel`. |
| Case F, before | Dump name symlinked to `/root/victim.txt` (`0600 root:root`, content `ROOT SECRET`). Root's dump write: OK. `/root/victim.txt` afterwards: the attacker-influenced SQL text, still `-rw------- root root`. Panel → root. |
| Case E, after | `panel` cannot rename `/var/lib/maran-scratch` (EACCES), cannot create a sibling in `/var/lib` (EACCES), cannot even list the scratch (EACCES), cannot rename the leaf (EACCES). The dump lands `-rw-r----- root root`; `panel` reads it → EACCES rc=1; customer uid 5001 → EACCES rc=1. |
| Case F, after | Has no first step left: every write the attack needs is one of the EACCES lines above. |
| **Inverse control** | On the fixed layout a legitimate run still works end to end: `open_scratch: OK`, `dump write: OK`, root reads the dump back, and the whole chain is `root:root 700` at all four levels with the dump `0640`. A fix that refused everything would pass every row above and fail this one. |

**And the second half on its own**, because two independent fixes are worth more than one and neither
should be believed without seeing it fire:

| | Measured |
|---|---|
| The OLD location with the NEW code — parent `panel:panel 0750` | `REFUSED` (`ScratchUnusable`). The chain check alone closes §1 even without the move. |
| An ancestor `chmod 0777` | `REFUSED` |
| The scratch root replaced by a symlink to a panel-owned directory | `REFUSED`, and nothing was created through the link |
| The scratch root present but `0755` | `REFUSED` |
| **Inverse control:** that same root at `0700` | `OK` |

The unit tests carry the same pairs (`create_backup_tests.rs`: an accepting control, a writable
ancestor refused, an unwritable ancestor accepted, a symlink-as-root refused, a `0755` root refused,
a leaf outside the walk's base refused), plus one in `agent_paths_tests.rs` pinning the constant
outside `/var/lib/maran` with an inverse assertion that it is still under `/var/lib`. Gates from
`agent/`: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings` and `cargo doc` clean of
anything from these files; `cargo test --workspace --no-fail-fast`: **1386 passed / 0 failed / 69
ignored across 24 targets** against the 1374 baseline.

### What a reviewer must check, and what the author could not verify

1. ~~**`restore_backup.rs` has an `open_scratch` of its own, and it did NOT get the chain check.**~~
   **CLOSED 2026-09-07.** The asymmetry is gone: the check moved into
   `agent/crates/ops/src/backup/root_only_chain.rs`, and both `open_scratch` implementations now
   call it twice — `create_backup.rs:379`/`:394` and `restore_backup.rs:792`/`:806`, each once with
   `MissingLevels::Tolerated` before creating the chain and once with `MissingLevels::Refused` on
   the finished chain. Restore rests on the same two controls as create. What a reviewer should
   still check is the *shared* module, not the two call sites: one implementation now decides for
   both directions.
2. Nothing here was run on a booted host. `ExecStartPre=`, `ReadWritePaths=`, `UMask=` and the
   installer's own gate were reasoned about and reproduced in a container, never observed under
   systemd — the same limitation the rest of this note carries, and item 2 of §"What a second
   reviewer must check" is still open.
3. ~~`docker/polygon/assert-installer-steps.sh` ... has no equivalent assertion for
   `/var/lib/maran-scratch`.~~ **CLOSED 2026-09-07.** The polygon now carries the boundary:
   `assert-installer-steps.sh` asserts `"/var/lib/maran-scratch:0:0:700"` alongside
   `"/var/lib/maran-sftp:0:0:700"`, refuses either path that resolves back underneath
   `/var/lib/maran` (the panel-owned ancestor this move existed to escape), and calls
   `assert_ancestors_are_root_only` on both. It also carries the inverse control
   `rules/testing.md` requires: `assert_scratch_gate_refuses` plants a hostile scratch and demands a
   refusal, while a separate assertion demands that the same walk ACCEPT the legitimate
   `/var/lib/maran-sftp`, so a walk that refused everything could not pass. (Note the jail base is
   `/var/lib/maran-sftp`, not the `/var/lib/maran/sftp` this entry used to name.)
4. The dump write was modelled as an `O_CREAT` open at mode `0666` — what `--result-file` does. If a
   real client opens more tightly, every row above is unchanged: the "before" rows depend on the
   attacker having made the file, and the "after" rows on the write never reaching it.
