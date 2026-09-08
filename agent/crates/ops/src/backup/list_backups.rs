//! ListBackups: every artifact a local destination is holding for an account.

use std::ffi::OsStr;
use std::fs::read_dir;
use std::os::unix::ffi::OsStrExt;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_root::open_account_directory_owned_by;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::unreadable_reason::UnreadableReason;
use crate::backup::object_key::sidecar_file_name;
use crate::backup::read_sidecar::read_sidecar;

/// The uid a backup directory must belong to on a real host.
///
/// The same value `backup_root.rs` holds and for the same reason; it is spelled
/// here because this file is what supplies the argument its injectable opener
/// takes, and a listing that passed anything else would be approving a
/// directory root does not own.
const ROOT_UID: u32 = 0;

/// Lists every backup published for `account` under `root`.
///
/// **Every artifact this account has, readable sidecar or not.** A sidecar
/// that is missing or does not parse is reported as
/// [`BackupSummary::unreadable`] rather than left out of the list — see that
/// type's doc for why a silent skip here is the defect this function exists
/// to not have: an entry retention cannot see is an entry it will never
/// prune, and a disk that only ever grows is invisible right up until it is
/// full.
///
/// An entry whose sidecar parses but names a version this agent does not
/// understand is also reported rather than skipped, with
/// [`BackupSummary::reason`] set to
/// [`UnreadableReason::UnknownVersion`] —
/// see that type's doc for why it is kept apart from a genuinely corrupt
/// sidecar.
///
/// A `.partial` artifact — one a creation never finished — is never listed,
/// whether or not the run that left it behind also removed it: this reads the
/// directory itself rather than trusting that cleanup.
///
/// # What else the directory can hold, and what happens to it
///
/// Three things can wear an artifact's name without being one, and this
/// function used to answer all three with silence — which is the exact defect
/// the paragraphs above exist to refuse, applied to the entries a listing
/// never got as far as reading a sidecar for:
///
/// - **A name that is not valid UTF-8, but ends in `.tar.gz`.** Refused,
///   [`BackupError::UnmintedArtifactName`]. The suffix is tested on the raw
///   bytes so such a name is SEEN rather than dropped at the string
///   conversion, which is where it used to disappear.
/// - **A `.tar.gz` whose stem is not a [`BackupId`].** Refused the same way.
///   Neither of these can be listed, because the only field that could carry
///   them is `backup_id`, and putting a file name in the field the panel
///   sends back as an identifier would make every later caller's id
///   untrustworthy — the litter is real, but it is not a backup and must not
///   be given a backup's identity.
/// - **A directory or a symbolic link named `<valid id>.tar.gz`.** Listed,
///   unreadable, [`UnreadableReason::NotARegularFile`]. This one has a real
///   id, so it can be reported honestly, and it must be: listed as an
///   ordinary backup, a directory is an entry retention can never prune and a
///   symbolic link is one a restore would read through. The entry's own type
///   answers this, from what `read_dir` already returned, and does not follow
///   a link.
///
/// An account with no backups yet lists as empty, not as an error — and
/// **nothing is created to find that out.**
///
/// This used to call `prepare_account_directory`, which creates the account's
/// directory, and `backup_root.rs`'s own doc argues against exactly that: "an
/// operation that only reads or removes has no business leaving an inode
/// behind". A listing is the purest read this area has, and it was the one
/// caller contradicting the rule the file it called was written around. It now
/// goes through `open_account_directory_owned_by`, the entry point that creates
/// nothing
/// and answers `None` for an account that has none — and `None` is an empty
/// list, because "this account has never been backed up" and "this account's
/// directory holds no artifacts" are the same answer to the question a caller
/// asked.
///
/// The inode checks are not weakened by the change: that entry point applies the same `require_root_only` to the root and to the account's
/// directory, refusing a symlink or a directory anybody but root can reach. The
/// only thing dropped is the side effect.
///
/// # Errors
///
/// Returns a [`BackupError`] variant from `open_account_directory_owned_by`
/// when the
/// backup root or the account's directory under it is not what the inode says a
/// backup directory must be, [`BackupError::BackupRootUnusable`] when the
/// directory cannot be read, or [`BackupError::UnmintedArtifactName`] when it
/// holds an artifact name this agent could never have written.
pub fn list_backups(
    root: &LocalBackupRoot,
    account: &AccountName,
) -> Result<Vec<BackupSummary>, BackupError> {
    let resolved = root
        .resolve()
        .map_err(|_| BackupError::BackupRootUnsafe { uid: 0, mode: 0 })?;

    list_under(&resolved, account, ROOT_UID)
}

/// The body of [`list_backups`], with the resolved root and the expected owner
/// injected.
///
/// Split for the reason `backup_root`'s own entry points are split: a test runs
/// as an ordinary user and cannot create a directory owned by uid 0, so a
/// listing that resolved root itself could only ever be exercised on its
/// refusing path — and the thing most worth observing here is not a refusal but
/// an ABSENCE. That a listing of an account with no directory answers an empty
/// list **and leaves no directory behind** is the whole of Finding 8's fix, and
/// it is a fact about this function rather than about
/// `open_account_directory`, which has its own tests. Without the injected
/// owner, swapping this call back to the creating entry point would pass every
/// test in the suite.
///
/// # Errors
///
/// As documented on [`list_backups`].
fn list_under(
    root: &Path,
    account: &AccountName,
    owner: u32,
) -> Result<Vec<BackupSummary>, BackupError> {
    match open_account_directory_owned_by(root, account, owner)? {
        Some(directory) => list_in(&directory),
        None => Ok(Vec::new()),
    }
}

/// The body of [`list_backups`], with the account's directory injected.
///
/// Split for the reason creation's own body is split (`create_backup.rs`):
/// a directory a test can really create is never one
/// `open_account_directory_owned_by` would accept on a machine that is not
/// root, so
/// the listing logic itself is exercised against a directory the test made,
/// leaving only the inode check itself untested here — that check has its own
/// tests in `backup_root_tests.rs`.
///
/// # Errors
///
/// [`BackupError::BackupRootUnusable`] when `directory` cannot be read, and
/// [`BackupError::UnmintedArtifactName`] when it holds an artifact name this
/// agent could never have written.
fn list_in(directory: &Path) -> Result<Vec<BackupSummary>, BackupError> {
    let entries = read_dir(directory).map_err(|_| BackupError::BackupRootUnusable)?;

    let mut summaries = Vec::new();
    for entry in entries {
        let entry = entry.map_err(|_| BackupError::BackupRootUnusable)?;
        let name = entry.file_name();

        // On the raw bytes, not on a `&str`: a name that is not valid UTF-8
        // used to vanish at the conversion, before anything had asked whether
        // it was claiming to be an artifact at all. The suffix test itself is
        // pure ASCII, so it is exact on bytes, and `.tar.gz.partial` still
        // does not end with `.tar.gz`.
        let Some(stem) = name
            .as_bytes()
            .strip_suffix(AgentPaths::BACKUP_ARTIFACT_SUFFIX.as_bytes())
        else {
            continue;
        };

        // A name this agent never minted is not a backup, whatever it is
        // sitting beside — and it is not litter this function may pass over
        // either. An id is the only thing that stands between this list and a
        // path it goes on to build, so an entry that fails to parse as one is
        // named to the operator rather than skipped.
        let Ok(id) = std::str::from_utf8(stem)
            .map_err(|_| ())
            .and_then(|stem| BackupId::parse(stem).map_err(|_| ()))
        else {
            return Err(BackupError::UnmintedArtifactName {
                name: OsStr::to_string_lossy(&name).into_owned(),
            });
        };

        // `file_type` here is the entry's own type as `read_dir` reported it:
        // it does not follow a symbolic link, so a link to a real artifact is
        // refused as the link it is rather than accepted as the file it points
        // at. A backup is a regular file or it is not a backup.
        let is_file = entry
            .file_type()
            .map_err(|_| BackupError::BackupRootUnusable)?
            .is_file();
        if !is_file {
            summaries.push(BackupSummary::unreadable(
                id.as_str().to_owned(),
                UnreadableReason::NotARegularFile,
            ));
            continue;
        }

        summaries.push(read_summary(directory, &id));
    }

    Ok(summaries)
}

/// Reads and parses the sidecar for `id` through
/// [`read_sidecar`] — the version
/// check happens there, not here, so this function only has to translate its
/// outcome into the [`BackupSummary`] variant a listing reports.
///
/// The id is taken from the ARTIFACT's own file name, never from whatever the
/// sidecar's own copy says — the artifact is the thing on disk this entry
/// describes, and its name is not in question the way the sidecar's contents
/// are.
fn read_summary(directory: &Path, id: &BackupId) -> BackupSummary {
    let sidecar_path = directory.join(sidecar_file_name(id));

    match read_sidecar(&sidecar_path) {
        Ok(details) => BackupSummary::readable(
            id.as_str().to_owned(),
            details.manifest,
            details.artifact_bytes,
            details.artifact_sha256,
        ),
        Err(reason) => BackupSummary::unreadable(id.as_str().to_owned(), reason),
    }
}

#[cfg(test)]
#[path = "../tests/backup/list_backups_tests.rs"]
mod tests;
