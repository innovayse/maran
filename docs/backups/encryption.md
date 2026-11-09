# Backup encryption at rest

What it protects, how the key is handled, what you lose if the key is lost, and what it does not
cover (issue #160). Local destinations only: the S3-compatible destination is not wired in this
build, and nothing here applies to it.

## What it is

An administrator can switch encryption on for a backup destination. From then on every backup
written there is sealed by the server agent before it is hashed and published: the archive that
rests under `/var/backups/maran/<account>/` is ciphertext, and so is everything the panel records
about it (size and digest are those of the sealed file). It is off by default and per destination.

- **Cipher**: XChaCha20-Poly1305 (an AEAD), the `chacha20poly1305` crate, in-process in the root
  agent. No `gpg`, no new binary on the agent's allow-list.
- **Key**: 256 random bits, made by the panel the first time encryption is switched on, held by the
  panel and sent to the agent with each call that seals or opens an archive. The agent keeps no key
  and cannot open a sealed archive unless it is handed one.
- **Nothing else changes**: the same artifact name, the same sidecar, the same digest check at
  restore, the same retention. A backup taken before encryption was switched on is a plain archive
  and stays restorable exactly as before; the agent tells the two apart by the file's own first
  bytes, never by a label.

## Switching it on

`PUT /api/v1/backup-destinations/{id}/encryption` with `{"enabled": true}` (administrator only;
answers 204). The first time, the panel makes the destination's key. The key is never returned by
any endpoint, never written to a log, an audit entry or a personal-data export, and never shown to
anybody. Switching encryption **off** (`{"enabled": false}`) stops sealing new backups and keeps the
key, so the backups already sealed remain restorable; switching it on again reuses the same key.
Every change is in the audit journal under `BackupDestinationEncryptionChanged`; the entry names
the destination and nothing about the key.

## Key custody

The key lives in the panel database, in the `ArchiveKey` column of `backups."BackupDestinations"`,
encrypted with the panel's own key (`Security__EncryptionKey` in `/etc/maran/panel.env`), the same
arrangement as the TOTP secrets and the SMTP password.

**There is exactly one copy of the key that matters, and it is inside the panel's own recovery
archive.** `maran panel-backup` archives the database dump and `/etc/maran` (which holds
`panel.env`) under a passphrase. That archive, with its passphrase, is how a sealed backup is read
on a rebuilt server: restore the panel, and the destination's key comes back with it.

> **If the database row and the panel key are both lost, every sealed backup of that destination is
> permanently unreadable.** There is no recovery, no escrow and no second key. Keep the panel
> recovery archive and its passphrase somewhere that survives the server, and test restoring it
> (docs/operations/panel-disaster-recovery.md).

Rotating the key is not supported in this release: one key per destination for the life of the
destination. A rotation that overwrote it would strand every backup sealed under the old one.

## What it protects

A copy of the backup files taken off the server, or read from a disk image of the backup volume,
without the panel database and `panel.env`: the contents of the account's home and the database
dumps are not readable, and a flipped bit, a removed or reordered chunk or a truncated file is
detected when the archive is opened.

## What it does not protect

- **Someone with root on the server, or with the panel database and `panel.env`.** Root can read
  the key; the database and `panel.env` are the key. This is protection for backups that leave the
  machine, not for the machine.
- **The sidecar and the listing.** `<id>.meta.json` is not encrypted: it names the account, the
  database names and sizes, the home size, the creation time, the agent version and the digest.
  File names inside the home are not in it.
- **Free space on the backup volume.** Between the archiver finishing and the sealed file replacing
  it, the plain archive existed on the volume, root-only. After the swap its blocks are freed, not
  overwritten. A volume whose free blocks must stay private needs full-disk encryption beneath it.
- **Restore and verification leave a plain copy for their duration.** A sealed archive is decrypted
  once into the agent's root-only scratch (`/var/lib/maran-scratch`) and read from there; the copy
  is removed when the operation ends, and anything an interrupted run left is removed when the agent
  starts. The scratch therefore needs room for a second copy of the archive.
- **Disk during creation.** Sealing writes a second file next to the plain one, so a backup needs
  room for roughly twice the archive on the backup volume at its peak.

## Failure modes an operator will see

| Situation | What happens |
|---|---|
| Restore or verify of a sealed backup, and the destination has no key | The agent refuses before extracting anything (`ERROR_CODE_ARCHIVE_UNOPENABLE`, "the archive is encrypted and no key was supplied"). The account is untouched. |
| Restore or verify with a key that does not open it | The agent refuses before extracting anything (`ERROR_CODE_ARCHIVE_UNOPENABLE`, "the archive cannot be opened with the key supplied for its destination"). The digest is checked first, so a wrong key is the likelier cause, but the cipher cannot tell a wrong key from a damaged sealed file, so the code and the sentence say only that the archive does not open with this key. The account is untouched. |
| Sealing fails (full disk, no randomness) | The creation fails whole: no artifact, no sidecar, no `.partial`. |
| The sidecar says plain and the file is sealed, or the reverse | A verification refuses the backup as a record that disagrees with its artifact. |

The panel shows a missing or wrong key as its own failure: a restore answers "could not open this copy
with the encryption key held for its backup destination" (`RestoreArchiveKeyRejected`, HTTP 400)
instead of the general refused-copy sentence, and a verification records "cannot be opened with its
destination's encryption key" (`BackupVerificationArchiveKeyRejected`). Agents older than this change
answered `ERROR_CODE_INVALID_INPUT` (no key) and `ERROR_CODE_VALIDATION_FAILED` (key does not open it);
the panel shows those as before.

## The tested restore

Encryption is only worth having if the restore works, and the check is built into the product: a
verification (`POST /api/v1/backups/{id}/verify`) of a sealed backup opens it with the destination's
key, lists it and reads its manifest, so a key that cannot open the archive shows up as a failed
verification on the day the backup is taken (verification runs after every completed run), not on
the day it is needed. A restore of a sealed backup is the same restore as for a plain one.

## The sealed file format (version 1)

Documented so that a rescue tool can be written without the agent; none ships today.

```
header (25 bytes)  "MARANENC" | version (1) | nonce prefix (16 random bytes)
frame, repeated    length (4 bytes, big endian) | ciphertext+tag (length bytes, 16 <= length <= 1 MiB + 16)
```

Each frame holds up to 1 MiB of the plain `.tar.gz`, sealed with XChaCha20-Poly1305 under the
destination's key. Nonce of frame `n` = nonce prefix || `n` as 8 big-endian bytes. Associated data =
the 25 header bytes followed by one byte, `1` for the last frame of the file and `0` for every other.
An empty archive is one frame holding only a tag. The plain archive is an ordinary gzip-compressed
tar of `manifest.json`, `home/…` and `databases/…`.
