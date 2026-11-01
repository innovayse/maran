# Threat note: panel disaster recovery — `maran panel-backup` and `maran panel-restore` (issue #129)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to how secrets are handled. This change gives the server's root user
two verbs of the break-glass `maran` command: one writes an encrypted archive holding the panel's
database, its encryption and signing keys, its licence and its configuration; the other restores such
an archive onto a clean install. Both handle the panel's most valuable secrets, so this note argues
why the shape is safe and says what is not proven.

**Second review: OUTSTANDING — must not merge to main.** The work was carried out by an agent
session, which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human
reviewer has read it. The owner is told at hand-off that this surface carries an outstanding review.

## The problem being solved

Account backups cover customer files and databases only. If the server dies, nothing restores the
panel's own PostgreSQL data or `Security__EncryptionKey`, the key every encrypted column was written
with — so even a database copied off the dead disk by hand cannot be read on a new server. The
recovery path is a command run by root, on the server, because root is who holds both halves.

## Shape

- `maran panel-backup --passphrase-file FILE [--out DIR]` writes
  `maran-panel-backup-<UTC>-<pid>.tar.gpg`, 0600 root:root, into a root-owned directory nobody else
  can write (default `/root`).
- `maran panel-restore --passphrase-file FILE --replace-this-panel ARCHIVE` stops the panel,
  restores the database, writes the two keys into this host's `panel.env`, restores the licence,
  brings the schema forward with `Maran.Host --migrate`, and starts the panel again.
- No new RPC, daemon, port, outbound call or package (security.md item 10). `gnupg` and `openssl`
  are already installed by `installer/lib/20-dependencies.sh` on both families; `pg_dump` and
  `pg_restore` ship with the PostgreSQL server step 30 installs.

## What is inside the archive, and why each part

| Part | Source | Restored automatically |
|---|---|---|
| `database.dump` | `pg_dump --format=custom` of the panel database, every schema (Wolverine's included) | yes |
| `etc-maran.tar` | `/etc/maran` whole: `panel.env`, the panel TLS key, rendered configs | only the two key lines of `panel.env` |
| `var-lib-maran.tar` | `/var/lib/maran` whole: the licence, ACME certificate material | only `licence.json` |
| `manifest.txt` | format version, UTC creation time, database name | read, never executed |

The two configuration trees travel whole so nothing an operator needs is missing on the day it is
needed; only what a fresh install cannot regenerate is put back automatically. Everything else in
`panel.env` describes the NEW host (its socket's peer uid, its ssh ports, its host name) and is the
new install's to write, which is why the restore replaces two named lines instead of the file.

## Secrets at rest — the archive

- **Encryption is gpg's, not ours** (security.md item 9): `gpg --symmetric --cipher-algo AES256`
  with an iterated-and-salted S2K over SHA-512 at gpg's maximum count, and gpg's integrity
  protection, which a gpg of the supported families requires on decryption — a modified archive is
  refused, not decrypted into garbage that is then restored.
- **The passphrase never touches argv or the environment.** It reaches gpg through
  `--passphrase-file`, read by gpg itself. The file must be a regular, non-symlink file owned by
  root, readable by nobody else (mode 0600 or 0400), and at least 20 characters long; anything else
  is refused before anything is written. A one-off `GNUPGHOME` inside the root-only staging
  directory keeps gpg's agent and keyring state away from root's own.
- **The plaintext exists only in a root-only staging directory** (`/var/lib/maran-cli-staging`,
  checked to be root-owned 0700 rather than trusted to be, the same gate `collect-diagnostics`
  applies), and an EXIT trap removes it on every exit — success, refusal, failure and the signals
  that are turned into exits. A `kill -9` or a power cut during a backup leaves the plaintext there
  until the next run of either verb, which clears stale stage directories first. Stated, not hidden.
- **The output is created with `O_EXCL`** (`set -C`) under `umask 077`, so a planted file or symlink
  at the target is refused rather than followed, and the archive is 0600 from its first byte.

## Who reads what

- `pg_dump` and `pg_restore` run as the `maran` service account through `runuser`, over the unix
  socket with peer authentication — the identity that already owns every object in the database.
  Root opens the dump file and hands the descriptor over, so the service account never gains write
  access to a root directory and root never authenticates to PostgreSQL. `--migrate` runs as `maran`
  too, exactly as `installer/lib/65-schema.sh` runs it.
- Root reads `/etc/maran` and `/var/lib/maran`. Root could already read both; the command adds no
  reach, only a supported path that leaves an audit line (`logger -t maran-cli`, verb and outcome,
  never an argument or a secret).

## Restore: what it overwrites, and what it trusts from the archive

- It is destructive by design: the panel database is replaced (`pg_restore --clean --if-exists
  --single-transaction --exit-on-error`, so a failure rolls the whole restore back and the fresh
  database is left as it was) and the two keys are replaced. It refuses to run without
  `--replace-this-panel`, a word nobody types by accident.
- **Nothing from the archive is executed or interpreted by the shell.** The manifest is matched line
  by line against fixed keys and a fixed format version. Inner tar members are extracted only by
  exact name (`./panel.env`, `./licence.json`) into a fresh private directory, never over `/`, so a
  member named `../../etc/cron.d/x` or a symlink member is never written where the archive says.
- **The key values are validated before they are written** (security.md item 4): each must be
  base64 of the panel's alphabet only, 44 characters or more, so no newline, carriage return or
  control character can reach `panel.env` and turn one line into several. The new `panel.env` is
  written beside the old one, given root:maran 0640, and renamed into place, so a failure leaves
  the old file whole.
- `licence.json` is installed maran:maran 0640, the mode `FileLicenceWriter` gives it, and only when
  it is a regular file in the archive.

## What an attacker could do with this surface

- **Steal an archive.** Without the passphrase they hold AES-256 ciphertext behind a salted,
  iterated S2K. With it, they hold the panel: every administrator's password hash, every customer
  row, and the key that decrypts every stored secret (SMTP password, S3 credentials, provisioning
  keys). The archive is therefore exactly as sensitive as root on the server, and the operator is
  told so when it is written. A weak passphrase is the attacker's best route; the 20-character
  floor is a floor, not a guarantee.
- **Plant an archive.** Someone who can make root restore an archive of their making owns the panel
  — but they must already have the passphrase and root's cooperation, and root can already replace
  the panel binary. The restore refuses an archive that is not a regular, non-symlink file owned by
  root, so an unprivileged user cannot swap one into a path root names.
- **A process running as `maran` gains nothing.** The wrapper is 0700 root:root and refuses a
  non-root caller with 77 before it reads anything; neither verb installs a setuid file, a sudoers
  entry, a socket or a group membership.

## What the author could not verify — for the reviewer

1. The full round trip on a real host — back up a panel with accounts and logins, install fresh,
   restore, sign in with the old administrator and read a decrypted secret — is the issue's closing
   criterion and has NOT been run in this session (no VM was available to it). The polygon
   assertions run the new verbs only as far as their argument and permission refusals.
2. That `pg_restore --clean --if-exists` against a database `65-schema.sh` has just migrated drops
   and recreates every object without an ownership error, on PostgreSQL 13 and 16.
3. That gpg on every supported family refuses an archive whose integrity packet is missing or
   wrong (measured nowhere here; gpg 2.2.8 and later are documented to).
4. That restoring an archive taken by a NEWER release onto an older install is refused somewhere:
   it is not refused by this command, which documents "same or newer release" instead.
5. SELinux contexts on files the restore writes on AlmaLinux (`panel.env`, `licence.json`): the
   restore runs `restorecon` on them when it is installed, and that has not been observed on an
   enforcing host.
