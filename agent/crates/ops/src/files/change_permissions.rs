//! ChangePermissions: permission bits of an entry, or of a tree.

use crate::files::model::change_permissions_input::ChangePermissionsInput;
use crate::files::model::permission_report::PermissionReport;
use crate::files::{FilesHost, FilesOpError};

/// Sets permission bits inside an account's home, as the account.
///
/// Without `recursive` the one entry gets `mode`. With it a directory's tree is
/// walked: files get `mode` and directories get `directory_mode`, so a single
/// call can leave files `0644` and directories `0755`. The change is made on a
/// descriptor opened without following links, so a symlink as the path is
/// [`FilesOpError::UnsupportedEntry`] and its target is never changed; inside a
/// tree a symlink, an entry the account does not own and a directory on another
/// filesystem are skipped and counted in the report, which is how a caller learns
/// the change did not reach everything it asked for. A tree deeper than the
/// agent's ceiling is refused whole before the first bit changes. Idempotent:
/// setting the same modes twice is success both times.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`], [`FilesOpError::UnsupportedEntry`] and
/// [`FilesOpError::NotOwned`] for the path itself; [`FilesOpError::TooDeep`] and
/// [`FilesOpError::LimitReached`] for a tree too large to walk;
/// [`FilesOpError::Saturated`] when a recursive change finds the bulk bound
/// reached; and [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`] and [`FilesOpError::MutationFailed`].
pub fn change_permissions(
    host: &dyn FilesHost,
    input: &ChangePermissionsInput,
) -> Result<PermissionReport, FilesOpError> {
    host.change_permissions_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/change_permissions_tests.rs"]
mod tests;
