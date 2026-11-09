//! VerifyBackup: reading one published artifact back to learn whether it is
//! still the artifact that was written.

use std::collections::HashSet;
use std::fs::remove_dir_all;
use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::secrets::archive_key::ArchiveKey;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::dump_database::dump_path;
use crate::backup::archive::is_encrypted_artifact::is_encrypted_artifact;
use crate::backup::archive::read_manifest::read_manifest;
use crate::backup::archive::scan_members::scan_members;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_root::open_account_directory;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::backup_verification::BackupVerification;
use crate::backup::model::sidecar_claim::SidecarClaim;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::open_readable_artifact::open_readable_artifact;
use crate::backup::open_restore_scratch::open_restore_scratch;
use crate::backup::read_sidecar::read_sidecar;
use crate::backup::require_sidecar_agrees::require_sidecar_agrees;
use crate::backup::verification_placement::VerificationPlacement;

/// The uid every file and directory a verification reads or stages in must
/// belong to on a real host.
const ROOT_UID: u32 = 0;

/// The length of a SHA-256 digest written as hexadecimal.
const SHA256_HEX_LENGTH: usize = 64;

/// The archive's directory of SQL dumps, as a member prefix.
const DATABASES_DIRECTORY: &str = "databases";

/// Reads the artifact `backup_id` names for `account` under `root`, together
/// with its sidecar, and answers what that read established.
///
/// **Read-only and idempotent.** Nothing is created, changed or removed under
/// the backup root, in any account's home or in any database; repeating the call
/// changes nothing on the host. The one thing written anywhere is a manifest of
/// a few kilobytes extracted into a root-only scratch that is removed on every
/// path out, including a refusal and a bound.
///
/// # What a success establishes
///
/// At the moment of the read, all of these held, in this order:
///
/// 1. The artifact and its sidecar are plain files owned by root with one link
///    each, reached through the account's directory after the same inode checks
///    a restore and a delete make of the root and of that directory.
/// 2. The sidecar parses, is of a version this agent understands, and records a
///    digest that is 64 lowercase hexadecimal characters.
/// 3. The artifact's SHA-256, computed here by streaming the whole file, equals
///    the sidecar's `artifact_sha256`, and its size equals the sidecar's.
/// 4. The member listing was read whole, within the bounds a restore applies,
///    and every member is inside the archive's fixed layout (`scan_members`, the
///    very pre-scan a restore runs; a refusal fails the verification entire).
/// 5. The manifest, extracted into the root-only scratch, is a plain file, is
///    within its size ceiling, parses, is of a known version, names `account`
///    AND names `backup_id`, and agrees with the sidecar (`require_sidecar_agrees`).
/// 6. Every database the manifest names is a name an account could own, and its
///    dump member `databases/<name>.sql` is in the listing.
///
/// # What it deliberately does not establish
///
/// - **That the SQL loads.** No dump is parsed, loaded or interpreted: a dump is
///   text a loader runs as the database superuser, and a root reader that
///   "parses" it would be a second, unreviewed SQL front end.
/// - **That a dump's bytes match the digest the manifest recorded for it.** The
///   existing archive code can compute that only by extracting the whole
///   `databases/` member into the scratch (it is what a restore does), which for
///   a large estate writes gigabytes after every backup. It is not done here and
///   is not faked: the artifact's own digest in step 3 covers every byte of every
///   dump, because the sidecar's digest was taken over the finished file, so a
///   damaged dump already fails there. What a per-dump check would add is only
///   catching a defect in creation itself, and that is the check creation makes
///   before it publishes. `database_count` therefore counts dumps whose PRESENCE
///   was established, and says nothing more.
/// - **That a dump member is a plain file.** The listing is names only, and a
///   verbose one that carried member types would need a second bounded read that
///   no existing unit provides. A dump is never read here, so a link wearing its
///   name is inert to a verification; a restore extracts it and refuses it at the
///   digest it checks before the first drop.
/// - **Anything about a coordinated replacement.** `require_sidecar_agrees`
///   compares the sidecar with the manifest inside the archive, and someone who
///   can write root-owned files can make an artifact, its sidecar and its
///   manifest agree with each other. This detects corruption, accidental damage
///   and the cases where only one of the three was changed; a reader must not
///   take a success as more than that.
/// - **Restorability.** That the databases exist in a form to be restored into,
///   that the host has room, that a remote copy exists, or that the artifact is
///   unchanged tomorrow. A success is "verified at this moment", never "safe".
///
/// # The lock, and why it refuses rather than waits
///
/// This takes `account`'s backup lock FIRST, as [`delete_backup`](crate::backup::delete_backup)
/// does, so it never reads an artifact a restore is replacing or a delete is
/// removing, and answers [`BackupError::AlreadyRunning`] when another operation
/// holds it. It does not wait: a verification after a run is unattended work,
/// and a wait held behind minutes-long work stalls the panel. The lock is
/// process-local, as every lock in this agent is.
///
/// # Bounds
///
/// The artifact is hashed in fixed chunks and never read whole. Every `tar` run
/// goes through the bounded spawn core under its call class, with the capped
/// drain; the listing bounds are the restore's own, unchanged, so an archive
/// that verifies is not one a restore would refuse for its size or the reverse.
/// A bound is a refusal ([`BackupError::Bound`]) and never a smaller answer.
///
/// # Cancellation class
///
/// **Runs to completion; a dropped caller's result is discarded.** The work is
/// read-only, so there is nothing to reclaim and no half state to leave; it runs
/// inside `spawn_blocking` like every unit of host work here, so a dropped
/// future cancels nothing, and stopping it early would only waste the minutes
/// already spent hashing. The scratch is removed by this function on every path
/// regardless, and one left by a kill is removed at the next start by the
/// startup reaper, which deletes everything under the bulk root but `backup/`.
///
/// # Errors
///
/// Two kinds, kept apart because the panel acts on them differently.
///
/// **A finding about the artifact** (the artifact was read and is not what it
/// should be): [`BackupError::ChecksumMismatch`], [`BackupError::ArtifactSizeMismatch`],
/// [`BackupError::ArtifactNotRegularFile`], [`BackupError::SidecarUnreadable`],
/// [`BackupError::UnexpectedArchiveMember`], [`BackupError::ManifestUnreadable`],
/// [`BackupError::ManifestVersionUnknown`], [`BackupError::ManifestAccountMismatch`],
/// [`BackupError::ManifestBackupMismatch`], [`BackupError::ManifestDisagreesWithSidecar`],
/// [`BackupError::ManifestTooLarge`], [`BackupError::MemberNotRegularFile`] and
/// [`BackupError::DumpMissing`].
///
/// **The host could not do the read, which says nothing about the artifact**:
/// [`BackupError::AlreadyRunning`], [`BackupError::Bound`],
/// [`BackupError::FreeSpace`], [`BackupError::ChecksumUnreadable`],
/// [`BackupError::ArchiveFailed`], [`BackupError::ScratchUnusable`],
/// [`BackupError::BackupRootUnsafe`], [`BackupError::BackupRootUnusable`],
/// [`BackupError::EncryptionKeyRequired`] and
/// [`BackupError::ArtifactDecryptionFailed`]. The last two are about the key the
/// call carried: the digest was already checked against the sidecar, so a sealed
/// artifact that then refuses to open is far more likely opened with the wrong
/// key than damaged, and this function cannot tell the two apart.
///
/// # A sealed (encrypted) artifact
///
/// `key` is the archive key the panel holds for the destination, or `None`. A
/// plain artifact is verified exactly as before and the key is not looked at. A
/// sealed one is decrypted once into the verification's own root-only scratch,
/// and the listing and the manifest are read from that copy; the digest and size
/// are still those of the stored file. The sidecar's `encrypted` label must
/// agree with the file's own first bytes, or the verification is refused as
/// [`BackupError::ManifestDisagreesWithSidecar`]. The scratch needs room for a
/// second copy of the archive, and is removed on every path.
///
/// [`BackupError::NotFound`] when the account has no directory under the root or
/// the directory holds no artifact of that id; nothing is created to find out.
pub fn verify_backup(
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    root: &LocalBackupRoot,
    floor: &FreeSpaceFloor,
    key: Option<&ArchiveKey>,
) -> Result<BackupVerification, BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let outcome = match open_account_directory(root, account) {
        Ok(Some(directory)) => {
            let placement = VerificationPlacement {
                scratch_base: PathBuf::from("/"),
                scratch_root: PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
                scratch: AgentPaths::backup_verification_scratch_dir(backup_id),
                owner: ROOT_UID,
                floor: *floor,
            };

            verify_in(&placement, host, account, backup_id, &directory, key)
        }
        Ok(None) => Err(BackupError::NotFound),
        Err(error) => Err(error),
    };

    drop(guard);
    outcome
}

/// The body of [`verify_backup`], with the account's directory and every owner
/// and scratch path injected.
///
/// Split for the reason the creation's and the restore's bodies are split: a
/// test runs as an ordinary user and cannot create a directory owned by root, so
/// a body that read its own placement could be exercised only on its refusing
/// path.
///
/// # Errors
///
/// As documented on [`verify_backup`].
fn verify_in(
    placement: &VerificationPlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    directory: &Path,
    key: Option<&ArchiveKey>,
) -> Result<BackupVerification, BackupError> {
    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    if !is_plain_file_owned_by(&artifact, placement.owner)? {
        return Err(BackupError::NotFound);
    }
    if !is_plain_file_owned_by(&sidecar, placement.owner)? {
        return Err(BackupError::SidecarUnreadable);
    }

    // The sidecar is a label and the artifact is the thing: the digest is taken
    // here, over the bytes on disk now, and only compared with the label.
    let recorded = read_sidecar(&sidecar).map_err(|_reason| BackupError::SidecarUnreadable)?;
    if !is_sha256_hex(&recorded.artifact_sha256) {
        return Err(BackupError::SidecarUnreadable);
    }

    let (actual_sha256, actual_bytes) = checksum_file(&artifact)?;
    if actual_sha256 != recorded.artifact_sha256 {
        return Err(BackupError::ChecksumMismatch);
    }
    if actual_bytes != recorded.artifact_bytes {
        return Err(BackupError::ArtifactSizeMismatch);
    }

    // The sidecar's label against the file's own first bytes. A sidecar that
    // says "plain" over a sealed file, or the reverse, is a record that no longer
    // describes its artifact — the shape a downgrade or a mislabel leaves.
    if is_encrypted_artifact(&artifact)? != recorded.encrypted {
        return Err(BackupError::ManifestDisagreesWithSidecar);
    }

    let judged = judge_artifact(
        placement,
        host,
        account,
        backup_id,
        &artifact,
        &SidecarClaim {
            sidecar: &sidecar,
            artifact_sha256: &actual_sha256,
        },
        key,
    );

    // Removed on every path, written once and on the outside rather than at each
    // place the body above can return early.
    let _ = remove_dir_all(&placement.scratch);

    let (member_count, database_count) = judged?;

    Ok(BackupVerification {
        member_count,
        database_count,
        artifact_bytes: actual_bytes,
    })
}

/// Opens the scratch, reads the artifact through it, and answers how many
/// members the listing held and how many dumps the manifest named that were
/// found.
///
/// The scratch is left for the caller to remove, so that the one removal covers
/// every return below — including the early ones, which now come after the
/// scratch exists, because a sealed artifact is decrypted into it before the
/// first listing.
///
/// # Errors
///
/// As documented on [`verify_backup`].
fn judge_artifact(
    placement: &VerificationPlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    artifact: &Path,
    claim: &SidecarClaim<'_>,
    key: Option<&ArchiveKey>,
) -> Result<(u32, u32), BackupError> {
    let scratch = open_restore_scratch(
        &placement.scratch_base,
        &placement.scratch_root,
        &placement.scratch,
        placement.owner,
    )?;

    // Asked on the directory the write lands in, before the first write.
    placement
        .floor
        .admit(host.available_bytes(&placement.scratch))?;

    // The stored file itself when it is a plain archive; a decrypted copy inside
    // the scratch when it is sealed. The digest above was taken over the stored
    // bytes and is the one the sidecar records.
    let readable = open_readable_artifact(artifact, &scratch, key)?;

    // The whole listing, judged before anything is extracted; nothing of `home/`
    // is ever extracted by a verification.
    let members = scan_members(host, &readable)?;

    let databases = judge_manifest(
        host, account, backup_id, &readable, &scratch, claim, &members,
    )?;

    Ok((u32::try_from(members.len()).unwrap_or(u32::MAX), databases))
}

/// Extracts the manifest into the root-only scratch and judges it against the
/// request, the sidecar and the member listing; answers how many dumps it named
/// that were found.
///
/// The scratch is opened, and its room asked, by the caller; this only writes
/// into it.
///
/// # Errors
///
/// As documented on [`verify_backup`], for steps 5 and 6.
fn judge_manifest(
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    artifact: &Path,
    scratch: &Path,
    claim: &SidecarClaim<'_>,
    members: &[String],
) -> Result<u32, BackupError> {
    let manifest = match read_manifest(host, artifact, scratch, account.as_str()) {
        Ok(manifest) => manifest,
        // Create-side wording for a read-side fact: a manifest already on disk
        // that does not parse is a finding about the artifact.
        Err(BackupError::ManifestUnwritable) => return Err(BackupError::ManifestUnreadable),
        Err(other) => return Err(other),
    };

    // An artifact copied or renamed under another id is refused, not verified as
    // whatever it contains.
    if manifest.backup_id != backup_id.as_str() {
        return Err(BackupError::ManifestBackupMismatch);
    }
    require_sidecar_agrees(claim.sidecar, &manifest, claim.artifact_sha256)?;

    require_dumps_present(account, &manifest, members)
}

/// Requires every database the manifest names to be a name an account could own
/// and to have its dump member in the listing; answers how many there are.
///
/// A name read out of the manifest is revalidated by [`DatabaseName`] before it
/// is used for anything, including as the member name looked up, and is never
/// carried into an error.
///
/// # Errors
///
/// [`BackupError::ManifestUnreadable`] for a name no account could own;
/// [`BackupError::DumpMissing`] for a dump absent from `members`.
pub(super) fn require_dumps_present(
    account: &AccountName,
    manifest: &BackupManifest,
    members: &[String],
) -> Result<u32, BackupError> {
    let listed: HashSet<&str> = members.iter().map(String::as_str).collect();

    for entry in &manifest.databases {
        let name =
            DatabaseName::decode(account, &entry.name).ok_or(BackupError::ManifestUnreadable)?;
        let member = dump_path(Path::new(DATABASES_DIRECTORY), &name);

        if !listed.contains(member.to_string_lossy().as_ref()) {
            return Err(BackupError::DumpMissing);
        }
    }

    Ok(u32::try_from(manifest.databases.len()).unwrap_or(u32::MAX))
}

/// Whether `path` is a plain file owned by `owner` with exactly one link, and
/// `false` when nothing is there at all.
///
/// `symlink_metadata`, so a link is judged for what it is and never followed: a
/// link wearing an artifact's name could point at anything root can read. The
/// directory it sits in was already proven root-only by the inode checks that
/// produced it, which is what makes the later open by path safe from a swap by
/// anybody but root.
///
/// # Errors
///
/// [`BackupError::ArtifactNotRegularFile`] for anything present that is not such
/// a file; [`BackupError::BackupRootUnusable`] when the entry could not be
/// stated for a reason other than its absence.
pub(super) fn is_plain_file_owned_by(path: &Path, owner: u32) -> Result<bool, BackupError> {
    let metadata = match path.symlink_metadata() {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(false),
        Err(_) => return Err(BackupError::BackupRootUnusable),
    };

    if !metadata.is_file() || metadata.nlink() != 1 || metadata.uid() != owner {
        return Err(BackupError::ArtifactNotRegularFile);
    }

    Ok(true)
}

/// Whether `candidate` is a SHA-256 digest in the form this agent writes: 64
/// lowercase hexadecimal characters.
///
/// Asked of the sidecar's value before it is compared, so a malformed digest is
/// a sidecar that cannot be trusted and not a mismatch of an unspecified kind.
pub(super) fn is_sha256_hex(candidate: &str) -> bool {
    candidate.len() == SHA256_HEX_LENGTH
        && candidate
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
}

#[cfg(test)]
#[path = "../tests/backup/verify_backup_tests.rs"]
mod tests;
