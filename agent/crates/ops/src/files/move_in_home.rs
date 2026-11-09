//! Moving or renaming one entry inside a customer's home, as the customer.

use std::ffi::OsStr;
use std::path::Path;

use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;
use crate::files::judge_movable_source::judge_movable_source;
use crate::files::model::missing_parents::MissingParents;
use crate::files::mutable_path_components::mutable_path_components;
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::refuse_taken_name::refuse_taken_name;
use crate::files::rename_failure::rename_failure;

/// Moves `source` to `destination` inside `home`, as the account owning `uid`.
///
/// One `renameat2(RENAME_NOREPLACE)` between two directory descriptors held
/// open, so the destination is never overwritten, a symlink at the destination
/// name is not followed (a rename replaces nothing and follows nothing), and a
/// directory moved into its own subtree is refused. A destination on another
/// filesystem is [`FilesOpError::CrossesDevice`] and there is no copy-and-delete
/// fallback: a move can never turn into a partial copy.
///
/// Before the rename, in this order and each for its own reason:
///
/// 1. the source's parent is reached by descriptor, every level required;
/// 2. the source is `fstatat`ed (no link followed): absent is
///    [`FilesOpError::NotFound`] (also what a retry of a move that already
///    happened sees), a symlink is [`FilesOpError::UnsupportedEntry`], and an
///    entry the account does not own is [`FilesOpError::NotOwned`], the claim
///    that survives an account handing write access around;
/// 3. the same path twice is [`FilesOpError::AlreadyExists`] and a destination
///    inside the source is [`FilesOpError::IntoItself`], decided on components
///    that are proved plain names over a walk that follows no link, so the
///    lexical position is the real one;
/// 4. the destination's parent is reached by descriptor, **created** when
///    missing (a file manager moves into a folder it has just named);
/// 5. a destination name that is taken is refused here with a typed answer, and
///    again by the kernel's own `EEXIST` for the instant between the two.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as `open_mutable_parent` does, and the verdicts listed above, plus
/// [`FilesOpError::CrossesDevice`], [`FilesOpError::QuotaExceeded`] and
/// [`FilesOpError::MutationFailed`] as `rename_failure` classifies the kernel's
/// answer.
pub(crate) fn move_in_home(
    home: &Path,
    source: &MutablePath,
    destination: &MutablePath,
    uid: u32,
) -> Result<(), FilesOpError> {
    let source_parent = open_mutable_parent(home, source, uid, MissingParents::Require)?;
    let source_name = OsStr::new(source.file_name());
    judge_movable_source(&source_parent, source_name, uid)?;

    let from = mutable_path_components(source);
    let to = mutable_path_components(destination);
    if from == to {
        return Err(FilesOpError::AlreadyExists);
    }
    if to.starts_with(&from) {
        return Err(FilesOpError::IntoItself);
    }

    let destination_parent = open_mutable_parent(home, destination, uid, MissingParents::Create)?;
    let destination_name = OsStr::new(destination.file_name());
    refuse_taken_name(&destination_parent, destination_name)?;

    rename_no_replace_between_directories(
        &source_parent,
        source_name,
        &destination_parent,
        destination_name,
    )
    .map_err(|error| rename_failure(&error))
}

#[cfg(test)]
#[path = "../tests/files/move_in_home_tests.rs"]
mod tests;
