//! Walking into a customer's home one descriptor at a time.

use std::ffi::OsStr;
use std::fs::{File, OpenOptions, Permissions};
use std::io;
use std::os::unix::fs::{MetadataExt, OpenOptionsExt, PermissionsExt};
use std::path::Path;

use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::relative_path::RelativePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;

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

/// Flags every directory in the walk is opened with.
///
/// `O_NOFOLLOW` refuses a symlink AT that component, and `O_CLOEXEC` keeps the
/// descriptor out of anything the agent spawns. `O_NOFOLLOW` on every component,
/// and not merely on the last, is what makes the containment a property of the
/// walk: no level of the descent can be a link, so no level can point outside
/// the home.
///
/// **`O_DIRECTORY` and the `is_dir()` check below are jointly, not
/// individually, observable, and this is where that is written down rather than
/// left for a reviewer to discover.** Either one alone can be deleted with no
/// test going red, because the other still refuses. Deleting BOTH is caught, but
/// only by [`open_parent_directory`]'s answer for a plain file at the LAST
/// level — anywhere higher, the next syscall on the opened descriptor returns
/// `ENOTDIR` and produces the same error, so the kernel, not either check, is
/// what makes the walk safe in the middle. The pair is kept because a refusal
/// that arrives at the level it belongs to is worth more than one that arrives
/// two syscalls later, and because `is_dir()` sits beside the ownership check
/// that IS individually observable; it is not kept because either half is load
/// bearing on its own. The test that catches the pair is
/// `a_plain_file_at_the_last_level_is_refused_by_the_walk_and_not_by_a_later_syscall`.
const DIRECTORY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC;

/// Opens the directory that will hold `relative`'s file, descending from `home`
/// one component at a time.
///
/// **This is where the containment of a customer file operation actually
/// happens**, and it happens without resolving a path at all. `resolve_in_home`
/// answers "where does this path really lead?" and its own documentation warns
/// that reopening by the original name afterwards reintroduces the race it
/// closes — which for a write is not a hypothetical, because the account owns
/// every directory being walked and can rename one between two syscalls. So
/// nothing here is resolved and reopened: the home is opened once, and each
/// component is reached from the descriptor above it with `openat` and
/// `O_NOFOLLOW`. A descriptor names an inode, so a level renamed, deleted or
/// replaced after it was opened does not redirect anything; and a level that is
/// a symlink is refused rather than followed.
///
/// The caller still runs this inside `fork_as_account`, as the account. The two
/// protections answer different attacks and neither replaces the other: the
/// descriptor walk stops the agent being steered to the wrong inode, and the
/// dropped uid stops it from having any interesting permission when it gets
/// there.
///
/// Every level, the home included, must be a directory the account owns.
/// Ownership and not merely permission, because an account can hand write
/// access to a directory around inside its own home; owning it is the claim
/// that survives.
///
/// `missing` decides what happens to a level that is not there — see
/// [`MissingParents`]. Creation is `mkdirat` followed by an `openat` of the same
/// name, never a create-and-assume: `mkdirat` reports `EEXIST` for a name a
/// customer occupied first, including one they occupied with a symlink, and the
/// `openat` that follows is what refuses that symlink.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] when the account's home is missing,
/// is not a directory, or is not owned by the account;
/// [`FilesOpError::DirectoryUnusable`] when a level below it cannot be created
/// or opened, is not a directory, is a symlink, or is not owned by the account.
pub(crate) fn open_parent_directory(
    home: &Path,
    relative: &RelativePath,
    uid: u32,
    missing: MissingParents,
) -> Result<File, FilesOpError> {
    let mut directory = open_home(home, uid)?;

    for component in relative.parent_components() {
        directory = open_level(&directory, OsStr::new(component.as_str()), uid, missing)?;
    }

    Ok(directory)
}

/// Opens the account's home and proves it is one.
///
/// The single point in the walk where a path is named, and it is the one path a
/// hosting account cannot rewrite: `/home` is root-owned and not writable by
/// them, so they cannot replace `/home/<account>` with a link to somewhere
/// else. Everything below this descriptor is reached by `openat` precisely
/// because everything below it IS theirs to rewrite.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] for every refusal: absent, not a
/// directory, a symlink, or owned by somebody other than the account.
fn open_home(home: &Path, uid: u32) -> Result<File, FilesOpError> {
    let opened = OpenOptions::new()
        .read(true)
        .custom_flags(DIRECTORY_FLAGS)
        .open(home)
        .map_err(|_| FilesOpError::HomeUnusable)?;

    let metadata = opened.metadata().map_err(|_| FilesOpError::HomeUnusable)?;
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(FilesOpError::HomeUnusable);
    }

    Ok(opened)
}

/// Descends one level, creating it first if the caller asked for that.
///
/// # Errors
///
/// Returns [`FilesOpError::DirectoryUnusable`] when the level cannot be
/// created, cannot be opened, is not a directory, is a symlink, or is not owned
/// by the account.
fn open_level(
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

#[cfg(test)]
#[path = "../tests/files/open_parent_directory_tests.rs"]
mod tests;
