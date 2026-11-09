//! Building the replacement for ONE directory of the home, as the account.

use std::path::Path;

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::archive::extract_home_as_account::make_staging_directory;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_spec::ExtractSpec;

/// Creates the restore's staging directory and extracts the archive's copy of
/// `directory` into it AS the account (issue #160).
///
/// Everything `extract_home_as_account` argues for holds here unchanged: the
/// account and never root (R3), the artifact handed over as an open descriptor,
/// no `--absolute-names`, the staging root repaired and not merely created. What
/// differs is the selection: the archiver is asked for the one member
/// `home/<directory>` and drops the leading components, so the directory lands
/// as the first entry of `staging` under its own name, ready to be moved into
/// place by one rename.
///
/// A directory the archive does not hold is refused here, from the member list
/// the pre-scan already read, and not discovered as an archiver failure: `tar`
/// answers a missing member with an exit status that says nothing about which
/// member, and "the site did not exist when the backup was taken" is a finding
/// the customer can act on.
///
/// `owner` is the account's uid and gid as the caller resolved them, passed in for
/// the reason `extract_home_as_account` gives.
///
/// # Errors
///
/// - [`BackupError::DirectoryNotInBackup`] when no member of `members` is the
///   directory itself or lies inside it. Nothing was created.
/// - [`BackupError::StagingUnusable`], [`BackupError::ArchiveFailed`] and
///   [`BackupError::ExtractionIdentityUnavailable`] as `extract_home_as_account`
///   documents them.
pub(crate) fn extract_directory_as_account(
    host: &dyn BackupHost,
    artifact: &Path,
    members: &[String],
    staging: &Path,
    account: &AccountName,
    directory: &RelativePath,
    owner: &AccountIdentity,
) -> Result<(), BackupError> {
    require_member(members, directory)?;

    make_staging_directory(staging, owner.uid, owner.gid)?;

    host.extract(&ExtractSpec {
        artifact: artifact.to_path_buf(),
        into: staging.to_path_buf(),
        part: ArchivePart::Directory {
            account: account.clone(),
            directory: directory.clone(),
        },
    })
}

/// Refuses when the archive's member list has nothing at or under `directory`.
///
/// Compared on whole path components: `home/sites/shop.example.com` is not
/// evidence for `home/sites/shop.example`, which a prefix test on the text would
/// accept.
///
/// # Errors
///
/// [`BackupError::DirectoryNotInBackup`].
fn require_member(members: &[String], directory: &RelativePath) -> Result<(), BackupError> {
    let wanted = format!("home/{}", directory.as_path().to_string_lossy());

    let held = members.iter().any(|member| {
        let member = member.trim_end_matches('/');
        member == wanted || member.starts_with(&format!("{wanted}/"))
    });

    if held {
        Ok(())
    } else {
        Err(BackupError::DirectoryNotInBackup)
    }
}

#[cfg(test)]
#[path = "../../tests/backup/archive/extract_directory_as_account_tests.rs"]
mod tests;
