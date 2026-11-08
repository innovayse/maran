# Threat note: backup encryption at rest (issue #160, local destinations)

`rules/security.md` requires a threat note, and a second reviewer, for a change to the agent's
privileged surface and for anything that holds a key. This change does both: it adds a cipher to the
root agent's backup area, a key to the panel's database, and a key on the agent's gRPC contract.

**Second reviewer: OUTSTANDING (#331).** The work is done by an agent session, which cannot obtain
one. A note by the author of a change closes nothing; it gives the reviewer an argument to read
instead of a diff. The branch MUST NOT merge to `main` until a reviewer has checked the list at the
end.

**When this was written.** The brief asked for this note first. It was written after the first draft
of the code was in the working tree and before any build, test or review. The decisions it records
(primitive, custody, nonce scheme) were taken from `research_notes/owner-decisions.md` item 16 and
fixed before the code; the "why it is safe" sections below were written against code that already
existed, so read the measured and cited paragraphs with more weight than the argued ones. NOTHING in
this change has been compiled or run: the lead's gate is the first execution.

## Scope

Only the LOCAL destination. The S3-compatible destination is not wired in this build and the owner has
left its decision pending; nothing here is reachable from it (`validated_destination` refuses an S3
destination whole, before the key is read). Surfaces:

- Agent: `agent/crates/ops/src/backup/archive/{encrypted_artifact_header,encrypt_artifact,
  decrypt_artifact,is_encrypted_artifact}.rs`, `backup/{seal_partial,open_readable_artifact}.rs`, the
  three entry points `create_backup`, `restore_backup`, `verify_backup`, and
  `agent-core/src/validation/secrets/archive_key.rs`.
- Contract: `BackupDestination.archive_key` (9), `CreateBackupOk.encrypted` (4),
  `ReadableBackup.encrypted` (4). Additive; the proto revision bump is the lead's.
- Panel: `BackupDestination.EncryptArchives` and `.ArchiveKey` (new columns, expand-only),
  `PUT /api/v1/backup-destinations/{id}/encryption` (administrator only), the resolver's two views of
  a destination.
- Dependencies: `chacha20poly1305` 0.11.0 (and what it brings: `aead`, `cipher`, `poly1305`,
  `universal-hash`), `getrandom` 0.4.3 (already in the lockfile).

No new daemon, listening port, outbound call or allow-listed binary (rules/security.md item 10).

## Decisions taken, and the owner decisions they apply

From `research_notes/owner-decisions.md` item 16 (the plan's recommended option, narrowed by the
instruction that S3 is out of scope):

1. **Policy: optional, per destination, off by default.** The plan recommended "mandatory for remote
   only"; with no remote destination in scope, the applicable arm is the optional one. The 2026-09-05
   backups threat note decided AGAINST client-side encryption because a lost key makes every backup
   useless when it is needed. That risk is real and is not removed here; it is made explicit and
   opt-in, with the custody below.
2. **Primitive: in-process AEAD, not `gpg`.** `gpg` would add a binary to the agent's allow-list and a
   platform literal for both families (rules/architecture.md). The cipher is XChaCha20-Poly1305 and
   not AES-GCM (which the panel itself uses for columns) for one reason: the 192-bit nonce. See
   "Nonces".
3. **Key custody: panel-held, encrypted at rest, recoverable through the panel recovery archive**, sent
   per call and kept by the agent for the length of the call only.

## The attacker, and the claim

The claim is narrow. **A copy of a sealed backup file, taken off the server or read from an image of
the backup volume, without the panel database and `/etc/maran/panel.env`, discloses nothing about the
contents of the account's home or its database dumps, and cannot be altered without detection.** It is
NOT a claim about root on the server, the panel host's memory, or anyone holding the panel's recovery
archive and its passphrase: they hold the key.

## What an attacker could do, and why it is safe

1. **Read a backup that left the machine.** The file is ciphertext under a 256-bit random key. The
   key is not derived from anything guessable (`ArchiveKeyGenerator` draws 32 bytes from
   `RandomNumberGenerator`), and is not in the file, in the sidecar, in any log or in any API
   response. Residue and limits are R1 to R3 below.
2. **Replay or reuse a nonce.** The nonce of chunk `n` is `prefix(16 random bytes, per archive) ||
   n(8 bytes, big endian)`. Within one archive `n` never repeats (an overflow past 2^64 chunks is
   refused). Across archives a collision needs two equal 128-bit prefixes: 2^-128 per pair. With a
   96-bit AES-GCM nonce the same scheme would leave 32 random bits and a collision after tens of
   thousands of backups under one long-lived key, which is the reason for the extended-nonce cipher
   and for no key-derivation step. UNVERIFIED BY MEASUREMENT: no test draws many prefixes; there is a
   test that two draws differ. A reviewer should check that nothing else writes under this key with a
   different nonce scheme.
3. **Truncate, extend, reorder or edit a sealed file.** Every chunk is authenticated; the associated
   data is the whole header plus a last-chunk flag (1 for the final chunk, 0 for the rest). A file cut
   at a chunk boundary ends on a chunk sealed with flag 0 and is refused when opened with flag 1; a
   file with a chunk appended is refused the other way; swapped chunks fail on the counter in the
   nonce; any flipped bit, in the header too, fails. Each of these has a named test in
   `decrypt_artifact_tests.rs`.
4. **Downgrade a sealed backup to a plain one.** A reader decides by the file's own first bytes
   (`MARANENC`), never by the sidecar. Replacing the artifact with a plain archive is a root action and
   is refused by the digest the panel recorded (restore compares the panel's SHA-256, taken over the
   stored bytes, before anything is read). `verify_backup` additionally refuses a sidecar whose
   `encrypted` label disagrees with the file. The restore path does NOT compare the label: it relies on
   the panel's digest.
5. **Make the root agent allocate or write without bound.** A frame's length is read before its bytes
   and refused if it is under a tag or over 1 MiB + 16; the reader holds one frame. The decrypted copy
   can never be larger than the stored file, and its room is asked of the scratch before the first
   byte is written (`require_scratch_room`). A wrong key fails at the first chunk, so it costs one
   chunk of work.
6. **Read the key from the panel.** The key is a column under `EncryptedStringConverter` (AES-GCM, the
   panel's `Security__EncryptionKey`). It is on no DTO and no response (`PUT .../encryption` answers
   204), no audit entry, no log. The agent client wraps it in `SensitiveString`, so a formatted
   destination prints `[redacted]` (`Formatting_a_local_destination_never_prints_the_archive_key`); the
   client's error redaction also names it. The panel process can read it (it must, to send it): a
   compromise of `maran-api` yields the key, but not the artifacts, which are `root:root 0600` in a
   `0700` directory. An attacker needs both. This is the same split the 2026-09-05 note found holds for
   the plaintext dumps, with the same caveat about the panel owning `/var/lib/maran`.
7. **Read the key from the agent.** The key crosses the agent's unix socket in a protobuf `string`, the
   same way S3 credentials were designed to. The agent parses it into `ArchiveKey` (`Debug` prints
   `«redacted»`, no `Display`, no `Clone`, no serde) and drops it with the call (best-effort zeroing
   on drop, stated in the code as nothing stronger). The generated request struct derives `Debug` and
   so would print the key if anything logged a request. NOT FOUND: any such logging (the audit layer
   builds lines from validated types only, rules/rust.md "Logging"), but absence of a log call is not
   a proof, and a reviewer should grep the agent for request formatting.
8. **Make the agent seal under a key it should not have used.** The panel hands a creation a
   destination that carries the key only while `EncryptArchives` is on; reads carry it whenever the
   destination has one (`BackupDestinationMapper.ForNewBackup` / `ForAgent`). The failure of a wrong
   choice is in the safe direction on reads (an error) and in the visible direction on creation (a
   plain archive where a sealed one was wanted), which is why creation has its own method, its own
   resolver property and a test for each.

## Residue and limits (stated, not hidden)

R1. **A plain archive exists during creation.** `tar` writes the plain `.partial` into the root-only
   backup directory; the sealed copy is written beside it and renamed over it. The plain blocks are
   freed, not overwritten, and on a copy-on-write filesystem could not be overwritten reliably. So a
   disk image of the backup volume taken AFTER a run can contain the plain archive in free space.
   Protecting that needs full-disk encryption under the volume. The fix inside the product is to pipe
   `tar` into the sealer (no plain file at all); it was not done because the archiver runs under the
   bounded-spawn core with a deadline and process group, and changing its output path blind was a
   larger risk than the residue it removes.
R2. **A plain copy exists during restore and verification.** A sealed archive is decrypted once into the
   root-only scratch and read from there, because `tar` is the thing that reads archives and it cannot
   open a sealed file. The copy is `0600`, in a `0700` root-only chain the operation already proved
   (`open_restore_scratch`), removed on every path by the existing cleanup, and anything left by a kill
   is removed at the next start by the reaper (which removes everything but `rollback/`). The cost is
   disk: a second copy of the archive in the scratch (verification: in `verify/<id>`).
R3. **The sidecar is not encrypted.** It lists the account, database names and sizes, home size, times,
   agent version and the digest. It must stay readable because the listing and retention read it
   without a key. File names are inside the archive, not the sidecar.
R4. **Losing the key loses the backups.** One copy matters: the database row, recoverable only with
   `panel.env`, both inside `maran panel-backup`'s archive. No escrow, no second key, no rotation
   (a rotation that overwrote the value would strand every backup sealed under it; it would need a key
   identity on each backup). Documented in `docs/backups/encryption.md` in those words.
R5. **No standalone decryptor ships.** An operator with no panel and no agent cannot open a sealed
   backup with `tar` and `gzip` as they can a plain one. The format is specified in the doc so a tool
   can be written.
R6. **Wrong key reaches the customer as "archive rejected".** The agent distinguishes missing key
   (`INVALID_INPUT`) from a key that does not open (`VALIDATION_FAILED`); the panel's translators map
   both to the existing validation codes. A dedicated error code would be a `common.proto` change,
   left for the lead.

## What the author could not verify

- **Nothing has been compiled or run.** The API of `chacha20poly1305` 0.11 / `aead` 0.6 was read from
  the downloaded crate source, not exercised: `KeyInit::new_from_slice`, `Aead::{encrypt,decrypt}` with
  `Payload`, `XNonce::try_from(&[u8])`. `Cargo.lock` has not been regenerated; `cargo audit` has not
  been run against the new crates; `THIRD-PARTY-NOTICES.md` has not been regenerated.
- **Interoperability with an independent implementation.** The framing is this repository's own
  composition of a standard AEAD (a STREAM-style chunked construction); it has not been checked against
  a second implementation. The associated-data and last-chunk-flag design is the part to review.
- **Behaviour at the 64 GiB ceiling and on a 4 GB-plus file.** Chunking is bounded by construction; no
  test seals more than a few mebibytes.
- **The EF migration and snapshot were written by hand**, not generated; `maran migrate check` is the
  test.

## What a reviewer must check (OUTSTANDING)

1. The AEAD usage in `encrypt_artifact.rs` / `decrypt_artifact.rs`: nonce construction, associated data,
   the last-chunk lookahead (a chunk is last exactly when nothing follows it), the frame length bounds.
2. That no path reads a sealed artifact except through `open_readable_artifact` (grep the backup area
   for `list_members`, `extract` and `checksum_file` callers; the digest is over STORED bytes by design).
3. That the key reaches no log, error text or response (agent and panel), including the generated
   request's `Debug`.
4. That `BackupDestinationMapper.ForNewBackup` is what every CREATE call site uses
   (`CreateBackupCommandHandler`, `BackupRunHandler`, `SafetyBackupService`) and `ForAgent` every read.
5. The dependency additions (rules/security.md item 11): version pins, `cargo audit`, licences, what
   `chacha20poly1305` 0.11 pulls in.
6. The accepted residue R1 and R2 above, and whether the owner accepts it for v1.
