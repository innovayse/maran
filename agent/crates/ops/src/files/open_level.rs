//! Descending one level of a customer's tree from a descriptor already held.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};

use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::DIRECTORY_FLAGS;

/// Permission bits every directory this creates ends up with.
///
/// `0o755`: the account owns it and writes into it, and everybody else may only
/// traverse and list. The web server is "everybody else" here — it runs as its
/// own user and has to walk `sites/<domain>/.well-known/acme-challenge/` to
/// serve the token — so a narrower mode would make every issuance fail
/// validation with nothing in any log to explain it.
///
/// It is applied with an explicit `fchmod` on the newly created directory and
/// not left to `mkdirat`'s mode argument, which the daemon's umask narrows. The
/// umask is the daemon's business and the traversability of a customer's
/// challenge directory is the web server's, and a mode that is right only while
/// nobody changes the unit file is exactly the silent failure the paragraph
/// above describes. Only a directory this walk CREATED is chmodded: one that was
/// already there is left as it is, which is what `files.proto` promises.
const DIRECTORY_MODE: u32 = 0o755;

/// Permission bits `mkdirat` is asked for, before the umask narrows them.
///
/// Narrow on purpose. The directory is widened to [`DIRECTORY_MODE`] on its own
/// descriptor a moment later, so creating it at `0o700` means it is never, at
/// any instant, traversable by anybody but the account — and a crash between the
/// two steps leaves a private directory rather than a public one.
const DIRECTORY_MODE_ON_CREATE: libc::mode_t = 0o700;

/// Descends one level, creating it first if the caller asked for that.
///
/// # Errors
///
/// Returns [`FilesOpError::DirectoryUnusable`] when the level cannot be
/// created, cannot be opened, is not a directory, is a symlink, or is not owned
/// by the account.
pub(crate) fn open_level(
    directory: &File,
    name: &OsStr,
    uid: u32,
    missing: MissingParents,
) -> Result<File, FilesOpError> {
    let mut created = false;
    if missing == MissingParents::Create {
        match make_directory_in_directory(directory, name, DIRECTORY_MODE_ON_CREATE) {
            Ok(()) => created = true,
            // Already there is the ordinary case — every renewal walks the same
            // chain — and it is deliberately not trusted to be a directory. The
            // open below is what decides that, and it refuses a symlink an
            // account planted at this name.
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
            Err(_) => return Err(FilesOpError::DirectoryUnusable),
        }
    }

    let opened = open_in_directory(directory, name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::DirectoryUnusable)?;

    let metadata = opened
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?;

    // The ownership claim, and the only one of the three conditions in this
    // function that a test can observe on its own (see [`DIRECTORY_FLAGS`] for
    // why `is_dir()` cannot). Ownership and not permission, because an account
    // can hand write access around inside its own home; owning it is the claim
    // that survives.
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(FilesOpError::DirectoryUnusable);
    }

    // Only on the level this walk just made, and through the descriptor rather
    // than the name — the name is a customer's to swap, the inode is not.
    if created {
        opened
            .set_permissions(Permissions::from_mode(DIRECTORY_MODE))
            .map_err(|_| FilesOpError::DirectoryUnusable)?;
    }

    Ok(opened)
}
