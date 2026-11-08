# Threat note: panel self-update, `maran update` and `maran rollback` (issue #128)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to how a signed release reaches a root process. This note covers the
CLI core of the self-update: the verbs `maran update` and `maran rollback`, the release layout they
introduce, and the way they verify, switch, migrate, restart and roll back. It is written BEFORE the
code: every statement below is a requirement the code must meet, not a description of code that exists.
Where the code lands and differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (rules/security.md), the branch must not merge to main.** The work is
carried out by agent sessions, which cannot obtain a second reviewer. The branch
`fix/128-panel-self-update` MUST NOT merge to `main` until a human reviewer has read it
(rules/security.md "Sensitive change escalation"). The owner is told at hand-off that this surface
carries an outstanding review. The earlier notes this one builds on
(`2026-09-19-release-bundle-signing-threat-note.md`,
`2026-11-04-panel-backup-schedule-threat-note.md`) carry their own review state; nothing here
discharges either.

## What is added

The design contract is the file the implementing agents code to; it is summarised here so a reviewer
does not need it.

1. Two root-only verbs of the existing `maran` CLI (`installer/cli/maran`, installed by
   `installer/lib/66-cli.sh`): `maran update [--check] [--channel C] [--offline-tarball PATH] [--yes]`
   and `maran rollback [--restore-database] [--yes]`. Exit codes: 0 ok or nothing to do, 1 failure via
   `cli_fail`, 64 usage, 77 not root (existing), and a distinct failure message for "updated, the
   health gate failed, rolled back".
2. A release layout (new installs by `installer/lib/50-artifacts.sh`; a legacy host is adopted by the
   first `maran update`):
   - `/usr/local/maran/releases/<version>/{api,agent,frontend}`, `root:root`, never written after install.
   - `/usr/local/maran/current -> releases/<version>`, swapped atomically (`ln -sfn` to a temporary
     name, then `mv -T`).
   - `/usr/local/maran/{api,agent,frontend} -> current/{api,agent,frontend}`, static symlinks set
     once. No systemd unit, nginx vhost or path elsewhere changes: `ExecStart=/usr/local/maran/api/Maran.Host`
     in `installer/systemd/maran-api.service` and the agent unit keep resolving.
3. State files: `/etc/maran/release.state` (`root:root` 0644; lines `CHANNEL=stable|beta`,
   `VERSION=<semver>`, `PREVIOUS=<semver or empty>`) and `/var/backups/maran-update/` (`root:root`
   0700; pre-update dumps `pre-<from>-to-<to>-<UTC timestamp>.dump`, mode 0600, the 3 newest kept).
   The 3 newest release directories are kept, plus `current` and the previous one.
4. One additive field in the signed manifest, `channel` (`stable` or `beta`), written by
   `scripts/lib/release-bundle.sh` and checked by the CLI. The existing scheme is unchanged: ECDSA
   P-384 over SHA-384 (`MARAN_SIGNATURE_DIGEST` in `scripts/lib/release-bundle.sh`), verified against
   the public key shipped at `installer/keys/release-signing.pub` and installed by
   `installer/lib/66-cli.sh` to `/usr/local/maran/cli/keys/release-signing.pub` (root 0644 in a root
   0755 directory).
5. New libraries under `installer/cli/lib/` (`release-version.sh`, `release-verify.sh`,
   `release-unpack.sh`, `release-layout.sh`, `panel-dump.sh`, `update-dumps.sh`, `update-health.sh`,
   `update.sh`, `rollback.sh`) and one change to the existing ones: `panel-backup.sh` and
   `panel-restore.sh` call the new `panel-dump.sh`, so `pg_dump` and `pg_restore` are spelled in one
   place. Each concern has one home: `release-version.sh` is the only place a version or release name
   is validated or ordered, `release-unpack.sh` the only place an archive is vetted and unpacked,
   `update-dumps.sh` the only place the pre-update dump name is built and read.
   *Correction, 2026-11-04, after review: the first draft of this list named six libraries and let
   the installer carry its own copy of the verify and unpack code; the list above is the fixed set.*
6. The order of `maran update` (from the design): root and lock; resolve channel; fetch manifest and
   signature, verify, check channel, check version; download the three artifacts for this
   architecture and verify sha256; free-space check; `pg_dump`; unpack into `releases/<v>.partial` and
   rename; adopt a legacy layout if needed, switch `current`, record PREVIOUS; run migrations from the
   NEW binary; restart `maran-api` then `maran-agent`; health gate; on failure at the switch, migrate,
   restart or gate steps, automatic rollback of the symlink; on success write `release.state`, prune,
   write an audit line, print a summary.

Not added: no daemon, no timer, no listening port, no RPC, no new package, no proto change, no
change to the agent. The update runs only when an operator types it. The UI button and the automatic
security patches in the spec (section 14) are separate sub-issues and are NOT part of this change.

## What an attacker could do with each surface, and why it is safe

### 1. A compromised or man-in-the-middle download host, or a replayed old signed manifest

The CLI fetches `manifest.json` and `manifest.json.sig` from the release host over HTTPS
(`MARAN_RELEASE_BASE_URL` is `https://releases.maran.innovayse.com` in `installer/cli/lib/release-verify.sh`).
A host that is compromised, or a network position that can answer in its place, controls every byte
the CLI downloads. A root process will then run what it unpacks, so this is the sharpest surface in
the change. Requirements:

- The signature covers the WHOLE manifest, including `version`, `channel` and every `url` and
  `sha256`. The CLI verifies it before reading a single field it acts on. A field is read out of the
  verified file only; nothing is read from the unverified one.
- `channel` is inside the signed bytes. A beta manifest replayed to a CLI that asked for stable is
  refused because the verified `channel` differs from the requested one. A manifest with no `channel`
  field is refused with a clear message when it came from the network (an old manifest cannot be
  told apart from a stripped one).
- The new version must be greater than the installed one by SemVer 2.0 precedence, pre-release aware
  (`cli_version_gt` in `installer/cli/lib/release-version.sh`). An old signed manifest replayed to a newer host is refused: its version
  is not greater. `rollback` is the only verb that moves backwards, and it uses a version already on
  disk, never a downloaded one.
- sha256 is checked twice: after each download and again immediately before unpacking. Each digest
  comes from the verified manifest. A mismatch refuses the update and deletes the staging directory.
  *Correction, 2026-11-04, after review: the second check lives INSIDE `cli_layout_install_release`
  (`release-layout.sh`), not in the caller. It re-reads each archive's digest from
  `STAGEDIR/manifest.json` (`cli_release_check_sha256` with `cli_release_manifest_field
  "<component>-<arch>.sha256"`) immediately before the unpack, so no caller can unpack without it.*
- TLS everywhere: `curl --proto =https` with certificate verification never disabled (no `-k`, no
  `--insecure`), `--fail` so an error page is never saved as an artifact.
  *Correction, 2026-11-04, after review: every `curl` also carries `--disable` (no `.curlrc`),
  `--tlsv1.2` and `--max-redirs 3`, besides the connect and total timeouts and the size caps. The
  total artifact cap matches the free-space floor. The health probes carry `--disable --noproxy '*'`.*
- No key override path. The public key path is a constant of the library, not read from an
  environment variable or a flag in production code. An attacker who can set the environment of a root
  CLI already has root; the point is that no supported knob exists to point the check at another key.
  The same applies to the release base URL: a reviewer checks that no environment variable replaces
  it in the shipped CLI.
  *Correction, 2026-11-04, after review: "a constant of the library" was too coarse. The CLI bakes
  its own key path as the default of `cli_release_verify_manifest DIR [KEYFILE]`; the INSTALLER, which
  runs before the CLI is installed, SOURCES the CLI's `release-version.sh`, `release-verify.sh`,
  `release-unpack.sh` and `release-layout.sh` (with `common.sh`) from its own package's `cli/lib/`
  and passes `${SCRIPT_DIR}/keys/release-signing.pub` as an ARGUMENT. There is no environment
  override on either side and no second copy of the verify or unpack code: the installer is thin
  (`installer/lib/50-artifacts.sh`) and calls `cli_release_*` and `cli_layout_*`. The online
  download in the installer uses the CLI's downloader, so the TLS, protocol, timeout and size
  settings above hold for a fresh install too, and the signed `channel` field is required there as
  well.*
- **The offline tarball is copied once, and only the copy is trusted.**
  *Correction, 2026-11-04, after review: the first draft vetted a path and then extracted the
  path, which an attacker who could write to the tarball's directory could swap between the two.*
  `cli_release_verify_offline` requires the tarball's parent directory to be root-owned and not
  group- or world-writable, copies the file ONCE into the root-0700 work directory (`install -m 0600`
  from a single open), and vets (`cli_unpack_vet_members`) and extracts only that copy.
- **Environment scrubbing.** *Correction, 2026-11-04, after review.* `installer/cli/lib/common.sh`
  unsets, at its top, `OPENSSL_CONF`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `CURL_CA_BUNDLE`, `CURL_HOME`,
  `TAR_OPTIONS`, `LD_PRELOAD`, `LD_LIBRARY_PATH` and the `PG*` variables, so the operator's
  environment cannot point `openssl`, `curl`, `tar` or the database client at other material. This
  is not a defence against an attacker who already controls a root shell's environment; it keeps an
  ordinary leftover export from silently changing what a signature check trusts.
- The `version` that becomes a directory name (`releases/<version>`) is validated against a
  strict SemVer regex before any path is built from it, so a signed-but-hostile value cannot carry
  `/` or `..`.
  *Correction, 2026-11-04, after review: the check is `cli_version_is_semver` in `release-version.sh`
  (SemVer 2.0 without build metadata, no leading zeros in numeric identifiers, at most 64 bytes),
  the only definition of a version pattern in the tree; no other library defines a regex. Ordering is
  `cli_version_gt`, where a `legacy-YYYYMMDDTHHMMSSZ` name counts as LOWER than any SemVer and two
  legacy names compare equal. Wherever a release name is valid (switch, PREVIOUS, state, prune,
  rollback) `cli_version_is_release_name` accepts SemVer or legacy; a name that fails is refused with
  `cli_fail`.*

What this does NOT defend against, stated plainly:

- **A freeze or withhold.** The manifest carries no expiry. A host that serves a still-valid older
  manifest above the installed version keeps the operator on a known-vulnerable release, and the
  operator sees "available" for that version, not for the newer one that exists. The version-greater
  check stops a downgrade, not a stale upgrade.
- **A compromised signing key.** Whoever holds the key can sign anything every server accepts. This is
  the limit already recorded in the release-bundle note; nothing here moves it.
- **A legitimate release built from defective source.**

### 2. A stale, partial or tampered staging directory

- Staging is root-only. The CLI's existing staging base is `/var/lib/maran-cli-staging`
  (`MARAN_CLI_STAGING_BASE` in `installer/cli/lib/common.sh`), outside `/var/lib/maran`, which the api
  user owns and can write (`ReadWritePaths` in `installer/systemd/maran-api.service`). The same
  reasoning as `MARAN_ARTIFACT_TMP` in `installer/lib/50-artifacts.sh` applies: the api could replace
  an archive between verification and extraction if root staged inside its reach.
- The per-run staging directory is created fresh (`mktemp -d`-style, 0700, root:root) and the CLI
  refuses to continue if the path is a symlink, not a directory, not owned by uid 0 or accessible to
  group or others, the same gate `prepare_staging_dir` applies in the installer. A leftover from an
  earlier killed run is removed, never reused.
- Nothing is unpacked into the live tree. Unpacking goes to `releases/<version>.partial` and the
  directory is renamed to `releases/<version>` only after both sha256 checks passed. A directory with
  the final name therefore always held a verified, completely unpacked release.
- Requirement on the unpack: archive members are listed first and refused if any is absolute or
  contains `..`; the extraction does not restore ownership from the archive (`--no-same-owner`) and
  the finished tree is made `root:root` and not group or world writable by the CLI itself. The sha256
  chain already ties the bytes to the signed manifest; this is a second layer for the day a signed
  archive is itself malformed.
- If `releases/<version>` already exists, the CLI does not overwrite it. It compares nothing and trusts
  nothing: it refuses with the path, or removes only a `.partial` it created itself.
  *Correction, 2026-11-04, after review: "refuses" was wrong for the case that matters. A release
  directory left by an update that failed and rolled back is ABANDONED, and refusing it would make
  that version impossible to retry. `cli_layout_install_release VERSION STAGEDIR [replace]` therefore
  replaces an existing release directory, but ONLY when it is neither `current` nor the release
  `PREVIOUS` names; for those two it refuses. The installer's re-run of the same version uses the same
  path. The replacement is still unpacked from archives whose sha256 was re-checked immediately
  before, so a replaced tree is no less verified than a new one.*
- *Correction, 2026-11-04, after review: unpacking is `cli_unpack_vet_members` then `cli_unpack_into`
  (`release-unpack.sh`).* The vetting refuses absolute members, `..`, backslashes, hard links,
  devices, and any symlink whose target escapes the root (judged by `dirname` semantics, depth
  included); **symlinks are refused outright in release trees**, which carry none. The unpack runs in
  a `umask 022` subshell with `--no-same-owner --no-same-permissions`, and the finished tree gets
  `chmod -R go-w,ug-s,o-t` and `chown -R root:root`.

### 3. The symlink swap and the time-of-check to time-of-use window on `/usr/local/maran/current`

The pointer that decides which code root runs is a symlink. The question is who can change it, and
who can change what it points at.

- `/usr/local/maran` is root-owned (`chown -R root:root` in `cli_unpack_into`, `installer/cli/lib/release-unpack.sh`). Its
  parent `/usr/local` is root-only. The CLI is the only writer of `current`, `releases/`, and the
  three static symlinks, and it runs as root.
- The api runs as the unprivileged service account (`User=__MARAN_USER__` in
  `installer/systemd/maran-api.service`) under `ProtectSystem=strict`, so the whole filesystem is
  read-only to it except the three `ReadWritePaths` entries: `/var/lib/maran`, `/var/log/maran/panel`
  and the socket directory. None of `/usr/local/maran`, `/etc/maran` or `/var/backups/maran-update` is
  among them, and the files are root-owned besides. Requirement: this must stay true. The
  reviewer re-runs the `grep` in "What a reviewer must check" item 3, and any change that adds a
  path under `/usr/local/maran`, `/etc/maran` or `/var/backups` to that line fails review.
- The agent runs as root, so file permissions do not constrain it. What constrains it is its unit
  (`installer/systemd/maran-agent.service`, `ProtectSystem=true`, which makes `/usr` read-only to it,
  and a `ReadWritePaths` list that names no path under `/usr/local/maran`) and the closed command set
  (rules/architecture.md): no RPC writes there. This is stated as a requirement and listed under
  "What the author could not verify".
- The swap is atomic: a new link is created under a temporary name in the same directory and moved
  over `current` with `mv -T`, so there is no instant at which `current` is missing or dangling. The
  CLI does not use `ln -sfn` directly onto `current`, which is two steps on some implementations.
- The check-then-use window: the CLI resolves `current` once to a version, and every later decision
  (what to roll back to, what to prune) is made from `release.state` and the directory names, never
  from re-reading the link in a loop. A swapped link between steps could only be done by root.
- Prune never removes `current`, never removes the version `PREVIOUS` names, and removes only
  directories whose name matches the version regex and that sit directly inside `releases/`. It does
  not follow symlinks and removes with `--` and an argv array.
- Legacy adoption moves `api`, `agent` and `frontend` into `releases/<name>` with `mv` inside one
  filesystem, which keeps the inodes of running processes. It runs only when `/usr/local/maran/api` is
  a real directory, and it creates the symlinks only after the moves succeeded.
  *Correction, 2026-11-04, after review: the adopted release is named `legacy-<UTC timestamp>`
  and is a VALID name everywhere a release name is: a rollback target, `PREVIOUS`, a state value, a
  prune candidate. It compares lowest (see section 1), so any real release is an upgrade from it.
  A kill in the middle of adoption (some of the three directories moved, the symlinks not yet made) is
  RESUMED by the next run, not refused: the moves already made are recognised and the rest completed.
  Symlinks inside release trees are refused outright (section 2), so adoption cannot import one.*

### 4. The pre-update database dump

- It is a full `pg_dump` in custom format, so it contains every table, including the columns the panel
  encrypts at rest (AES-GCM `IEncryptionService`) and the rows of the `wolverine` schema. The key that
  decrypts them lives in `/etc/maran/panel.env`, which the dump does not contain, but a dump beside a
  copy of that file is the whole panel. The dump is therefore handled as a secret.
- It is written to `/var/backups/maran-update/`, `root:root` 0700, as a file of mode 0600. The CLI
  creates the directory with `install -d -m 0700` and checks owner, mode and that it is not a symlink
  before writing, the check the panel-backup directory already gets.
- The dump runs as the service account through `runuser` (so no root database authentication exists),
  and ROOT opens the output file; the service account never sees a path to it. The process writes to
  an inherited descriptor, not to a path the service account names.
- Retention: the 3 newest dumps by the timestamp in the NAME (not mtime), matching
  `pre-<from>-to-<to>-<UTC>.dump` anchored at both ends; anything else in the directory is never
  touched.
  *Correction, 2026-11-04, after review: the name contract has one home, `update-dumps.sh`
  (`cli_dumps_dir`, `cli_dumps_name`, `cli_dumps_newest_to`, `cli_dumps_prune`,
  `cli_dumps_require_private`); `update.sh`, `rollback.sh` and the layout's interrupted-update message
  use only those. `<from>` and `<to>` are release names; on a legacy host `<from>` is the ADOPTED
  name `legacy-<ts>`, the same string recorded as `PREVIOUS`, never `0.0.0` or `unknown`.*
- Never printed, never logged: output carries the file path, not content; the audit line carries verb
  and outcome only. A dump is never uploaded, copied elsewhere or put in the diagnostics bundle by
  this change.
- A failed or empty dump aborts the update before anything is switched.

### 5. Migrations from the new binary, and why an automatic rollback never restores the database

- `Maran.Host --migrate` of the NEW release runs before the old `maran-api` is restarted, as the
  service account, so the schema is changed by the code that understands it and never by root. The
  environment comes from `panel.env`.
  *Correction, 2026-11-04, after review. The first draft said the file is "not `source`d into the
  root shell". That sentence was false about the code and about the installer it claimed to copy,
  and it is replaced by the truth: `panel.env` IS sourced, as root, inside a subshell, by ONE helper,
  `cli_run_as_service BINARY ARGS...` in `installer/cli/lib/common.sh`, which changes into the
  binary's directory and runs `runuser -u maran -- BINARY ARGS` with the file sourced first
  (`installer/lib/65-schema.sh` and `cli_dotnet` always did the same; `cli_dotnet`,
  `cli_panel_restore_migrate` and `cli_update_migrate` now all go through the helper). A sourced
  file is code executed as root, so this is acceptable ONLY because (a) the file is root-owned, mode
  0640 or stricter, in a root-owned directory the service group cannot write, and (b) its values come
  from a validated alphabet (the installer writes them; rules/security.md item 4). The CLI enforces (a)
  rather than assuming it: `cli_require_trusted_env_file` refuses to source the file unless it is a
  regular non-symlink, uid 0, mode 640 or stricter, and a different owner or mode is a `cli_fail`.
  Migration output is printed only on failure, and capped. A reviewer checks the helper, the refusal
  and that no other library sources the file (reviewer check 8).*
- The safety of an automatic rollback depends on the expand-then-contract law
  (rules/architecture.md): the PREVIOUS release must still run against the NEW schema. `maran migrate
  guard` fails CI for a migration that drops, renames or narrows what the last release reads, and the
  only way past it is a `// contract-phase:` line. A release that carries such a line breaks the
  rollback promise for that one step. Requirement: the notes of such a release say so, and the
  health gate after a rollback is real, so a rollback that cannot start the old code reports it.
- **Automatic rollback never runs `pg_restore`.** A restore discards every row written since the dump
  and every Wolverine message in flight (the reason rules/architecture.md gives for expand-then-
  contract). Doing that unattended, on a health failure that may be a transient, trades a failed
  upgrade for silent data loss. After a rollback the CLI prints the dump path and the explicit
  command. Only `maran rollback --restore-database` restores, requires `--yes` or a typed word, and
  states that it drops all writes since the dump.
- A migration that is half applied when the process dies is the migration tool's atomicity, not this
  change's; the note does not claim otherwise.

### 6. Rollback loops and half-states

Concurrency and kills:

- Lock: `flock -n` on the shared panel-archive lock (`MARAN_PANEL_LOCK_FILE` in
  `installer/cli/lib/panel-archive.sh`, taken on a descriptor opened under `MARAN_CLI_STAGING_BASE`).
  `update`, `rollback`, `panel-backup` and `panel-restore` are mutually exclusive. A second run exits
  at once rather than waiting. The kernel releases the lock when the holder dies, including on
  `SIGKILL`, so a killed update never leaves a lock that blocks the next one.
- The scheduled panel backup (`maran-panel-backup.timer`) is not stopped by editing the unit: the
  lock refuses a concurrent scheduled run, which then exits non-zero and shows `failed` in
  `systemctl` once. That is a visible side effect of an update, and it is the honest alternative to
  the CLI starting and stopping units it does not own.
- A trap on EXIT removes the staging directory and the `.partial` directory this run created. A trap
  on `INT` and `TERM` does the same cleanup and prints the recovery command; it does NOT start a
  rollback inside a signal handler.

States a kill can leave, and how the next run recovers. A kill is `SIGKILL`, the machine losing power
or an operator closing the terminal; a trap does not run for the first two.

| Killed during | Left behind | Services run | Next run |
|---|---|---|---|
| download, verify, free-space, dump | a staging directory, maybe a dump | old release | removes the stale staging directory; `current` and `release.state` untouched |
| unpack | `releases/<v>.partial` | old release | removes any `.partial`, never renames one it did not finish |
| after the rename, before the switch | a complete `releases/<v>` not pointed at | old release | sees `current` unchanged; a complete release directory with no pointer is simply the next candidate |
| after the switch, before the restart | `current` -> new, `release.state` still says old | old processes (old inodes) | see below |
| during the migration | `current` -> new, schema maybe partly expanded | old processes | see below |
| during the restarts or the gate | `current` -> new, services in either state | mixed | see below |

The three rows that matter are those where `current` already points at the new release while
`release.state` still names the old one. The design records PREVIOUS before the switch and writes
the rest of `release.state` only on success, so the file and the link disagree, and that disagreement
is the marker of an interrupted update. **The design does not say what the next run does about it;
this note records the requirement, and the code must meet it:** a run that finds the resolved version
of `current` different from `VERSION` in `release.state` refuses a new update and prints the two
versions, the dump path and the command to take (`maran rollback`). It never repairs the state by
guessing which side is right. `PREVIOUS` is written durably before the switch so `rollback` has a
target after a kill.

*Correction, 2026-11-04, after review: the check (`cli_layout_check_consistent`) is FAIL-CLOSED. A
`current` that exists but cannot be resolved is an error; a missing `current` is consistent only when
`release.state` is missing too or names a `legacy-*` version. It prints the facts to stderr once and
returns non-zero (it does not call `cli_fail`), and the caller tests it as a plain condition. No
reader of the layout or the state uses `|| true`, and `cli_layout_current_version` prints an empty
string ONLY when there is no `current` symlink. An installed version that is unknown with no state is
refused unless the host is legacy-adopted (a real `api` directory present). State writes `sync` the
file and its directory, so there is no window of an unsynced state file. A rollback killed after the
switch but before the state write is completed by re-running it. `maran rollback --restore-database`
also works when `PREVIOUS` equals `current` after an automatic rollback: it restores only the newest
dump taken for the update that was rolled back (`cli_dumps_newest_to`), without switching.*

A danger in those rows is a reboot: a host that reboots with `current` on the new release starts
the NEW api and agent against a schema that may or may not have been migrated. That is the
ordinary risk of any interrupted deploy; the requirement above is what makes it visible.

Loops: the automatic rollback happens at most once per run. If the gate fails after the rollback as
well, the CLI stops, exits non-zero with the "rolled back but still unhealthy" message, and does
nothing further. There is no retry loop, no timer, and no repeated attempt to update again.

### 7. The CLI replacing itself, and the unit and library mismatch window

- `maran update` replaces `/usr/local/maran/{api,agent,frontend}` through the symlinks. It does NOT
  replace the CLI (`/usr/local/sbin/maran`, `installer/cli/lib/`) or the signing key
  (`/usr/local/maran/cli/keys/`): those are installed by the installer step, not by this verb. A bash
  script that rewrites itself while it runs reads garbage; this design avoids the case by never doing
  it. Reviewer check: the verb writes nothing under `installer/cli`'s install location.
- A CLI newer than the release it manages, or older than it, is possible: the operator can install a
  new installer package without updating the panel, and an update does not carry a new CLI. The
  mismatch is in the CLI's expectations of the release (`--migrate`, `/health/ready`, the agent socket
  path, the layout). Requirement: the CLI reads only interfaces that already exist (`Maran.Host
  --migrate`, the two health endpoints, the agent socket) and treats a missing one as a failed gate
  and a rollback, never as success.
- Systemd units are not changed by this verb. A release that needs a new unit directive cannot ship it
  through `maran update`; that is stated in the operator documentation, and it is why the unit paths
  stay behind the static symlinks.
- The proto window between the two restarts: the agent restarts LAST (spec section 14), so for a
  short time the new api talks to the old agent. The proto is additive inside a major version
  (rules/architecture.md), which covers one revision. The health gate runs only after both restarts,
  because `/health/ready` may be 503 between them.

### 8. Denial of service

- **A release that never becomes healthy.** The gate polls `/health/ready` and `/health` on the api's
  unix socket and the existence of the agent socket, up to a timeout (default 120 seconds). On
  expiry the CLI rolls back once and exits non-zero. An attacker with a validly signed broken
  release could force this, but that needs the signing key. A bad release of our own is the likelier
  case, and the answer is the rollback.
- **A disk filled by dumps and releases.** Dumps are capped at the 3 newest and releases at 3 plus
  `current` and the previous one. The design puts the free-space check AFTER the download, so a
  download can itself fill the staging filesystem before the check runs. Requirement: a conservative
  floor check before the first download, and an exact check (compressed artifacts, unpacked size,
  database size) before the dump. Accepted: fixed floor, not the database size. The code checks a
  fixed 2 GiB floor on three filesystems and does not size the dump from the database; this is an
  accepted residue. A full disk during the dump aborts with nothing changed.
- **A release host that answers slowly or never.** Every `curl` carries a connect timeout and a
  maximum time, so a stalled download fails instead of holding the lock indefinitely. The lock is not
  a queue, so a hung run blocks `panel-backup` until it ends or is killed.
- **A repeated failing update** costs a dump each time, bounded by the retention of 3.

### 9. A new outbound call from a root CLI

`rules/security.md` item 10 says no new outbound calls; the only phone-home is the documented licence
lease, and anything of the kind needs a spec change first, not a PR. `maran update --check` and
`maran update` contact the release host from a root process. This is an outbound call that the spec
has not approved. Facts, not approval:

- The check is operator-triggered only. There is no timer, no background call, no call at login, in
  the installer or in the panel process. With no operator typing the command, nothing is ever sent.
- `--offline-tarball PATH` needs no network at all. It verifies the signature and checksums of a
  bundle the operator carried to the server and is a first-class path, not a fallback.
- The request is a plain `GET` of the manifest and signature, with no query string, no identifier of
  the panel, the host, the licence or the customer, and no header beyond what `curl` sends. The CLI
  adds no telemetry. A reviewer checks the `curl` argv.
- The destination is the project's release host, the one the installer already contacts at install
  time; the operator's own data goes nowhere.

#### Owner decision: NOT recorded here

**The owner has not approved this outbound call and the spec has not been amended.** This note and
its branch argue the case; they cannot grant it. The pull request for this branch must say so in plain
words, next to the OUTSTANDING second review, so that nobody reads the argument as an approval. The
decision is the owner's: accept the operator-triggered check as an exception and amend the spec, or
ship only the offline path.

*Addendum, 2026-11-07, issue #383: the statements above that there is "no timer" and that the check is
"operator-triggered only" describe this change as it was reviewed. The unattended security-patch update
adds a root-owned timer that makes the same call daily; its own threat note
(`2026-11-07-security-patch-updates-threat-note.md`) carries that argument, and it is blocked on the
same missing owner decision (issue #384), now for a timer-driven call as well.*

### 10. The stale "Ed25519" statements versus the ECDSA P-384 scheme in the code

The code signs and verifies with ECDSA P-384 over SHA-384 through `openssl dgst`
(`MARAN_SIGNATURE_DIGEST` and `sign_release_file` in `scripts/lib/release-bundle.sh`, with the reason
recorded there: issue #52, OpenSSL 1.1.1 hosts cannot verify Ed25519 with `pkeyutl -rawin`). Several
places still say Ed25519:

- `rules/security.md` item 9 (line 47): "Ed25519 for licenses/artifacts".
- `docs/superpowers/specs/2026-08-29-maran-design.md` section 14 (the installer paragraph): "проверкой
  Ed25519-подписи манифеста". Section 13 describes licences as signed Ed25519 JSON, and that statement
  is not about this scheme.
- `docs/superpowers/specs/2026-09-19-maran-code-integrity.md` (line 53), which describes the release
  gate as Ed25519.
- `installer/lib/50-artifacts.sh` comments (the header, the public-key comment and two function
  comments). **This change fixes those comments**, because they are code comments describing
  behaviour the code does not have (rules/architecture.md).
- `scripts/lib/release-bundle.sh` (the `require_signing_key` message names an Ed25519 key file).
  Whether this change touches it is for the author of that script; it is listed so the grep result
  is not a surprise.

The rules and the spec are NOT edited by this change: rules change by pull request with the owner's
approval (rules/README.md), and the spec is the owner's. **Owner decision: reconcile.** Either the
artifact scheme is declared ECDSA P-384 in `rules/security.md` and spec section 14 (licences stay
Ed25519, which rule 9 can name separately), or the code changes. This note assumes the first, because
the code, the shipped public key and the verifying hosts already agree on it.

## What a reviewer must check

Each item names a command. Negative results owe a positive control (rules/testing.md); where the
command is a search for absence, the control is the same pattern against a line that must match.

1. **The signature, the channel and the version are checked before any other field is trusted.**
   Read `cli_release_verify_manifest` and `cli_release_fetch_manifest` in
   `installer/cli/lib/release-verify.sh`; confirm no `cli_release_manifest_field` call precedes
   verification:
   `grep -nE 'cli_release_manifest_field|cli_release_verify_manifest' installer/cli/lib/*.sh`.
   Run the unit tests for a tampered manifest, a wrong channel, an equal version and a lower version,
   each of which must refuse, and the control: a good manifest accepted.
2. **No override of the key or of the base URL.**
   `grep -nE 'release-signing\.pub|MARAN_RELEASE_BASE_URL|RELEASE_PUBLIC_KEY' -r installer/cli installer/lib`
   must show the key path as a constant and no `${VAR:-...}` fallback and no flag that replaces it.
   Control: the same grep must find the constant in `installer/cli/lib/release-verify.sh` (the CLI's
   baked default) and the installer's `${SCRIPT_DIR}/keys/release-signing.pub` argument in
   `installer/lib/50-artifacts.sh`. *Correction, 2026-11-04, after review: the first draft expected the
   constant in `50-artifacts.sh` alone; the key is now passed there as an argument and baked in the
   CLI.*
3. **The api cannot write anywhere in the releases, `current`, `/etc/maran` or the state directory.**
   `grep -nE '^ReadWritePaths=|^NoExecPaths=' installer/systemd/maran-api.service` must list only
   `/var/lib/maran`, `/var/log/maran/panel` and the socket directory placeholder. And
   `grep -nE 'ReadWritePaths=' installer/systemd/maran-agent.service` must name nothing under
   `/usr/local/maran`. Also confirm `/etc/maran` is root-owned and not writable by the service group
   in `installer/lib/60-config.sh`.
4. **The swap is `mv -T` of a temporary link.**
   `grep -nE 'mv -T|ln -s' installer/cli/lib/release-layout.sh`: no `ln -sfn` directly onto `current`.
5. **No `pg_restore` outside `panel-dump.sh`, and none in the automatic path.**
   `grep -rnE 'pg_restore|pg_dump' installer/cli` must list only `panel-dump.sh` (positive control:
   it lists that file) and `grep -nE 'cli_restore_database' installer/cli/lib/update*.sh` must be empty
   (control: `rollback.sh` does call it under `--restore-database`).
6. **Every refusal path has a test, and each has an inverse control.** List the refusals in
   `update.sh`, `rollback.sh` and the libraries and match each to a named test; confirm a gate that
   refuses everything would fail one (the accepting case).
7. **Recovery from an interrupted update is implemented as required in section 6.** Read what
   `cli_update` does when `current` and `release.state` disagree; run the case in the tests.
8. **No shell string building, no `eval`, no `source` of `panel.env` outside the one helper, no secret
   in output.** *Correction, 2026-11-04, after review: the first draft required this grep to be empty
   over the new libraries, which contradicted the design (the helper sources `panel.env`) and the
   installer (which sources the CLI libraries).*
   `grep -nE '\beval\b|bash -c|sh -c' installer/cli/lib/{release-version,release-verify,release-unpack,release-layout,panel-dump,update-dumps,update-health,update,rollback}.sh`
   must be empty. Then
   `grep -nE '^\s*(\.|source) ' installer/cli/lib/*.sh` must list the sourcing of `panel.env` only
   inside `cli_run_as_service` in `common.sh`, immediately after `cli_require_trusted_env_file`, and
   no other library may source it. The installer's `installer/lib/50-artifacts.sh` sources only the
   five CLI libraries named in section 1 (`grep -nE '^\s*(\.|source) ' installer/lib/50-artifacts.sh`).
   Control: `grep -nE '^\s*(\.|source) ' installer/cli/lib/common.sh` finds the helper's line, so the
   grep is capable of a hit.
9. **Unpack and prune safety.** Confirm the member listing, the refusal of absolute and `..` members,
   `--no-same-owner`, and that prune matches the version regex, never `current` or `PREVIOUS`, and
   uses `--`.
10. **The outbound request.** Read the `curl` argv in `release-verify.sh`: `--proto =https`, no `-k`,
    `--fail`, `--disable`, `--tlsv1.2`, `--max-redirs 3`, connect and total timeouts, no identifier in
    the URL or headers.
11. **The stale Ed25519 statements.**
    `grep -rnE 'Ed25519' rules/security.md installer/lib/50-artifacts.sh docs/superpowers/specs/2026-08-29-maran-design.md scripts/lib/release-bundle.sh`
    after the change must leave the `50-artifacts.sh` comments corrected and the rule and spec lines
    as they are, flagged to the owner.
12. **The gates.** `maran structure`, `maran test` (the whole suite, verdict line `TEST VERDICT: OK`
    and the total against the baseline, never the exit code), and the installer polygon for update and
    rollback on both families.

## What the author could not verify

Nothing in this note or the code it describes has been run on a real host when this note was written.

- No update, rollback or adoption has run on a real server or in the polygon. Every behaviour
  above is a requirement, not an observation.
- The real release host (`releases.maran.innovayse.com`) was not exercised. Its manifest does not yet
  carry a `channel` field and no signed manifest from it has been seen by this session; the first real
  manifest decides whether the "missing field" refusal is right for the published releases.
- AlmaLinux, Rocky and RHEL specifics: SELinux enforcing (labels on `releases/`, the swapped link and
  `/var/backups/maran-update`), the `runuser`, `flock`, `mv -T` and `curl` versions on each family, and
  PostgreSQL 13 (AlmaLinux 9) against 16 (Ubuntu 24.04) custom-format dump compatibility. The
  polygon containers do not enforce SELinux.
- That no agent RPC writes under `/usr/local/maran` (section 3): argued from the closed command set
  and the unit's `ProtectSystem=true`, not from a measurement.
- The behaviour of the CLI killed at each point in the table in section 6 on a booted systemd host.
- A clock that jumps (the UTC stamp orders the dumps), a disk that fills mid-unpack, and a power loss
  between the rename and the fsync of the directory.
- Whether a production `panel.env` loads correctly into the migration child by the method chosen.

## Honest ordering

This note is written BEFORE the code, as `rules/security.md` requires. The design contract it argues
from was written first and is what the implementing agents are given; this note adds the attacker's
view of it, the requirements that view produced (items marked "requirement" above, among them the
interrupted-update recovery of section 6, the pre-download space floor of section 8 and the unpack
checks of section 2, none of which the design file states), and the debts. If the code differs, the
code or this note is amended in the same change, and the amendment is appended below under a dated
heading rather than rewriting the text above.

## Correction, 2026-11-04, after review: what the reviewer found and what changed

A review of the first implementation found blockers. Each is fixed in the code and, where the text
above described the wrong behaviour, in the sentence itself (marked "Correction, 2026-11-04, after
review"). A note that describes behaviour the code lacks is a defect, so the list is kept here.

1. **`panel.env` was described as "not sourced into the root shell", and it is sourced.** It is
   sourced inside a subshell, as root, by one helper (`cli_run_as_service`), exactly as
   `installer/lib/65-schema.sh` and `cli_dotnet` always did. The claim was false and the reviewer
   check that depended on it (item 8) contradicted the design. Now: one helper, a root-owned 0640
   file in a directory the service group cannot write, a validated value alphabet, and
   `cli_require_trusted_env_file` refusing to source when owner or mode differ (section 5).
2. **The installer carried a second copy of the verify and unpack code.** It now SOURCES the CLI's
   `release-version.sh`, `release-verify.sh`, `release-unpack.sh` and `release-layout.sh`; the key
   is an argument from the installer and a baked default in the CLI; there is one home for
   verification, unpack and layout (section 1).
3. **The offline tarball was vetted by path and extracted by path.** It is now copied once into the
   root-0700 work directory, its parent directory must be root-owned and not group/world-writable,
   and only the copy is vetted and extracted (section 1).
4. **The second sha256 check was the caller's job.** It is inside `cli_layout_install_release`,
   immediately before the unpack (section 1).
5. **An abandoned release made its version unretryable, and a `legacy-*` host had no valid rollback
   target.** Abandoned releases (neither current nor PREVIOUS) are replaced; `legacy-*` names are
   valid release names, valid rollback targets, and compare lowest (sections 1 and 2).
6. **The consistency check failed open** (an unresolvable `current`, or a `|| true` on a read, passed
   as consistent). It is fail-closed, returns non-zero rather than calling `cli_fail`, and readers
   never swallow a failure (section 6).
7. **The ambient environment reached `openssl`, `curl`, `tar` and the database client.**
   `common.sh` unsets `OPENSSL_CONF`, `SSL_CERT_*`, `CURL_*`, `TAR_OPTIONS`, `LD_*` and `PG*`; `curl`
   carries `--disable`, `--tlsv1.2` and `--max-redirs 3` (section 1).
8. **Version validation and the dump name were spelled in several libraries.** They now have one home
   each, `release-version.sh` and `update-dumps.sh` (sections 1 and 4).

Accepted residue, stated so it is not mistaken for coverage:

- There is no fsync-less gap in the state write: the state file and its directory are `sync`ed. The
  window a power loss can still use is the one in "What the author could not verify" (a loss between
  the rename and the sync of a directory the kernel has not yet flushed on a filesystem that
  reorders), which no test here exercises.
- Symlinks are refused in release trees, so a hostile archive cannot plant one; the refusal is by
  member listing, and a symlink created in the tree AFTER the unpack would need root.
- A kill during legacy adoption is resumed by the next run; a kill is still a kill, and the table in
  section 6 is an argument, not a measurement on a booted host.
- `panel.env` is sourced as root. The control is the trusted-file check and the validated alphabet; it
  is not a sandbox, and a root-owned file with hostile content would still run.
- The restore (issue #386) now issues `DROP SCHEMA ... CASCADE` for every schema except `public` and
  the system ones, as the service account, in the same psql transaction that replays the dump, so
  objects a newer release created are removed instead of making the restore refuse. The surface is
  still only `--restore-database` after its confirmation, and the statement runs as the panel's own
  role over its own database; a failure rolls back all of it. A reviewer must check that the
  rendered script is written whole before psql reads it (a truncated script must never be committed)
  and that the script file lives only in the root-only stage directory. Not verified: the polygon
  assertion for this (a schema and two tables added after the dump) was written and not run here.

Unchanged by this correction, and stated so that nothing above is read as discharging them:

- **Second reviewer: OUTSTANDING (rules/security.md).** The branch `fix/128-panel-self-update` MUST NOT
  merge to `main` until a human reviewer has read it. The review above was an in-session review by
  agents, which is the author-side check the rules say is not the second reviewer.
- **Owner decision on the outbound call (section 9): NOT recorded.** The owner has not approved it and
  the spec has not been amended; the pull request must say so beside the outstanding review.
- The stale Ed25519 statements (section 10) remain the owner's decision to reconcile.
