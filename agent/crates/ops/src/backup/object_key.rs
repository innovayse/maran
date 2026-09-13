//! The one place a backup's file name — and the key ending in it — is built.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::s3_object_prefix::S3ObjectPrefix;

/// The file name one backup's artifact wears, in a directory or in a key.
///
/// Every caller that needs it goes through here: this function,
/// [`sidecar_file_name`] and
/// [`AgentPaths::BACKUP_ARTIFACT_SUFFIX`] together replace what were five
/// hand-kept spellings of `.tar.gz` and four of `.meta.json`. The rule's test
/// is met and not merely resembled — `restore_backup`'s
/// `format!("{}.tar.gz", …)` could be replaced by a call to `create_backup`'s
/// `AgentPaths` helper with **nothing observable changing**: same string, same
/// directory, same failure modes. That is a copy, and it was the third and
/// fourth.
///
/// Built from [`AgentPaths`] rather than from a constant of this area's own, so
/// that "what a backup file is called" has exactly one answer in the workspace
/// and an artifact fetched from a bucket and one opened from
/// `/var/backups/maran` are the same `.tar.gz` by construction rather than by
/// two files agreeing.
///
/// Only the NAME is borrowed from [`AgentPaths`], never the directory: the
/// directory comes from the operator's configured root, which [`AgentPaths`]
/// does not know about. That is why this takes only a [`BackupId`] and no
/// account.
pub(crate) fn artifact_file_name(backup_id: &BackupId) -> String {
    format!(
        "{}{}",
        backup_id.as_str(),
        AgentPaths::BACKUP_ARTIFACT_SUFFIX
    )
}

/// The file name one backup's sidecar wears, beside its artifact.
///
/// The counterpart to [`artifact_file_name`], for the same reason and from the
/// same source.
pub(crate) fn sidecar_file_name(backup_id: &BackupId) -> String {
    format!(
        "{}{}",
        backup_id.as_str(),
        AgentPaths::BACKUP_SIDECAR_SUFFIX
    )
}

/// Builds the key one backup's artifact is stored under.
///
/// `<prefix>/<account>/<backup-id>.tar.gz`, and — this is the whole point —
/// **nothing else**. Three segments, from three sources, each of which is a
/// value that has already been parsed:
///
/// - the prefix is the operator's, and [`S3ObjectPrefix`] has already refused a
///   leading `/`, a `..` segment and every byte outside `[A-Za-z0-9/_-]`;
/// - the account name is [`AccountName`], which is a system username;
/// - the id is [`BackupId`], which is a lowercase hyphenated uuid this agent
///   minted.
///
/// So there is no escaping here and no sanitising, because there is nothing
/// left to escape — the types are the check, and a `&str` parameter in this
/// signature would be the hole (rules/rust.md "Validation first"). Nothing from
/// a request body reaches this function; a caller that wanted to put a
/// customer-chosen string into a key would have to add a parameter, which is a
/// diff a reviewer sees.
///
/// The reason it matters that the key is derived and not supplied: a key is a
/// path in the destination's namespace. A caller-chosen `../` or an absolute
/// segment aims a write at another account's prefix — the same defect as a path
/// traversal, in a namespace where there is no kernel to refuse it.
///
/// An empty prefix is legal ([`S3ObjectPrefix`] allows it) and produces
/// `<account>/<backup-id>.tar.gz` with no leading separator: a key beginning
/// with `/` names an object whose first path segment is empty, which is a
/// different object from the one every other part of this product expects.
#[must_use]
pub fn object_key(prefix: &S3ObjectPrefix, account: &AccountName, backup_id: &BackupId) -> String {
    format!(
        "{}{}",
        account_key_prefix(prefix, account),
        artifact_file_name(backup_id)
    )
}

/// The key prefix every artifact of one account shares, separator included.
///
/// `<prefix>/<account>/`, or `<account>/` when the operator configured no
/// prefix. This is what a listing asks the destination for, and it is derived
/// by the same function that builds the keys themselves so that "where does
/// this account's backups live" cannot have two answers — a listing prefix that
/// drifted from the key builder is a listing that silently returns nothing,
/// which retention would read as "this account has no backups".
///
/// Crate-private: it is a detail of how this area addresses a destination, and
/// no caller outside it composes keys.
pub(crate) fn account_key_prefix(prefix: &S3ObjectPrefix, account: &AccountName) -> String {
    if prefix.is_empty() {
        format!("{}/", account.as_str())
    } else {
        format!("{}/{}/", prefix.as_str(), account.as_str())
    }
}

#[cfg(test)]
#[path = "../tests/backup/object_key_tests.rs"]
mod tests;
