//! Walking into an account's cron directory one descriptor at a time.

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::unix::fs::{MetadataExt as _, OpenOptionsExt as _};
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::cron::cron_error::CronError;

/// Flags EVERY level of the descent is opened with.
///
/// `O_NOFOLLOW` refuses a symlink at the component being opened — which is why
/// the walk opens one component at a time rather than one path — `O_DIRECTORY`
/// refuses anything that is not a directory, and `O_CLOEXEC` keeps the
/// descriptor out of anything the agent spawns.
const DIRECTORY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC;

/// Descends from `home` to the account's cron directory, one component at a
/// time.
///
/// **The descent is the containment, and a single `open` of the whole path is
/// not.** `O_NOFOLLOW` refuses a symlink at the TRAILING component only, so
/// opening `/home/<account>/.maran/cron` in one call follows a symlink planted
/// at `.maran` — which the account can plant, because the home is theirs. Each
/// level is therefore reached from the descriptor above it with `openat`, where
/// the flag applies to that level's own name; and a descriptor names an inode,
/// so a level renamed or replaced after it was opened cannot redirect the next
/// step. It is the descent `ops::files::open_parent_directory` performs, for
/// the same reason.
///
/// The components come from [`AgentPaths::ACCOUNT_CRON_DIRECTORY`] rather than
/// being written here, so this walk and the paths the rest of the agent builds
/// cannot describe two different directories.
///
/// `Ok(None)` when a level is not there — an account that has never had an
/// entry.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when a level is there and is not
/// a directory owned by `uid`: a symlink refused by `O_NOFOLLOW`, a plain file,
/// or a directory belonging to somebody else.
pub(crate) fn open_cron_directory(home: &Path, uid: u32) -> Result<Option<File>, CronError> {
    let opened = match OpenOptions::new()
        .read(true)
        .custom_flags(DIRECTORY_FLAGS)
        .open(home)
    {
        Ok(opened) => opened,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(_) => return Err(CronError::EntryFileUnreadable),
    };

    let mut directory = verify_directory(opened, uid)?;

    for component in AgentPaths::ACCOUNT_CRON_DIRECTORY.split('/') {
        let level = match open_in_directory(&directory, OsStr::new(component), DIRECTORY_FLAGS) {
            Ok(level) => level,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
            Err(_) => return Err(CronError::EntryFileUnreadable),
        };

        directory = verify_directory(level, uid)?;
    }

    Ok(Some(directory))
}

/// Refuses `opened` unless it is a directory owned by `uid`.
///
/// Applied at every level of the descent. Ownership is the second answer to the
/// symlink question rather than the only one — `O_NOFOLLOW` on each component
/// is the first — and it is the only claim that survives the account being able
/// to rename things inside its own home.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when the `fstat` fails, when the
/// thing opened is not a directory, or when it belongs to somebody else.
fn verify_directory(opened: File, uid: u32) -> Result<File, CronError> {
    let metadata = opened
        .metadata()
        .map_err(|_| CronError::EntryFileUnreadable)?;

    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(CronError::EntryFileUnreadable);
    }

    Ok(opened)
}
